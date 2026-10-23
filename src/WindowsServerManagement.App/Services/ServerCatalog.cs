using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services.Validation;

namespace WindowsServerManagement.App.Services;

public sealed partial class ServerCatalog : ObservableObject
{
    private readonly IServerProfileRepository _profiles;
    private readonly ISecretStore _secrets;
    private readonly IAppSettingsService _settings;
    private readonly IServerSessionManager _sessions;
    private readonly IIisWebsiteService _iis;
    private readonly IDialogService _dialogs;
    private readonly IActivityLogService _activity;
    private readonly ILogger<ServerCatalog> _logger;

    [ObservableProperty]
    private ServerItemViewModel? _selectedServer;

    public ServerCatalog(
        IServerProfileRepository profiles,
        ISecretStore secrets,
        IAppSettingsService settings,
        IServerSessionManager sessions,
        IIisWebsiteService iis,
        IDialogService dialogs,
        IActivityLogService activity,
        ILogger<ServerCatalog> logger)
    {
        _profiles = profiles;
        _secrets = secrets;
        _settings = settings;
        _sessions = sessions;
        _iis = iis;
        _dialogs = dialogs;
        _activity = activity;
        _logger = logger;
        _sessions.StatusChanged += OnSessionStatusChanged;
    }

    public ObservableCollection<ServerItemViewModel> Servers { get; } = [];
    public event EventHandler<ServerItemViewModel>? ServerConnected;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var profiles = await _profiles.LoadAsync(cancellationToken).ConfigureAwait(true);
        Servers.Clear();
        foreach (var profile in profiles)
        {
            Servers.Add(new ServerItemViewModel(profile));
        }

        var selected = Servers.FirstOrDefault(item => item.Id == _settings.Current.LastSelectedServerId)
                       ?? Servers.FirstOrDefault();
        SelectedServer = selected;
    }

    public async Task<ServerProfile> SaveProfileAsync(
        ServerProfile candidate,
        string? suppliedSecret,
        bool clearStoredSecret,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var existing = Servers.FirstOrDefault(item => item.Id == candidate.Id);
        var resolvedSecret = await ResolveSecretAsync(candidate, suppliedSecret, clearStoredSecret, cancellationToken)
            .ConfigureAwait(true);

        var validation = ServerProfileValidator.Validate(
            candidate,
            hasStoredSecret: !resolvedSecret.IsEmpty,
            newSecret: resolvedSecret.Password ?? resolvedSecret.Passphrase);
        validation.Errors.AddRange(ServerProfileValidator.ValidateProfileUniqueness(
                Servers.Select(item => item.Profile),
                candidate)
            .Errors);

        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        candidate.Name = candidate.Name.Trim();
        candidate.Host = candidate.Host.Trim();
        candidate.Username = candidate.Username.Trim();
        candidate.PrivateKeyPath = string.IsNullOrWhiteSpace(candidate.PrivateKeyPath)
            ? null
            : candidate.PrivateKeyPath.Trim();
        candidate.UpdatedAt = DateTimeOffset.UtcNow;
        candidate.HasStoredSecret = !resolvedSecret.IsEmpty;

        if (existing is null)
        {
            candidate.CreatedAt = DateTimeOffset.UtcNow;
        }
        else
        {
            candidate.CreatedAt = existing.Profile.CreatedAt;
            candidate.LastConnectedAt = existing.Profile.LastConnectedAt;

            if (existing.IsConnected && RequiresReconnect(existing.Profile, candidate))
            {
                await _sessions.DisconnectAsync(candidate.Id, cancellationToken).ConfigureAwait(true);
            }
        }

        if (resolvedSecret.IsEmpty)
        {
            await _secrets.DeleteAsync(candidate.Id, cancellationToken).ConfigureAwait(true);
        }
        else
        {
            await _secrets.SaveAsync(candidate.Id, resolvedSecret, cancellationToken).ConfigureAwait(true);
        }

        await _profiles.SaveAsync(candidate, cancellationToken).ConfigureAwait(true);

        if (existing is null)
        {
            var item = new ServerItemViewModel(candidate);
            Servers.Add(item);
            SelectedServer = item;
            await LogAsync(
                candidate,
                "Server profile added",
                ActivityStatus.Success,
                $"Added server profile '{candidate.Name}'.",
                cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        else
        {
            existing.UpdateProfile(candidate);
            await LogAsync(
                candidate,
                "Server profile updated",
                ActivityStatus.Success,
                $"Updated server profile '{candidate.Name}'.",
                cancellationToken: cancellationToken).ConfigureAwait(true);
        }

        return candidate;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        ServerProfile draft,
        string? suppliedSecret,
        bool clearStoredSecret,
        CancellationToken cancellationToken = default)
    {
        var secret = await ResolveSecretAsync(draft, suppliedSecret, clearStoredSecret, cancellationToken)
            .ConfigureAwait(true);

        var validation = ServerProfileValidator.Validate(
            draft,
            hasStoredSecret: !secret.IsEmpty,
            newSecret: secret.Password ?? secret.Passphrase);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        if (draft.AuthenticationMethod == AuthenticationMethod.Password && string.IsNullOrEmpty(secret.Password))
        {
            throw new ValidationException("Enter a password before testing the connection.");
        }

        var expectation = string.IsNullOrWhiteSpace(draft.TrustedHostKeyFingerprint)
            ? HostKeyExpectation.FirstUse
            : new HostKeyExpectation(draft.TrustedHostKeyFingerprint, IsPinned: true);

        ConnectionTestResult result;
        try
        {
            result = await _sessions.TestConnectionAsync(draft, secret, expectation, cancellationToken).ConfigureAwait(true);
        }
        catch (HostKeyVerificationException exception) when (!exception.IsMismatch)
        {
            var trust = await _dialogs.ConfirmAsync(
                    "Trust this SSH host key?",
                    BuildHostKeyPrompt(draft, exception.Fingerprint),
                    "Trust and continue",
                    "Cancel",
                    dangerous: true)
                .ConfigureAwait(true);

            if (!trust)
            {
                await LogAsync(
                    draft,
                    "Connection test cancelled",
                    ActivityStatus.Warning,
                    "The SSH host key was not trusted, so the connection test was cancelled.",
                    cancellationToken: cancellationToken).ConfigureAwait(true);
                return new ConnectionTestResult(
                    Success: false,
                    ManagementReady: false,
                    Message: "The connection test was cancelled because the SSH host key was not trusted.",
                    HostKeyFingerprint: exception.Fingerprint,
                    Capabilities: null);
            }

            draft.TrustedHostKeyFingerprint = exception.Fingerprint;
            result = await _sessions.TestConnectionAsync(
                    draft,
                    secret,
                    new HostKeyExpectation(exception.Fingerprint, IsPinned: false),
                    cancellationToken)
                .ConfigureAwait(true);
        }

        if (!string.IsNullOrWhiteSpace(result.HostKeyFingerprint))
        {
            draft.TrustedHostKeyFingerprint = result.HostKeyFingerprint;
        }

        await LogAsync(
            draft,
            "Connection test",
            result.Success ? (result.ManagementReady ? ActivityStatus.Success : ActivityStatus.Warning) : ActivityStatus.Error,
            result.Message,
            cancellationToken: cancellationToken).ConfigureAwait(true);

        return result;
    }

    public async Task<bool> ConnectAsync(ServerItemViewModel item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.IsConnected)
        {
            return true;
        }

        if (item.IsBusy)
        {
            return false;
        }

        item.IsBusy = true;
        try
        {
            var secret = await ResolveSecretAsync(item.Profile, null, clearStoredSecret: false, cancellationToken)
                .ConfigureAwait(true);

            var validation = ServerProfileValidator.Validate(
                item.Profile,
                hasStoredSecret: !secret.IsEmpty,
                newSecret: secret.Password ?? secret.Passphrase);
            if (!validation.IsValid)
            {
                throw new ValidationException(validation.Errors);
            }

            if (item.Profile.AuthenticationMethod == AuthenticationMethod.Password && string.IsNullOrEmpty(secret.Password))
            {
                throw new ValidationException("A saved password is required. Edit the server profile and enter the password.");
            }

            var expectation = string.IsNullOrWhiteSpace(item.Profile.TrustedHostKeyFingerprint)
                ? HostKeyExpectation.FirstUse
                : new HostKeyExpectation(item.Profile.TrustedHostKeyFingerprint, IsPinned: true);

            try
            {
                await _sessions.ConnectAsync(item.Profile, secret, expectation, cancellationToken).ConfigureAwait(true);
            }
            catch (HostKeyVerificationException exception) when (!exception.IsMismatch)
            {
                var trust = await _dialogs.ConfirmAsync(
                        "Trust this SSH host key?",
                        BuildHostKeyPrompt(item.Profile, exception.Fingerprint),
                        "Trust and connect",
                        "Cancel",
                        dangerous: true)
                    .ConfigureAwait(true);

                if (!trust)
                {
                    await LogAsync(
                        item.Profile,
                        "Connect",
                        ActivityStatus.Warning,
                        "Connection cancelled because the SSH host key was not trusted.",
                        cancellationToken: cancellationToken).ConfigureAwait(true);
                    throw new ValidationException("The SSH host key was not trusted. The connection was cancelled.");
                }

                item.Profile.TrustedHostKeyFingerprint = exception.Fingerprint;
                await _sessions.ConnectAsync(
                        item.Profile,
                        secret,
                        new HostKeyExpectation(exception.Fingerprint, IsPinned: false),
                        cancellationToken)
                    .ConfigureAwait(true);
            }

            item.Profile.LastConnectedAt = DateTimeOffset.Now;
            item.Profile.UpdatedAt = DateTimeOffset.UtcNow;
            await _profiles.SaveAsync(item.Profile, cancellationToken).ConfigureAwait(true);
            item.UpdateProfile(item.Profile);
            await LogAsync(
                item.Profile,
                "Connect",
                ActivityStatus.Success,
                $"Connected to {item.Profile.Endpoint} as {item.Profile.Username}.",
                cancellationToken: cancellationToken).ConfigureAwait(true);

            try
            {
                var capabilities = await _iis.GetCapabilitiesAsync(item.Id, forceRefresh: true, cancellationToken)
                    .ConfigureAwait(true);
                item.Capabilities = capabilities;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Capability check failed after connecting to profile {ProfileId}.", item.Id);
            }

            ServerConnected?.Invoke(this, item);
            return true;
        }
        catch (OperationCanceledException)
        {
            await LogAsync(
                item.Profile,
                "Connect",
                ActivityStatus.Warning,
                "Connection attempt was cancelled.",
                cancellationToken: CancellationToken.None).ConfigureAwait(true);
            throw;
        }
        catch (Exception exception)
        {
            await LogAsync(
                item.Profile,
                "Connect",
                ActivityStatus.Error,
                "Connection failed.",
                exception.Message,
                cancellationToken: CancellationToken.None).ConfigureAwait(true);
            throw;
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public async Task EnsureConnectedAsync(ServerItemViewModel item, CancellationToken cancellationToken = default)
    {
        if (item.IsConnected)
        {
            return;
        }

        await ConnectAsync(item, cancellationToken).ConfigureAwait(true);
        if (!item.IsConnected)
        {
            throw new NotConnectedException(item.Name);
        }
    }

    public async Task DisconnectAsync(ServerItemViewModel item, CancellationToken cancellationToken = default)
    {
        if (!item.IsConnected)
        {
            return;
        }

        item.IsBusy = true;
        try
        {
            await _sessions.DisconnectAsync(item.Id, cancellationToken).ConfigureAwait(true);
            await LogAsync(
                item.Profile,
                "Disconnect",
                ActivityStatus.Info,
                $"Disconnected from {item.Profile.Endpoint}.",
                cancellationToken: cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public async Task ReconnectAsync(ServerItemViewModel item, CancellationToken cancellationToken = default)
    {
        await DisconnectAsync(item, cancellationToken).ConfigureAwait(true);
        await ConnectAsync(item, cancellationToken).ConfigureAwait(true);
    }

    public async Task DeleteProfileAsync(ServerItemViewModel item, CancellationToken cancellationToken = default)
    {
        if (item.IsConnected)
        {
            await DisconnectAsync(item, cancellationToken).ConfigureAwait(true);
        }

        await _profiles.DeleteAsync(item.Id, cancellationToken).ConfigureAwait(true);
        await _secrets.DeleteAsync(item.Id, cancellationToken).ConfigureAwait(true);
        Servers.Remove(item);

        if (ReferenceEquals(SelectedServer, item))
        {
            SelectedServer = Servers.FirstOrDefault();
        }

        await LogAsync(
            item.Profile,
            "Server profile deleted",
            ActivityStatus.Warning,
            $"Deleted server profile '{item.Profile.Name}'. No remote resources were changed.",
            cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    public async Task ForgetHostKeyAsync(ServerItemViewModel item, CancellationToken cancellationToken = default)
    {
        if (item.IsConnected)
        {
            await DisconnectAsync(item, cancellationToken).ConfigureAwait(true);
        }

        item.Profile.TrustedHostKeyFingerprint = null;
        item.Profile.UpdatedAt = DateTimeOffset.UtcNow;
        await _profiles.SaveAsync(item.Profile, cancellationToken).ConfigureAwait(true);
        item.UpdateProfile(item.Profile);

        await LogAsync(
            item.Profile,
            "Trusted host key reset",
            ActivityStatus.Warning,
            $"Forgot the trusted SSH host key for '{item.Profile.Name}'. The next connection will require review.",
            cancellationToken: cancellationToken).ConfigureAwait(true);
    }

    public async Task<CapabilityReport?> RefreshCapabilitiesAsync(ServerItemViewModel item, CancellationToken cancellationToken = default)
    {
        if (!item.IsConnected)
        {
            return item.Capabilities;
        }

        var capabilities = await _iis.GetCapabilitiesAsync(item.Id, forceRefresh: true, cancellationToken)
            .ConfigureAwait(true);
        item.Capabilities = capabilities;
        return capabilities;
    }

    public async Task<ServerSecret> GetSecretAsync(Guid profileId, CancellationToken cancellationToken = default)
    {
        return await _secrets.GetAsync(profileId, cancellationToken).ConfigureAwait(true) ?? new ServerSecret();
    }

    partial void OnSelectedServerChanged(ServerItemViewModel? value)
    {
        _ = PersistSelectedServerAsync(value?.Id);
    }

    private async Task PersistSelectedServerAsync(Guid? serverId)
    {
        try
        {
            var settings = _settings.Current.Clone();
            settings.LastSelectedServerId = serverId;
            await _settings.SaveAsync(settings, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Could not persist the selected server.");
        }
    }

    private async Task<ServerSecret> ResolveSecretAsync(
        ServerProfile profile,
        string? suppliedSecret,
        bool clearStoredSecret,
        CancellationToken cancellationToken)
    {
        if (clearStoredSecret)
        {
            return new ServerSecret();
        }

        if (!string.IsNullOrEmpty(suppliedSecret))
        {
            return profile.AuthenticationMethod == AuthenticationMethod.Password
                ? new ServerSecret { Password = suppliedSecret }
                : new ServerSecret { Passphrase = suppliedSecret };
        }

        return await _secrets.GetAsync(profile.Id, cancellationToken).ConfigureAwait(false) ?? new ServerSecret();
    }

    private void OnSessionStatusChanged(object? sender, ServerConnectionStatus status)
    {
        OnUi(() =>
        {
            var item = Servers.FirstOrDefault(server => server.Id == status.ServerId);
            item?.ApplyConnectionState(status.State, status.Message, status.State == ConnectionState.Error ? status.Message : null);
        });
    }

    private async Task LogAsync(
        ServerProfile profile,
        string operation,
        ActivityStatus status,
        string message,
        string? details = null,
        CancellationToken cancellationToken = default)
    {
        await _activity.LogAsync(new ActivityLogEntry
        {
            ServerId = profile.Id,
            ServerName = profile.Name,
            Operation = operation,
            Status = status,
            Message = message,
            Details = details
        }, cancellationToken).ConfigureAwait(false);
    }

    private static bool RequiresReconnect(ServerProfile current, ServerProfile candidate)
    {
        return !string.Equals(current.Host, candidate.Host, StringComparison.OrdinalIgnoreCase) ||
               current.Port != candidate.Port ||
               !string.Equals(current.Username, candidate.Username, StringComparison.Ordinal) ||
               current.AuthenticationMethod != candidate.AuthenticationMethod ||
               !string.Equals(current.PrivateKeyPath, candidate.PrivateKeyPath, StringComparison.OrdinalIgnoreCase) ||
               !string.Equals(current.TrustedHostKeyFingerprint, candidate.TrustedHostKeyFingerprint, StringComparison.Ordinal);
    }

    private static string BuildHostKeyPrompt(ServerProfile profile, string fingerprint)
    {
        return $"""
               The server '{profile.Host}:{profile.Port}' presented this SSH host key:

               SHA256:{fingerprint}

               Verify this fingerprint against the server administrator or an out-of-band source before trusting it. Trusting the wrong key could allow a man-in-the-middle attack. The fingerprint is persisted only after a successful connection.
               """;
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }
}

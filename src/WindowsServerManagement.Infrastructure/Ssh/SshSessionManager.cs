using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using Renci.SshNet.Common;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Interfaces;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services.Remote;
using WindowsServerManagement.Core.Services.Validation;

namespace WindowsServerManagement.Infrastructure.Ssh;

public sealed class SshSessionManager : IServerSessionManager, IDisposable
{
    private readonly ConcurrentDictionary<Guid, ServerSession> _sessions = new();
    private readonly ConcurrentDictionary<Guid, ConnectionState> _states = new();
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private readonly IAppSettingsService _settings;
    private readonly ILogger<SshSessionManager> _logger;
    private bool _disposed;

    public SshSessionManager(IAppSettingsService settings, ILogger<SshSessionManager> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public event EventHandler<ServerConnectionStatus>? StatusChanged;

    public bool IsConnected(Guid serverId)
    {
        return _sessions.TryGetValue(serverId, out var session) && IsClientConnected(session.Client);
    }

    public ConnectionState GetState(Guid serverId)
    {
        if (_sessions.TryGetValue(serverId, out var session))
        {
            return IsClientConnected(session.Client) ? ConnectionState.Connected : ConnectionState.Error;
        }

        return _states.TryGetValue(serverId, out var state) ? state : ConnectionState.Disconnected;
    }

    public async Task ConnectAsync(
        ServerProfile profile,
        ServerSecret secret,
        HostKeyExpectation hostKeyExpectation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateProfile(profile);

        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_sessions.ContainsKey(profile.Id))
            {
                await DisconnectUnlockedAsync(profile.Id, cancellationToken).ConfigureAwait(false);
            }

            RaiseStatus(profile.Id, ConnectionState.Connecting, "Opening SSH connection...", null);
            _states[profile.Id] = ConnectionState.Connecting;

            var timeout = TimeSpan.FromSeconds(_settings.Current.ConnectionTimeoutSeconds);
            SshClient? client = null;
            HostKeyObservation? observation = null;

            try
            {
                (client, observation) = BuildClient(profile, secret, hostKeyExpectation, timeout);
                using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutSource.CancelAfter(timeout);

                await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);

                var session = new ServerSession(
                    client,
                    profile.Name,
                    observation.Fingerprint ?? hostKeyExpectation.Fingerprint ?? string.Empty,
                    new SemaphoreSlim(1, 1));

                _sessions[profile.Id] = session;
                _states[profile.Id] = ConnectionState.Connected;
                _logger.LogInformation(
                    "SSH connection established to {Host}:{Port} as {User} (profile {ProfileId}).",
                    profile.Host,
                    profile.Port,
                    profile.Username,
                    profile.Id);
                RaiseStatus(profile.Id, ConnectionState.Connected, "Connected", session.HostKeyFingerprint);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                client?.Dispose();
                _states[profile.Id] = ConnectionState.Disconnected;
                RaiseStatus(profile.Id, ConnectionState.Disconnected, "Connection cancelled.", null);
                throw;
            }
            catch (OperationCanceledException)
            {
                client?.Dispose();
                var message = $"The connection to '{profile.Host}' timed out after {_settings.Current.ConnectionTimeoutSeconds} seconds.";
                _states[profile.Id] = ConnectionState.Error;
                RaiseStatus(profile.Id, ConnectionState.Error, message, null);
                throw new RemoteOperationException(message);
            }
            catch (SshOperationTimeoutException)
            {
                client?.Dispose();
                var message = $"The connection to '{profile.Host}' timed out after {_settings.Current.ConnectionTimeoutSeconds} seconds.";
                _states[profile.Id] = ConnectionState.Error;
                RaiseStatus(profile.Id, ConnectionState.Error, message, null);
                throw new RemoteOperationException(message);
            }
            catch (Exception exception)
            {
                client?.Dispose();

                if (observation?.Fingerprint is { Length: > 0 } fingerprint &&
                    !FingerprintsEqual(hostKeyExpectation.Fingerprint, fingerprint))
                {
                    var hostKeyException = new HostKeyVerificationException(
                        profile.Host,
                        fingerprint,
                        isMismatch: hostKeyExpectation.IsPinned);
                    _states[profile.Id] = ConnectionState.Error;
                    RaiseStatus(profile.Id, ConnectionState.Error, hostKeyException.Message, fingerprint);
                    _logger.LogWarning(
                        "SSH host key verification failed for {Host}. Mismatch: {IsMismatch}.",
                        profile.Host,
                        hostKeyExpectation.IsPinned);
                    throw hostKeyException;
                }

                var message = BuildConnectionErrorMessage(profile, exception);
                _states[profile.Id] = ConnectionState.Error;
                RaiseStatus(profile.Id, ConnectionState.Error, message, null);
                _logger.LogWarning(
                    exception,
                    "SSH connection to {Host}:{Port} failed (profile {ProfileId}).",
                    profile.Host,
                    profile.Port,
                    profile.Id);
                throw new RemoteOperationException(message, exception);
            }
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(
        ServerProfile profile,
        ServerSecret secret,
        HostKeyExpectation hostKeyExpectation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ValidateProfile(profile);

        var timeout = TimeSpan.FromSeconds(_settings.Current.ConnectionTimeoutSeconds);
        SshClient? client = null;
        HostKeyObservation? observation = null;

        try
        {
            (client, observation) = BuildClient(profile, secret, hostKeyExpectation, timeout);
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);
            await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);

            var result = await ExecuteCapabilityCheckAsync(client, cancellationToken).ConfigureAwait(false);
            var fingerprint = observation.Fingerprint ?? hostKeyExpectation.Fingerprint;

            if (result.Cancelled)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            CapabilityReport? capabilities = null;
            try
            {
                capabilities = RemoteResultParser.ParseData<CapabilityReport>(result);
            }
            catch (RemoteOperationException exception)
            {
                return new ConnectionTestResult(
                    Success: true,
                    ManagementReady: false,
                    Message: "SSH authentication succeeded, but the PowerShell capability check failed: " + exception.Message,
                    HostKeyFingerprint: fingerprint,
                    Capabilities: null);
            }

            var message = capabilities.IsManagementReady
                ? $"SSH and PowerShell are working. Connected to {capabilities.ComputerName} running {capabilities.OsCaption}."
                : "SSH authentication succeeded, but IIS management is not ready. Review the diagnostics.";

            return new ConnectionTestResult(
                Success: true,
                ManagementReady: capabilities.IsManagementReady,
                Message: message,
                HostKeyFingerprint: fingerprint,
                Capabilities: capabilities);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (SshOperationTimeoutException)
        {
            return new ConnectionTestResult(
                false,
                false,
                $"The connection to '{profile.Host}' timed out after {timeout.TotalSeconds:0} seconds.",
                observation?.Fingerprint,
                null);
        }
        catch (HostKeyVerificationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            if (observation?.Fingerprint is { Length: > 0 } fingerprint &&
                !FingerprintsEqual(hostKeyExpectation.Fingerprint, fingerprint))
            {
                throw new HostKeyVerificationException(
                    profile.Host,
                    fingerprint,
                    isMismatch: hostKeyExpectation.IsPinned);
            }

            return new ConnectionTestResult(
                false,
                false,
                BuildConnectionErrorMessage(profile, exception),
                observation?.Fingerprint,
                null);
        }
        finally
        {
            client?.Dispose();
        }
    }

    public async Task DisconnectAsync(Guid serverId, CancellationToken cancellationToken = default)
    {
        await _connectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectUnlockedAsync(serverId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _connectionGate.Release();
        }
    }

    public async Task<RemoteExecutionResult> ExecuteAsync(
        Guid serverId,
        RemoteScript script,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (!_sessions.TryGetValue(serverId, out var session))
        {
            throw new NotConnectedException(serverId.ToString());
        }

        if (!IsClientConnected(session.Client))
        {
            RemoveSession(serverId, session);
            _states[serverId] = ConnectionState.Error;
            RaiseStatus(serverId, ConnectionState.Error, "The SSH connection was lost.", null);
            throw new NotConnectedException(session.ServerName);
        }

        await session.CommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var commandText = PowerShellCommandBuilder.Build(script);
            using var command = session.Client.CreateCommand(commandText, Encoding.UTF8);
            command.CommandTimeout = TimeSpan.FromSeconds(script.TimeoutSeconds);

            try
            {
                await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
                stopwatch.Stop();

                return new RemoteExecutionResult(
                    command.ExitStatus ?? 0,
                    command.Result ?? string.Empty,
                    command.Error ?? string.Empty,
                    TimedOut: false,
                    Cancelled: false,
                    stopwatch.Elapsed);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                stopwatch.Stop();
                return new RemoteExecutionResult(-1, string.Empty, string.Empty, TimedOut: false, Cancelled: true, stopwatch.Elapsed);
            }
            catch (SshOperationTimeoutException)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    "Remote script {ScriptName} timed out after {TimeoutSeconds} seconds on profile {ProfileId}.",
                    script.Name,
                    script.TimeoutSeconds,
                    serverId);
                return new RemoteExecutionResult(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: false, stopwatch.Elapsed);
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                _logger.LogWarning(
                    exception,
                    "Remote script {ScriptName} failed at the transport layer on profile {ProfileId}.",
                    script.Name,
                    serverId);

                if (!IsClientConnected(session.Client))
                {
                    RemoveSession(serverId, session);
                    _states[serverId] = ConnectionState.Error;
                    RaiseStatus(serverId, ConnectionState.Error, "The SSH connection was lost.", null);
                }

                return new RemoteExecutionResult(
                    -1,
                    string.Empty,
                    string.Empty,
                    TimedOut: false,
                    Cancelled: false,
                    stopwatch.Elapsed,
                    TransportError: exception.Message);
            }
        }
        finally
        {
            session.CommandGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pair in _sessions)
        {
            pair.Value.Client.Dispose();
            pair.Value.CommandGate.Dispose();
        }

        _sessions.Clear();
        _connectionGate.Dispose();
    }

    private async Task<RemoteExecutionResult> ExecuteCapabilityCheckAsync(SshClient client, CancellationToken cancellationToken)
    {
        var script = PowerShellScripts.Capabilities();
        var commandText = PowerShellCommandBuilder.Build(script);
        using var command = client.CreateCommand(commandText, Encoding.UTF8);
        command.CommandTimeout = TimeSpan.FromSeconds(script.TimeoutSeconds);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await command.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            return new RemoteExecutionResult(
                command.ExitStatus ?? 0,
                command.Result ?? string.Empty,
                command.Error ?? string.Empty,
                TimedOut: false,
                Cancelled: false,
                stopwatch.Elapsed);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new RemoteExecutionResult(-1, string.Empty, string.Empty, TimedOut: false, Cancelled: true, stopwatch.Elapsed);
        }
        catch (SshOperationTimeoutException)
        {
            stopwatch.Stop();
            return new RemoteExecutionResult(-1, string.Empty, string.Empty, TimedOut: true, Cancelled: false, stopwatch.Elapsed);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new RemoteExecutionResult(-1, string.Empty, string.Empty, TimedOut: false, Cancelled: false, stopwatch.Elapsed, TransportError: exception.Message);
        }
    }

    private (SshClient Client, HostKeyObservation Observation) BuildClient(
        ServerProfile profile,
        ServerSecret secret,
        HostKeyExpectation expectation,
        TimeSpan timeout)
    {
        Renci.SshNet.AuthenticationMethod authenticationMethod;
        if (profile.AuthenticationMethod == WindowsServerManagement.Core.Models.AuthenticationMethod.Password)
        {
            if (string.IsNullOrEmpty(secret.Password))
            {
                throw new ValidationException("A password is required for password authentication.");
            }

            authenticationMethod = new PasswordAuthenticationMethod(profile.Username.Trim(), secret.Password);
        }
        else
        {
            var keyPath = profile.PrivateKeyPath?.Trim();
            if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
            {
                throw new ValidationException("The configured private key file does not exist.");
            }

            PrivateKeyFile? keyFile = null;
            try
            {
                keyFile = string.IsNullOrEmpty(secret.Passphrase)
                    ? new PrivateKeyFile(keyPath)
                    : new PrivateKeyFile(keyPath, secret.Passphrase);
                authenticationMethod = new PrivateKeyAuthenticationMethod(profile.Username.Trim(), keyFile);
            }
            catch (Exception exception) when (exception is SshException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                keyFile?.Dispose();
                throw new ValidationException(
                    "The private key could not be loaded. Verify the file format and passphrase. " + exception.Message);
            }
        }

        var connectionInfo = new ConnectionInfo(
            profile.Host.Trim(),
            profile.Port,
            profile.Username.Trim(),
            authenticationMethod)
        {
            Timeout = timeout,
            Encoding = Encoding.UTF8
        };

        var observation = new HostKeyObservation();
        var client = new SshClient(connectionInfo);
        client.HostKeyReceived += (_, args) =>
        {
            observation.Fingerprint = args.FingerPrintSHA256;
            args.CanTrust = FingerprintsEqual(expectation.Fingerprint, args.FingerPrintSHA256);
        };

        return (client, observation);
    }

    private static bool FingerprintsEqual(string? expected, string? actual)
    {
        if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        static string Normalize(string value)
        {
            var normalized = value.Trim();
            if (normalized.StartsWith("SHA256:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized[7..];
            }

            return normalized.TrimEnd('=');
        }

        return string.Equals(Normalize(expected), Normalize(actual), StringComparison.Ordinal);
    }

    private static void ValidateProfile(ServerProfile profile)
    {
        var validation = ServerProfileValidator.Validate(profile, hasStoredSecret: true, newSecret: "supplied");
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }
    }

    private string BuildConnectionErrorMessage(ServerProfile profile, Exception exception)
    {
        if (exception is SshAuthenticationException)
        {
            return $"SSH authentication failed for '{profile.Username}' at '{profile.Host}'. Check the username and credential.";
        }

        if (exception is SocketException or SshConnectionException)
        {
            return $"Could not reach '{profile.Host}:{profile.Port}' over SSH. Verify the host, port, firewall rules, and that OpenSSH Server is running.";
        }

        if (exception is ValidationException)
        {
            return exception.Message;
        }

        return $"The SSH connection to '{profile.Host}' failed: {exception.Message}";
    }

    private async Task DisconnectUnlockedAsync(Guid serverId, CancellationToken cancellationToken)
    {
        if (_sessions.TryRemove(serverId, out var session))
        {
            try
            {
                if (session.Client.IsConnected)
                {
                    session.Client.Disconnect();
                }
            }
            catch (Exception exception)
            {
                _logger.LogDebug(exception, "Disconnect failed for profile {ProfileId}.", serverId);
            }
            finally
            {
                session.Client.Dispose();
                session.CommandGate.Dispose();
            }
        }

        _states[serverId] = ConnectionState.Disconnected;
        RaiseStatus(serverId, ConnectionState.Disconnected, "Disconnected", null);
        await Task.CompletedTask.ConfigureAwait(false);
    }

    private void RemoveSession(Guid serverId, ServerSession session)
    {
        if (_sessions.TryRemove(serverId, out _))
        {
            session.Client.Dispose();
            session.CommandGate.Dispose();
        }
    }

    private void RaiseStatus(Guid serverId, ConnectionState state, string? message, string? fingerprint)
    {
        StatusChanged?.Invoke(this, new ServerConnectionStatus(serverId, state, message, fingerprint));
    }

    private static bool IsClientConnected(SshClient client)
    {
        try
        {
            return client.IsConnected;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private sealed record ServerSession(
        SshClient Client,
        string ServerName,
        string HostKeyFingerprint,
        SemaphoreSlim CommandGate);

    private sealed class HostKeyObservation
    {
        public string? Fingerprint { get; set; }
    }
}

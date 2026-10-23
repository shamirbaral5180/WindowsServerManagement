using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WindowsServerManagement.App.Services;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services.Remote;

namespace WindowsServerManagement.App.ViewModels;

public sealed partial class ServerEditorViewModel : ObservableObject
{
    private readonly ServerCatalog _catalog;
    private readonly IDialogService _dialogs;
    private readonly ServerProfile? _existing;
    private string? _trustedFingerprint;
    private string _lastVerifiedHost = string.Empty;
    private int _lastVerifiedPort;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _host = string.Empty;

    [ObservableProperty]
    private string _portText = "22";

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private AuthenticationMethod _authenticationMethod = AuthenticationMethod.Password;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _passphrase = string.Empty;

    [ObservableProperty]
    private string _privateKeyPath = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    private bool _forgetTrustedHostKey;

    [ObservableProperty]
    private bool _clearStoredSecret;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _testSummary;

    [ObservableProperty]
    private string? _testDetails;

    [ObservableProperty]
    private string _fingerprintText = "Not trusted yet";

    public ServerEditorViewModel(ServerCatalog catalog, IDialogService dialogs, ServerProfile? existing)
    {
        _catalog = catalog;
        _dialogs = dialogs;
        _existing = existing;
        _lastVerifiedHost = existing?.Host?.Trim() ?? string.Empty;
        _lastVerifiedPort = existing?.Port ?? 0;

        if (existing is not null)
        {
            Name = existing.Name;
            Host = existing.Host;
            PortText = existing.Port.ToString();
            Username = existing.Username;
            AuthenticationMethod = existing.AuthenticationMethod;
            PrivateKeyPath = existing.PrivateKeyPath ?? string.Empty;
            Notes = existing.Notes ?? string.Empty;
            _trustedFingerprint = existing.TrustedHostKeyFingerprint;
            FingerprintText = string.IsNullOrWhiteSpace(_trustedFingerprint)
                ? "Not trusted yet"
                : "SHA256:" + _trustedFingerprint;
        }
    }

    public event EventHandler<bool>? CloseRequested;
    public ServerProfile? Result { get; private set; }
    public IReadOnlyList<AuthenticationMethod> AuthenticationMethods { get; } = [AuthenticationMethod.Password, AuthenticationMethod.PrivateKey];
    public bool IsEdit => _existing is not null;
    public string WindowTitle => IsEdit ? "Edit server" : "Add server";
    public string SaveLabel => IsEdit ? "Save changes" : "Add server";
    public bool HasStoredSecret => _existing?.HasStoredSecret == true;
    public bool CanForgetTrustedHostKey => !string.IsNullOrWhiteSpace(_trustedFingerprint);
    public bool IsPasswordAuthentication => AuthenticationMethod == AuthenticationMethod.Password;
    public bool IsPrivateKeyAuthentication => AuthenticationMethod == AuthenticationMethod.PrivateKey;
    public string SecretLabel => IsPasswordAuthentication ? "Password" : "Private key passphrase (optional)";
    public string SecretHint => HasStoredSecret
        ? "A secret is already saved. Leave the field blank to keep it."
        : IsPasswordAuthentication
            ? "The password is protected with Windows DPAPI for your Windows account."
            : "Leave blank for an unencrypted private key. Passphrases are protected with Windows DPAPI.";

    [RelayCommand]
    private void BrowsePrivateKey()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select SSH private key",
            Filter = "Private key files|*.pem;*.key;*.ppk;id_rsa;id_ed25519|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            PrivateKeyPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        TestSummary = null;
        TestDetails = null;
        IsBusy = true;

        try
        {
            var draft = BuildProfile();
            draft.TrustedHostKeyFingerprint = ForgetTrustedHostKey ? null : _trustedFingerprint;
            var suppliedSecret = IsPasswordAuthentication ? Password : Passphrase;

            var result = await _catalog.TestConnectionAsync(
                    draft,
                    suppliedSecret,
                    ClearStoredSecret,
                    CancellationToken.None)
                .ConfigureAwait(true);

            if (!string.IsNullOrWhiteSpace(draft.TrustedHostKeyFingerprint))
            {
                _trustedFingerprint = draft.TrustedHostKeyFingerprint;
                _lastVerifiedHost = Host.Trim();
                _ = int.TryParse(PortText, out _lastVerifiedPort);
                ForgetTrustedHostKey = false;
                FingerprintText = "SHA256:" + _trustedFingerprint;
                OnPropertyChanged(nameof(CanForgetTrustedHostKey));
            }

            TestSummary = result.Message;
            if (result.Capabilities is not null)
            {
                var capabilities = result.Capabilities;
                TestDetails = $"""
                               OS: {capabilities.OsCaption} {capabilities.OsVersion}
                               Computer: {capabilities.ComputerName}
                               PowerShell: {capabilities.PowerShellVersion}
                               IIS installed: {(capabilities.IisInstalled ? "Yes" : "No")}
                               WebAdministration: {(capabilities.WebAdministrationAvailable ? "Available" : "Missing")}
                               Administrator: {(capabilities.IsElevated ? "Yes" : "No")}
                               W3SVC: {capabilities.W3svcStatus}
                               Management ready: {(capabilities.IsManagementReady ? "Yes" : "No")}
                               {(capabilities.Problems.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, capabilities.Problems.Select(problem => "• " + problem)) : string.Empty)}
                               """;
            }

            if (!result.Success)
            {
                ErrorMessage = result.Message;
            }
        }
        catch (Exception exception)
        {
            ErrorMessage = exception is AppException ? exception.Message : "Connection test failed. " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (IsBusy)
        {
            return;
        }

        ErrorMessage = null;
        IsBusy = true;

        try
        {
            var profile = BuildProfile();
            profile.TrustedHostKeyFingerprint = ForgetTrustedHostKey ? null : _trustedFingerprint;
            var suppliedSecret = IsPasswordAuthentication ? Password : Passphrase;

            var saved = await _catalog.SaveProfileAsync(
                    profile,
                    suppliedSecret,
                    ClearStoredSecret,
                    CancellationToken.None)
                .ConfigureAwait(true);

            Result = saved;
        }
        catch (Exception exception)
        {
            ErrorMessage = exception is AppException ? exception.Message : "The server profile could not be saved. " + exception.Message;
        }
        finally
        {
            IsBusy = false;
        }

        if (Result is not null)
        {
            CloseRequested?.Invoke(this, true);
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsBusy)
        {
            CloseRequested?.Invoke(this, false);
        }
    }

    partial void OnAuthenticationMethodChanged(AuthenticationMethod value)
    {
        OnPropertyChanged(nameof(IsPasswordAuthentication));
        OnPropertyChanged(nameof(IsPrivateKeyAuthentication));
        OnPropertyChanged(nameof(SecretLabel));
        OnPropertyChanged(nameof(SecretHint));
    }

    partial void OnHostChanged(string value)
    {
        ClearTrustIfTargetChanged();
    }

    partial void OnPortTextChanged(string value)
    {
        ClearTrustIfTargetChanged();
    }

    partial void OnForgetTrustedHostKeyChanged(bool value)
    {
        FingerprintText = value ? "Will be forgotten on save" : string.IsNullOrWhiteSpace(_trustedFingerprint)
            ? "Not trusted yet"
            : "SHA256:" + _trustedFingerprint;
    }

    private void ClearTrustIfTargetChanged()
    {
        if (string.IsNullOrWhiteSpace(_trustedFingerprint))
        {
            return;
        }

        var portMatches = int.TryParse(PortText, out var port) && port == _lastVerifiedPort;
        if (!string.Equals(Host.Trim(), _lastVerifiedHost, StringComparison.OrdinalIgnoreCase) || !portMatches)
        {
            _trustedFingerprint = null;
            ForgetTrustedHostKey = false;
            FingerprintText = "Host or port changed. Test the connection to verify the host key again.";
            OnPropertyChanged(nameof(CanForgetTrustedHostKey));
        }
    }

    private ServerProfile BuildProfile()
    {
        if (!int.TryParse(PortText, out var port) || port is < 1 or > 65535)
        {
            throw new ValidationException("SSH port must be between 1 and 65535.");
        }

        var profile = _existing?.Clone() ?? new ServerProfile();
        profile.Name = Name.Trim();
        profile.Host = Host.Trim();
        profile.Port = port;
        profile.Username = Username.Trim();
        profile.AuthenticationMethod = AuthenticationMethod;
        profile.PrivateKeyPath = IsPrivateKeyAuthentication ? PrivateKeyPath.Trim() : null;
        profile.Notes = string.IsNullOrWhiteSpace(Notes) ? null : Notes.Trim();
        profile.TrustedHostKeyFingerprint = ForgetTrustedHostKey ? null : _trustedFingerprint;
        return profile;
    }
}

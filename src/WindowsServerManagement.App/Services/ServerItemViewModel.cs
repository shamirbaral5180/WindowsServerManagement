using CommunityToolkit.Mvvm.ComponentModel;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.App.Services;

public sealed partial class ServerItemViewModel : ObservableObject
{
    [ObservableProperty]
    private ServerProfile _profile;

    [ObservableProperty]
    private ConnectionState _connectionState = ConnectionState.Disconnected;

    [ObservableProperty]
    private string _statusMessage = "Disconnected";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _websiteCount;

    [ObservableProperty]
    private int _runningWebsiteCount;

    [ObservableProperty]
    private int _stoppedWebsiteCount;

    [ObservableProperty]
    private string? _lastError;

    [ObservableProperty]
    private CapabilityReport? _capabilities;

    public ServerItemViewModel(ServerProfile profile)
    {
        _profile = profile;
    }

    public Guid Id => Profile.Id;
    public string Name => Profile.Name;
    public string Host => Profile.Host;
    public string Endpoint => Profile.Endpoint;
    public string AuthenticationLabel => Profile.AuthenticationLabel;
    public string Initial => string.IsNullOrWhiteSpace(Profile.Name) ? "?" : Profile.Name.Trim()[..1].ToUpperInvariant();
    public bool IsConnected => ConnectionState == ConnectionState.Connected;
    public bool IsConnecting => ConnectionState == ConnectionState.Connecting;
    public bool IsDisconnected => ConnectionState == ConnectionState.Disconnected;
    public bool HasError => ConnectionState == ConnectionState.Error;
    public string LastConnectedText => Profile.LastConnectedAt is null
        ? "Never connected"
        : Profile.LastConnectedAt.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm");
    public bool HasTrustedHostKey => !string.IsNullOrWhiteSpace(Profile.TrustedHostKeyFingerprint);
    public string FingerprintText => string.IsNullOrWhiteSpace(Profile.TrustedHostKeyFingerprint)
        ? "Host key not yet trusted"
        : "SHA256:" + Profile.TrustedHostKeyFingerprint;
    public string WebsiteSummary => $"{WebsiteCount} sites | {RunningWebsiteCount} running | {StoppedWebsiteCount} stopped";
    public bool HasCapabilities => Capabilities is not null;
    public bool IsManagementReady => Capabilities?.IsManagementReady == true;
    public string CapabilitySummary => Capabilities is null
        ? "Not checked"
        : Capabilities.IsManagementReady
            ? "IIS management ready"
            : "Needs attention";


    public void UpdateProfile(ServerProfile profile)
    {
        Profile = profile;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(AuthenticationLabel));
        OnPropertyChanged(nameof(Initial));
        OnPropertyChanged(nameof(LastConnectedText));
        OnPropertyChanged(nameof(FingerprintText));
    }

    public void ApplyConnectionState(ConnectionState state, string? message, string? error)
    {
        ConnectionState = state;
        StatusMessage = state switch
        {
            ConnectionState.Connected => "Connected",
            ConnectionState.Connecting => "Connecting...",
            ConnectionState.Error => "Connection error",
            _ => "Disconnected"
        };
        LastError = error;
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(HasError));
    }

    public void SetWebsiteStats(int total, int running, int stopped)
    {
        WebsiteCount = total;
        RunningWebsiteCount = running;
        StoppedWebsiteCount = stopped;
        OnPropertyChanged(nameof(WebsiteSummary));
    }

    partial void OnConnectionStateChanged(ConnectionState value)
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsConnecting));
        OnPropertyChanged(nameof(IsDisconnected));
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnCapabilitiesChanged(CapabilityReport? value)
    {
        OnPropertyChanged(nameof(HasCapabilities));
        OnPropertyChanged(nameof(IsManagementReady));
        OnPropertyChanged(nameof(CapabilitySummary));
    }

    partial void OnProfileChanged(ServerProfile value)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Host));
        OnPropertyChanged(nameof(Endpoint));
        OnPropertyChanged(nameof(AuthenticationLabel));
        OnPropertyChanged(nameof(Initial));
        OnPropertyChanged(nameof(LastConnectedText));
        OnPropertyChanged(nameof(FingerprintText));
        OnPropertyChanged(nameof(HasTrustedHostKey));
    }
}

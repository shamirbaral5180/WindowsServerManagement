using System.Text.Json.Serialization;

namespace WindowsServerManagement.Core.Models;

public enum AuthenticationMethod
{
    Password,
    PrivateKey
}

[JsonConverter(typeof(JsonStringEnumConverter<AuthenticationMethod>))]
public enum ConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Error
}

[JsonConverter(typeof(JsonStringEnumConverter<ThemeMode>))]
public enum ThemeMode
{
    System,
    Light,
    Dark
}

[JsonConverter(typeof(JsonStringEnumConverter<ActivityStatus>))]
public enum ActivityStatus
{
    Info,
    Success,
    Warning,
    Error
}

public sealed class ServerProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public AuthenticationMethod AuthenticationMethod { get; set; } = AuthenticationMethod.Password;
    public string? PrivateKeyPath { get; set; }
    public string? TrustedHostKeyFingerprint { get; set; }
    public bool HasStoredSecret { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastConnectedAt { get; set; }

    [JsonIgnore]
    public string Endpoint => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";

    [JsonIgnore]
    public string AuthenticationLabel => AuthenticationMethod == AuthenticationMethod.Password ? "Password" : "Private key";

    public ServerProfile Clone()
    {
        return (ServerProfile)MemberwiseClone();
    }
}

public sealed class ServerSecret
{
    public string? Password { get; set; }
    public string? Passphrase { get; set; }

    [JsonIgnore]
    public bool IsEmpty => string.IsNullOrEmpty(Password) && string.IsNullOrEmpty(Passphrase);
}

public sealed record HostKeyExpectation(string? Fingerprint, bool IsPinned)
{
    public static HostKeyExpectation FirstUse { get; } = new(null, false);
}

public sealed record ServerConnectionStatus(Guid ServerId, ConnectionState State, string? Message, string? HostKeyFingerprint)
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}

public sealed class CapabilityReport
{
    public string OsCaption { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string ComputerName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string PowerShellVersion { get; set; } = string.Empty;
    public string W3svcStatus { get; set; } = string.Empty;
    public bool IsWindows { get; set; }
    public bool IisInstalled { get; set; }
    public bool WebAdministrationAvailable { get; set; }
    public bool IsElevated { get; set; }
    public bool CanManageIis { get; set; }

    [JsonIgnore]
    public bool IsManagementReady => IsWindows && IisInstalled && WebAdministrationAvailable && IsElevated && CanManageIis;

    [JsonIgnore]
    public IReadOnlyList<string> Problems
    {
        get
        {
            var problems = new List<string>();
            if (!IsWindows)
            {
                problems.Add("The remote operating system is not Windows.");
            }

            if (string.IsNullOrWhiteSpace(PowerShellVersion))
            {
                problems.Add("PowerShell did not report a version. Verify that powershell.exe can run over SSH.");
            }

            if (!IisInstalled)
            {
                problems.Add("IIS is not installed. Install the Web-Server role and management tools.");
            }

            if (!WebAdministrationAvailable)
            {
                problems.Add("The WebAdministration PowerShell module is not available. Install IIS management tools.");
            }

            if (!IsElevated)
            {
                problems.Add("The SSH account is not elevated. IIS management requires an administrator session.");
            }

            if (!CanManageIis && problems.Count == 0)
            {
                problems.Add("The server did not report a management-ready IIS environment.");
            }

            return problems;
        }
    }
}

public sealed record ConnectionTestResult(
    bool Success,
    bool ManagementReady,
    string Message,
    string? HostKeyFingerprint,
    CapabilityReport? Capabilities);

public sealed record RemoteScript(string Name, string ScriptText, int TimeoutSeconds = 60, bool RequiresElevation = false);

public sealed record RemoteExecutionResult(
    int ExitStatus,
    string StandardOutput,
    string StandardError,
    bool TimedOut,
    bool Cancelled,
    TimeSpan Duration,
    string? TransportError = null)
{
    public bool Succeeded => !TimedOut && !Cancelled && string.IsNullOrWhiteSpace(TransportError) && ExitStatus == 0;
}

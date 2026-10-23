using System.Text.Json.Serialization;

namespace WindowsServerManagement.Core.Models;

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.Light;
    public string DefaultIisRoot { get; set; } = @"C:\inetpub";
    public string DefaultApplicationPoolPattern { get; set; } = "{site}";
    public string DefaultApplicationPoolRuntimeVersion { get; set; } = "v4.0";
    public string DefaultApplicationPoolPipelineMode { get; set; } = "Integrated";
    public int DefaultWebsitePort { get; set; } = 8080;
    public int ConnectionTimeoutSeconds { get; set; } = 20;
    public int CommandTimeoutSeconds { get; set; } = 90;
    public bool EnableFileLogging { get; set; } = true;
    public string MinimumLogLevel { get; set; } = "Information";
    public Guid? LastSelectedServerId { get; set; }

    public AppSettings Clone()
    {
        return (AppSettings)MemberwiseClone();
    }

    public void Normalize()
    {
        if (string.IsNullOrWhiteSpace(DefaultIisRoot))
        {
            DefaultIisRoot = @"C:\inetpub";
        }

        if (string.IsNullOrWhiteSpace(DefaultApplicationPoolPattern))
        {
            DefaultApplicationPoolPattern = "{site}";
        }

        if (string.IsNullOrWhiteSpace(DefaultApplicationPoolRuntimeVersion))
        {
            DefaultApplicationPoolRuntimeVersion = "v4.0";
        }

        if (string.IsNullOrWhiteSpace(DefaultApplicationPoolPipelineMode))
        {
            DefaultApplicationPoolPipelineMode = "Integrated";
        }

        if (string.IsNullOrWhiteSpace(MinimumLogLevel))
        {
            MinimumLogLevel = "Information";
        }

        DefaultWebsitePort = Math.Clamp(DefaultWebsitePort, 1, 65535);
        ConnectionTimeoutSeconds = Math.Clamp(ConnectionTimeoutSeconds, 5, 300);
        CommandTimeoutSeconds = Math.Clamp(CommandTimeoutSeconds, 10, 900);
    }
}

public sealed class ActivityLogEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;
    public Guid? ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public ActivityStatus Status { get; set; } = ActivityStatus.Info;
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public long? DurationMs { get; set; }

    [JsonIgnore]
    public string TimestampText => Timestamp.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public string StatusText => Status.ToString();
}

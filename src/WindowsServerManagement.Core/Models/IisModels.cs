using System.Text.Json.Serialization;

namespace WindowsServerManagement.Core.Models;

public sealed class WebsiteBinding
{
    public string Protocol { get; set; } = "http";
    public string IpAddress { get; set; } = "*";
    public int Port { get; set; } = 80;
    public string Hostname { get; set; } = string.Empty;
    public string CertificateThumbprint { get; set; } = string.Empty;
    public string BindingInformation { get; set; } = string.Empty;

    [JsonIgnore]
    public string Display
    {
        get
        {
            var host = string.IsNullOrWhiteSpace(Hostname) ? "*" : Hostname.Trim();
            var protocol = string.IsNullOrWhiteSpace(Protocol) ? "http" : Protocol.Trim().ToLowerInvariant();
            return $"{protocol}://{host}:{Port}/";
        }
    }

    [JsonIgnore]
    public string Key => $"{Protocol?.Trim().ToLowerInvariant()}|{Port}|{Hostname?.Trim().ToLowerInvariant()}|{IpAddress?.Trim()}";

    public WebsiteBinding Clone()
    {
        return (WebsiteBinding)MemberwiseClone();
    }
}

public sealed class IisWebsite
{
    public Guid ServerId { get; set; }
    public string ServerName { get; set; } = string.Empty;
    public string ServerHost { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string PhysicalPath { get; set; } = string.Empty;
    public string ApplicationPool { get; set; } = string.Empty;
    public List<WebsiteBinding> Bindings { get; set; } = [];

    [JsonIgnore]
    public string BindingsDisplay => Bindings.Count == 0
        ? "No bindings"
        : string.Join(Environment.NewLine, Bindings.Select(binding => binding.Display));

    [JsonIgnore]
    public bool IsRunning => string.Equals(State, "Started", StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public string PrimaryUrl
    {
        get
        {
            var binding = Bindings.FirstOrDefault(item => string.Equals(item.Protocol, "https", StringComparison.OrdinalIgnoreCase))
                          ?? Bindings.FirstOrDefault();
            if (binding is null)
            {
                return string.Empty;
            }

            var host = !string.IsNullOrWhiteSpace(binding.Hostname) &&
                       binding.Hostname != "*" &&
                       !binding.Hostname.StartsWith("*.", StringComparison.Ordinal)
                ? binding.Hostname
                : (string.IsNullOrWhiteSpace(ServerHost) ? ServerName : ServerHost);

            if (string.IsNullOrWhiteSpace(host))
            {
                return string.Empty;
            }

            var defaultPort = string.Equals(binding.Protocol, "https", StringComparison.OrdinalIgnoreCase) ? 443 : 80;
            return binding.Port == defaultPort
                ? $"{binding.Protocol}://{host}/"
                : $"{binding.Protocol}://{host}:{binding.Port}/";
        }
    }
}

public sealed class IisCertificateInfo
{
    public string Subject { get; set; } = string.Empty;
    public string Thumbprint { get; set; } = string.Empty;
    public string NotAfter { get; set; } = string.Empty;
    public string FriendlyName { get; set; } = string.Empty;

    [JsonIgnore]
    public string Display => string.IsNullOrWhiteSpace(FriendlyName)
        ? $"{Subject} ({Thumbprint})"
        : $"{FriendlyName} - {Subject} ({Thumbprint})";
}

public sealed class WebsiteCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public int Port { get; set; } = 8080;
    public string Hostname { get; set; } = string.Empty;
    public bool UseHttps { get; set; }
    public string CertificateThumbprint { get; set; } = string.Empty;
    public string ApplicationPoolName { get; set; } = string.Empty;
    public string IisRoot { get; set; } = @"C:\inetpub";
    public string ApplicationPoolRuntimeVersion { get; set; } = "v4.0";
    public string ApplicationPoolPipelineMode { get; set; } = "Integrated";
    public bool UseExistingFolder { get; set; }
    public bool StartImmediately { get; set; } = true;

    [JsonIgnore]
    public string Protocol => UseHttps ? "https" : "http";
}

public sealed class WebsiteCreationResult
{
    public bool Success { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public string ApplicationPool { get; set; } = string.Empty;
    public string Binding { get; set; } = string.Empty;
    public bool SiteCreated { get; set; }
    public bool FolderCreated { get; set; }
    public bool AppPoolCreated { get; set; }
    public string State { get; set; } = string.Empty;
    public List<string> Warnings { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class WebsiteOperationResult
{
    public bool Success { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class WebsiteDeleteResult
{
    public bool Success { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class FolderDeleteResult
{
    public bool Success { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public string? Error { get; set; }
}

public sealed class BindingsUpdateResult
{
    public bool Success { get; set; }
    public string SiteName { get; set; } = string.Empty;
    public List<WebsiteBinding> Bindings { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public string? Error { get; set; }
}

public sealed class ValidationResult
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];

    public bool IsValid => Errors.Count == 0;

    public static ValidationResult Success(params string[] warnings)
    {
        var result = new ValidationResult();
        result.Warnings.AddRange(warnings);
        return result;
    }

    public static ValidationResult Failure(params string[] errors)
    {
        var result = new ValidationResult();
        result.Errors.AddRange(errors);
        return result;
    }

    public string ErrorText => string.Join(Environment.NewLine, Errors);
}

using System.Net;
using System.Text.RegularExpressions;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Core.Services.Validation;

public static partial class WebsiteValidator
{
    private static readonly HashSet<string> ReservedFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public static ValidationResult ValidateCreateRequest(WebsiteCreateRequest request)
    {
        var result = new ValidationResult();

        ValidateSiteName(request.Name, result);
        ValidateApplicationPoolName(request.ApplicationPoolName, result);
        if (string.IsNullOrWhiteSpace(request.PhysicalPath))
        {
            ValidateFolderName(request.FolderName, result);
        }

        if (request.Port is < 1 or > 65535)
        {
            result.Errors.Add("Port must be between 1 and 65535.");
        }
        else if (request.Port < 1024)
        {
            result.Warnings.Add("Ports below 1024 usually require administrator privileges. The remote account must be elevated.");
        }

        if (!string.IsNullOrWhiteSpace(request.Hostname))
        {
            ValidateHostname(request.Hostname, result);
        }

        if (request.UseHttps)
        {
            var thumbprint = NormalizeThumbprint(request.CertificateThumbprint);
            if (thumbprint.Length == 0)
            {
                result.Errors.Add("Select an HTTPS certificate or enter its 40-character SHA-1 thumbprint.");
            }
            else if (!ThumbprintRegex().IsMatch(thumbprint))
            {
                result.Errors.Add("Certificate thumbprint must contain exactly 40 hexadecimal characters.");
            }
        }

        var root = request.IisRoot?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(root))
        {
            result.Errors.Add("IIS root directory is required.");
        }
        else if (!WebsitePathSafety.IsValidRoot(root, out var rootError))
        {
            result.Errors.Add(rootError);
        }

        if (result.IsValid)
        {
            var pathResult = WebsitePathSafety.ResolvePhysicalPath(
                root,
                request.PhysicalPath,
                request.FolderName,
                out _,
                out var pathError);
            if (!pathResult)
            {
                result.Errors.Add(pathError);
            }
        }

        if (!string.Equals(request.ApplicationPoolRuntimeVersion, "v4.0", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.ApplicationPoolRuntimeVersion, "v2.0", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.ApplicationPoolRuntimeVersion, "No Managed Code", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("Application pool runtime must be v4.0, v2.0, or No Managed Code.");
        }

        if (!string.Equals(request.ApplicationPoolPipelineMode, "Integrated", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.ApplicationPoolPipelineMode, "Classic", StringComparison.OrdinalIgnoreCase))
        {
            result.Errors.Add("Application pool pipeline mode must be Integrated or Classic.");
        }

        return result;
    }

    public static ValidationResult ValidateBindings(IReadOnlyList<WebsiteBinding> bindings)
    {
        var result = new ValidationResult();
        if (bindings.Count == 0)
        {
            result.Errors.Add("At least one binding is required.");
            return result;
        }

        var duplicateKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var binding in bindings)
        {
            var protocol = binding.Protocol?.Trim().ToLowerInvariant() ?? string.Empty;
            if (protocol is not ("http" or "https"))
            {
                result.Errors.Add("Binding protocol must be HTTP or HTTPS.");
            }

            if (binding.Port is < 1 or > 65535)
            {
                result.Errors.Add($"Binding port '{binding.Port}' must be between 1 and 65535.");
            }

            if (!string.IsNullOrWhiteSpace(binding.Hostname))
            {
                ValidateHostname(binding.Hostname, result);
            }

            if (protocol == "https")
            {
                var thumbprint = NormalizeThumbprint(binding.CertificateThumbprint);
                if (!ThumbprintRegex().IsMatch(thumbprint))
                {
                    result.Errors.Add("Every HTTPS binding needs a valid 40-character certificate thumbprint.");
                }
            }

            var key = $"{protocol}|{binding.Port}|{binding.Hostname?.Trim().ToLowerInvariant()}";
            if (!duplicateKeys.Add(key))
            {
                result.Errors.Add($"Duplicate binding: {protocol} port {binding.Port}, host '{binding.Hostname}'.");
            }
        }

        return result;
    }

    public static void ValidateSiteName(string? siteName, ValidationResult result)
    {
        var name = siteName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            result.Errors.Add("Website name is required.");
            return;
        }

        if (name.Length > 64)
        {
            result.Errors.Add("Website name must be 64 characters or fewer.");
        }

        if (name.Any(char.IsControl) || name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            result.Errors.Add("Website name contains characters that IIS does not allow.");
        }

        if (name is "." or ".." || name.EndsWith(' '))
        {
            result.Errors.Add("Website name is not valid.");
        }
    }

    public static void ValidateApplicationPoolName(string? poolName, ValidationResult result)
    {
        var name = poolName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            result.Errors.Add("Application pool name is required.");
            return;
        }

        if (name.Length > 64)
        {
            result.Errors.Add("Application pool name must be 64 characters or fewer.");
        }

        if (name.Any(char.IsControl) || name.IndexOfAny(['\\', '/', ':', '*', '?', '"', '<', '>', '|']) >= 0)
        {
            result.Errors.Add("Application pool name contains invalid characters.");
        }
    }

    public static void ValidateFolderName(string? folderName, ValidationResult result)
    {
        var name = folderName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
        {
            result.Errors.Add("Physical folder name is required.");
            return;
        }

        if (name.Length > 128)
        {
            result.Errors.Add("Physical folder name must be 128 characters or fewer.");
        }

        if (name is "." or ".." || name.Contains('/') || name.Contains('\\') || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            result.Errors.Add("Physical folder name must be a single valid Windows folder name.");
        }

        if (name.EndsWith(' ') || name.EndsWith('.'))
        {
            result.Errors.Add("Physical folder name cannot end with a space or period.");
        }

        if (ReservedFolderNames.Contains(name))
        {
            result.Errors.Add("Physical folder name is a reserved Windows device name.");
        }
    }

    public static void ValidateHostname(string hostname, ValidationResult result)
    {
        var host = hostname.Trim();
        if (host.Length > 253)
        {
            result.Errors.Add("Hostname must be 253 characters or fewer.");
            return;
        }

        if (host.Any(char.IsWhiteSpace) || host.Contains('/') || host.Contains('\\') || host.Contains(':') || host.Contains('@'))
        {
            result.Errors.Add("Hostname must not include a scheme, port, path, credentials, or spaces.");
            return;
        }

        if (host.StartsWith('.') || host.EndsWith('.') || host.Contains("..", StringComparison.Ordinal))
        {
            result.Errors.Add("Hostname contains invalid dot placement.");
            return;
        }

        if (!IPAddress.TryParse(host, out _) && Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            result.Errors.Add("Hostname is not a valid DNS name or IP address.");
        }
    }

    public static string NormalizeThumbprint(string? thumbprint)
    {
        return (thumbprint ?? string.Empty).Replace(" ", string.Empty).Replace(":", string.Empty).Trim().ToUpperInvariant();
    }

    [GeneratedRegex("^[0-9A-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex ThumbprintRegex();
}

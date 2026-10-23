using System.Text;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Core.Services;

public static class IisBindingBuilder
{
    public static string BuildBindingInformation(string? hostname, int port)
    {
        var host = string.IsNullOrWhiteSpace(hostname) ? string.Empty : hostname.Trim();
        return $"*:{port}:{host}";
    }

    public static string BuildDisplay(string protocol, int port, string? hostname)
    {
        var scheme = string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        var host = string.IsNullOrWhiteSpace(hostname) ? "*" : hostname.Trim();
        return $"{scheme}://{host}:{port}/";
    }

    public static WebsiteBinding Create(string protocol, int port, string? hostname, string? thumbprint = null)
    {
        var normalizedProtocol = string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        return new WebsiteBinding
        {
            Protocol = normalizedProtocol,
            Port = port,
            Hostname = hostname?.Trim() ?? string.Empty,
            IpAddress = "*",
            CertificateThumbprint = normalizedProtocol == "https" ? (thumbprint ?? string.Empty).ToUpperInvariant() : string.Empty,
            BindingInformation = BuildBindingInformation(hostname, port)
        };
    }

    public static bool TryParseBindingInformation(string? bindingInformation, out string ipAddress, out int port, out string hostname)
    {
        ipAddress = "*";
        port = 0;
        hostname = string.Empty;

        if (string.IsNullOrWhiteSpace(bindingInformation))
        {
            return false;
        }

        var segments = bindingInformation.Split(':');
        if (segments.Length < 3 || !int.TryParse(segments[^2], out port))
        {
            return false;
        }

        ipAddress = string.Join(':', segments[..^2]);
        hostname = segments[^1];
        return port is > 0 and <= 65535;
    }

    public static bool IsHostUnderRoot(string path, string root)
    {
        return WebsitePathSafety.IsUnderRoot(path, root);
    }
}

public static class WebsitePathSafety
{
    public static bool IsValidRoot(string root, out string error)
    {
        error = string.Empty;
        if (!IsAbsoluteWindowsPath(root))
        {
            error = @"IIS root must be an absolute Windows path such as C:\inetpub.";
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(root.Trim());
            var rootOfDrive = Path.GetPathRoot(fullPath);
            if (string.Equals(fullPath.TrimEnd('\\'), rootOfDrive?.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                error = "IIS root cannot be the root of a drive.";
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            error = $"IIS root is not valid: {exception.Message}";
            return false;
        }
    }

    public static bool ResolvePhysicalPath(string root, string? physicalPathInput, string? folderName, out string physicalPath, out string error)
    {
        physicalPath = string.Empty;
        error = string.Empty;

        if (!IsValidRoot(root, out error))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(physicalPathInput))
        {
            var candidate = physicalPathInput.Trim();
            if (!IsAbsoluteWindowsPath(candidate))
            {
                error = @"Physical path must be an absolute Windows path such as C:\inetpub\MyPortfolio.";
                return false;
            }

            try
            {
                var rootFull = Path.GetFullPath(root.Trim()).TrimEnd('\\');
                var fullPath = Path.GetFullPath(candidate).TrimEnd('\\');
                if (!fullPath.StartsWith(rootFull + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Physical path must be inside the configured IIS root '{rootFull}'.";
                    return false;
                }

                physicalPath = fullPath;
                return true;
            }
            catch (Exception exception)
            {
                error = $"Physical path is not valid: {exception.Message}";
                return false;
            }
        }

        return BuildPhysicalPath(root, folderName ?? string.Empty, out physicalPath, out error);
    }

    public static bool BuildPhysicalPath(string root, string folderName, out string physicalPath, out string error)
    {
        physicalPath = string.Empty;
        error = string.Empty;

        if (!IsValidRoot(root, out error))
        {
            return false;
        }

        var folder = folderName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(folder) ||
            folder is "." or ".." ||
            folder.Contains('/') ||
            folder.Contains('\\') ||
            folder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            error = "Physical folder name is not valid.";
            return false;
        }

        try
        {
            var rootFull = Path.GetFullPath(root.Trim()).TrimEnd('\\');
            var combined = Path.GetFullPath(Path.Combine(rootFull, folder));
            var rootPrefix = rootFull + "\\";
            if (!combined.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                error = "The resolved physical folder must remain inside the configured IIS root.";
                return false;
            }

            physicalPath = combined;
            return true;
        }
        catch (Exception exception)
        {
            error = $"Physical folder path is not valid: {exception.Message}";
            return false;
        }
    }

    public static bool IsUnderRoot(string path, string root)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsValidRoot(root, out _))
        {
            return false;
        }

        try
        {
            var pathFull = Path.GetFullPath(path).TrimEnd('\\');
            var rootFull = Path.GetFullPath(root).TrimEnd('\\');
            return pathFull.Length > rootFull.Length &&
                   pathFull.StartsWith(rootFull + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAbsoluteWindowsPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length < 3)
        {
            return false;
        }

        var trimmed = path.Trim();
        return char.IsLetter(trimmed[0]) &&
               trimmed[1] == ':' &&
               (trimmed[2] == '\\' || trimmed[2] == '/') &&
               !trimmed.Contains('\0');
    }
}

public static class PowerShellLiterals
{
    public static string Quote(string? value)
    {
        if (value is null)
        {
            return "$null";
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('\'');
        foreach (var character in value)
        {
            builder.Append(character);
            if (character == '\'')
            {
                builder.Append('\'');
            }
        }

        builder.Append('\'');
        return builder.ToString();
    }

    public static string Boolean(bool value) => value ? "$true" : "$false";

    public static string Base64Utf8(string value)
    {
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    }
}

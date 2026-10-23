using System.Net;
using System.Text.RegularExpressions;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Core.Services.Validation;

public static partial class ServerProfileValidator
{
    public static ValidationResult Validate(ServerProfile profile, bool hasStoredSecret, string? newSecret)
    {
        var result = new ValidationResult();
        var name = profile.Name?.Trim() ?? string.Empty;
        var host = profile.Host?.Trim() ?? string.Empty;
        var username = profile.Username?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(name))
        {
            result.Errors.Add("Server name is required.");
        }
        else if (name.Length > 100)
        {
            result.Errors.Add("Server name must be 100 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(host))
        {
            result.Errors.Add("Hostname or IP address is required.");
        }
        else
        {
            var isIpAddress = IPAddress.TryParse(host, out _);
            if (host.Any(char.IsWhiteSpace) || host.Contains('/') || host.Contains('\\') || host.Contains('@'))
            {
                result.Errors.Add("Hostname must not include a scheme, path, credentials, or spaces.");
            }
            else if (!isIpAddress && host.Contains(':'))
            {
                result.Errors.Add("Hostname must not include a port. Enter IPv6 addresses without brackets.");
            }
            else if (!isIpAddress && Uri.CheckHostName(host) == UriHostNameType.Unknown)
            {
                result.Errors.Add("Hostname is not a valid DNS name or IP address.");
            }
            else if (!isIpAddress && (host.StartsWith('.') || host.EndsWith('.') || host.Contains("..", StringComparison.Ordinal)))
            {
                result.Errors.Add("Hostname contains invalid dot placement.");
            }
        }

        if (profile.Port is < 1 or > 65535)
        {
            result.Errors.Add("SSH port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            result.Errors.Add("Username is required.");
        }
        else if (username.Length > 100 || username.Any(char.IsControl))
        {
            result.Errors.Add("Username is not valid.");
        }

        if (profile.AuthenticationMethod == AuthenticationMethod.PrivateKey)
        {
            if (string.IsNullOrWhiteSpace(profile.PrivateKeyPath))
            {
                result.Errors.Add("Private key path is required for private-key authentication.");
            }
            else if (!File.Exists(profile.PrivateKeyPath))
            {
                result.Errors.Add("The selected private key file does not exist.");
            }
        }

        var secretProvided = !string.IsNullOrWhiteSpace(newSecret);
        if (profile.AuthenticationMethod == AuthenticationMethod.Password && !secretProvided && !hasStoredSecret)
        {
            result.Errors.Add("A password is required.");
        }

        return result;
    }

    public static string NormalizeHost(string host)
    {
        return host.Trim().TrimEnd('.').ToLowerInvariant();
    }

    public static ValidationResult ValidateProfileUniqueness(IEnumerable<ServerProfile> profiles, ServerProfile candidate)
    {
        var duplicate = profiles.FirstOrDefault(profile =>
            profile.Id != candidate.Id &&
            string.Equals(profile.Name.Trim(), candidate.Name.Trim(), StringComparison.OrdinalIgnoreCase));

        return duplicate is null
            ? ValidationResult.Success()
            : ValidationResult.Failure($"A server profile named '{candidate.Name.Trim()}' already exists.");
    }
}

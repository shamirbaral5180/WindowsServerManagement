using System.Text;
using System.Text.Json;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Core.Services.Remote;

public sealed record RemoteEnvelope(bool Success, string? Error, JsonElement? Data);

public static class RemoteResultParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static RemoteEnvelope Parse(RemoteExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Cancelled)
        {
            throw new OperationCanceledException("The remote operation was cancelled.");
        }

        if (result.TimedOut)
        {
            throw new RemoteOperationException("The remote operation timed out. Verify that the server is online and increase the command timeout in Settings if the server is slow.");
        }

        if (!string.IsNullOrWhiteSpace(result.TransportError))
        {
            throw new RemoteOperationException($"SSH transport failed: {Sanitize(result.TransportError)}");
        }

        var envelope = TryReadEnvelope(result.StandardOutput);
        if (envelope is null)
        {
            var detail = BuildFailureDetail(result);
            throw new RemoteOperationException($"The remote command did not return a valid result.{detail}");
        }

        return envelope;
    }

    public static T ParseData<T>(RemoteExecutionResult result)
    {
        var envelope = Parse(result);
        if (!envelope.Success)
        {
            throw new RemoteOperationException(string.IsNullOrWhiteSpace(envelope.Error)
                ? "The remote operation failed without returning error details."
                : Sanitize(envelope.Error));
        }

        if (envelope.Data is null)
        {
            throw new RemoteOperationException("The remote operation returned no data.");
        }

        try
        {
            var data = envelope.Data.Value.Deserialize<T>(JsonOptions);
            return data ?? throw new RemoteOperationException("The remote operation returned empty data.");
        }
        catch (JsonException exception)
        {
            throw new RemoteOperationException($"The remote response could not be read: {exception.Message}", exception);
        }
    }

    private static RemoteEnvelope? TryReadEnvelope(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var line = lines.LastOrDefault(item => item.StartsWith(PowerShellCommandBuilder.ResultMarker, StringComparison.Ordinal));
        if (line is null)
        {
            return null;
        }

        var payload = line[PowerShellCommandBuilder.ResultMarker.Length..].Trim();
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var success = root.TryGetProperty("success", out var successElement) && successElement.ValueKind == JsonValueKind.True;
            var error = root.TryGetProperty("error", out var errorElement) && errorElement.ValueKind == JsonValueKind.String
                ? errorElement.GetString()
                : null;
            JsonElement? data = null;
            if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined)
            {
                data = dataElement.Clone();
            }

            return new RemoteEnvelope(success, error, data);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return null;
        }
    }

    private static bool IsPowerShellUnavailable(RemoteExecutionResult result)
    {
        var output = (result.StandardError + " " + result.StandardOutput).ToLowerInvariant();
        return output.Contains("'powershell.exe' is not recognized", StringComparison.Ordinal) ||
               output.Contains("powershell.exe: not found", StringComparison.Ordinal) ||
               output.Contains("the term 'powershell.exe' is not recognized", StringComparison.Ordinal);
    }

    private static string BuildFailureDetail(RemoteExecutionResult result)
    {
        var standardError = Sanitize(result.StandardError);
        var standardOutput = Sanitize(result.StandardOutput);
        var detail = new StringBuilder();
        detail.Append($" Exit code: {result.ExitStatus}.");

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            detail.Append($" Error output: {standardError}");
        }
        else if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            detail.Append($" Output: {standardOutput}");
        }

        return detail.ToString();
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= 1000 ? trimmed : trimmed[..1000] + "...";
    }
}

using System.Text;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.Core.Services.Remote;

public static class PowerShellCommandBuilder
{
    public const string ResultMarker = "__WSM_RESULT__";

    public static string Build(RemoteScript script)
    {
        ArgumentNullException.ThrowIfNull(script);
        if (string.IsNullOrWhiteSpace(script.ScriptText))
        {
            throw new ArgumentException("The remote script is empty.", nameof(script));
        }

        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script.ScriptText));
        return "powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand " + encoded;
    }

    public static string DecodeEncodedCommand(string commandText)
    {
        ArgumentNullException.ThrowIfNull(commandText);
        const string token = "-EncodedCommand ";
        var index = commandText.LastIndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            throw new FormatException("The command does not contain an encoded PowerShell payload.");
        }

        var encoded = commandText[(index + token.Length)..].Trim();
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }
}

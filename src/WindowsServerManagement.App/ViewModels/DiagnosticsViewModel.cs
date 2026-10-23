using WindowsServerManagement.App.Services;
using WindowsServerManagement.Core.Models;

namespace WindowsServerManagement.App.ViewModels;

public sealed class DiagnosticRow
{
    public DiagnosticRow(string label, string value, bool ok)
    {
        Label = label;
        Value = value;
        Ok = ok;
    }

    public string Label { get; }
    public string Value { get; }
    public bool Ok { get; }
}

public sealed class DiagnosticsViewModel
{
    public DiagnosticsViewModel(ServerItemViewModel server, CapabilityReport report)
    {
        ServerName = server.Name;
        Endpoint = server.Endpoint;
        IsReady = report.IsManagementReady;
        Headline = IsReady ? "The server is ready for IIS management" : "The server is not ready for IIS management";
        Summary = IsReady
            ? "SSH, PowerShell, IIS, the WebAdministration module, and administrator rights were verified."
            : "Review the failed checks below before creating or managing IIS websites.";
        Fingerprint = server.FingerprintText;

        Rows =
        [
            new DiagnosticRow("Operating system", $"{report.OsCaption} {report.OsVersion}".Trim(), report.IsWindows),
            new DiagnosticRow("Computer", string.IsNullOrWhiteSpace(report.ComputerName) ? "Unknown" : report.ComputerName, true),
            new DiagnosticRow("PowerShell", string.IsNullOrWhiteSpace(report.PowerShellVersion) ? "Unavailable" : report.PowerShellVersion, !string.IsNullOrWhiteSpace(report.PowerShellVersion)),
            new DiagnosticRow("IIS installed", report.IisInstalled ? "Yes" : "No", report.IisInstalled),
            new DiagnosticRow("WebAdministration module", report.WebAdministrationAvailable ? "Available" : "Missing", report.WebAdministrationAvailable),
            new DiagnosticRow("W3SVC service", report.W3svcStatus, string.Equals(report.W3svcStatus, "Running", StringComparison.OrdinalIgnoreCase)),
            new DiagnosticRow("Elevated administrator", report.IsElevated ? "Yes" : "No", report.IsElevated),
            new DiagnosticRow("IIS management ready", report.CanManageIis ? "Yes" : "No", report.CanManageIis)
        ];

        Problems = report.Problems.ToList();
    }

    public string ServerName { get; }
    public string Endpoint { get; }
    public bool IsReady { get; }
    public string Headline { get; }
    public string Summary { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<DiagnosticRow> Rows { get; }
    public IReadOnlyList<string> Problems { get; }
    public bool HasProblems => Problems.Count > 0;
}

using System.Text;
using System.Text.Json;
using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services;
using WindowsServerManagement.Core.Services.Remote;

namespace WindowsServerManagement.Tests;

public sealed class RemoteCommandTests
{
    [Fact]
    public void CommandBuilder_EncodesScriptAndDoesNotExposeRawInput()
    {
        const string maliciousName = "O'Brien; Remove-Item C:\\Windows";
        var script = new RemoteScript("test", $"$name = {PowerShellLiterals.Quote(maliciousName)}\nWrite-Output $name");
        var command = PowerShellCommandBuilder.Build(script);

        Assert.StartsWith("powershell.exe ", command, StringComparison.Ordinal);
        Assert.DoesNotContain(maliciousName, command, StringComparison.Ordinal);
        var decoded = PowerShellCommandBuilder.DecodeEncodedCommand(command);
        Assert.Contains("'O''Brien; Remove-Item C:\\Windows'", decoded, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteResultParser_DecodesBase64Envelope()
    {
        const string json = """{"success":true,"data":{"value":"ok"}}""";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var result = new RemoteExecutionResult(
            0,
            "noise\n" + PowerShellCommandBuilder.ResultMarker + base64 + "\n",
            string.Empty,
            TimedOut: false,
            Cancelled: false,
            TimeSpan.FromMilliseconds(10));

        var envelope = RemoteResultParser.Parse(result);

        Assert.True(envelope.Success);
        Assert.NotNull(envelope.Data);
        Assert.Equal("ok", envelope.Data!.Value.GetProperty("value").GetString());
    }

    [Fact]
    public void RemoteResultParser_SurfacesRemoteError()
    {
        const string json = """{"success":false,"error":"Access denied."}""";
        var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        var result = new RemoteExecutionResult(
            1,
            PowerShellCommandBuilder.ResultMarker + base64,
            string.Empty,
            TimedOut: false,
            Cancelled: false,
            TimeSpan.FromMilliseconds(10));

        var exception = Assert.Throws<RemoteOperationException>(() => RemoteResultParser.ParseData<object>(result));
        Assert.Contains("Access denied", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RemoteResultParser_ReportsMissingPowerShell()
    {
        var result = new RemoteExecutionResult(
            1,
            string.Empty,
            "'powershell.exe' is not recognized as an internal or external command.",
            TimedOut: false,
            Cancelled: false,
            TimeSpan.FromMilliseconds(10));

        var exception = Assert.Throws<RemoteOperationException>(() => RemoteResultParser.Parse(result));
        Assert.Contains("powershell.exe", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoteResultParser_ReportsTimeout()
    {
        var result = new RemoteExecutionResult(
            -1,
            string.Empty,
            string.Empty,
            TimedOut: true,
            Cancelled: false,
            TimeSpan.FromSeconds(60));

        var exception = Assert.Throws<RemoteOperationException>(() => RemoteResultParser.Parse(result));
        Assert.Contains("timed out", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CreateWebsiteScript_EscapesEveryUserValue()
    {
        var request = new WebsiteCreateRequest
        {
            Name = "Site'Name",
            FolderName = "Folder'Name",
            Port = 50082,
            Hostname = "example.com",
            ApplicationPoolName = "Pool'Name",
            IisRoot = @"C:\inetpub"
        };

        var script = PowerShellScripts.CreateWebsite(request);

        Assert.Contains("$siteName = 'Site''Name'", script.ScriptText, StringComparison.Ordinal);
        Assert.Contains("$folderName = 'Folder''Name'", script.ScriptText, StringComparison.Ordinal);
        Assert.Contains("$appPoolName = 'Pool''Name'", script.ScriptText, StringComparison.Ordinal);
        Assert.Contains("__WSM_RESULT__", script.ScriptText, StringComparison.Ordinal);
        Assert.Contains("New-Website", script.ScriptText, StringComparison.Ordinal);
        Assert.Contains("Set-Acl", script.ScriptText, StringComparison.Ordinal);
        Assert.Contains("AddSslCertificate", PowerShellScripts.UpdateBindings(
            "Site",
            [IisBindingBuilder.Create("https", 443, "example.com", "A909502DD82AE41433E6F83886B00D4277A32A7B")]).ScriptText,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AllRemoteScripts_HaveResultMarkerAndPowerShellCommand()
    {
        var scripts = new List<RemoteScript>
        {
            PowerShellScripts.Capabilities(),
            PowerShellScripts.ListWebsites(),
            PowerShellScripts.GetCertificates(),
            PowerShellScripts.CreateWebsite(new WebsiteCreateRequest
            {
                Name = "MyPortfolio",
                FolderName = "MyPortfolio",
                Port = 50082,
                ApplicationPoolName = "MyPortfolio",
                IisRoot = @"C:\inetpub"
            }),
            PowerShellScripts.StartWebsite("MyPortfolio"),
            PowerShellScripts.StopWebsite("MyPortfolio"),
            PowerShellScripts.RestartWebsite("MyPortfolio"),
            PowerShellScripts.DeleteWebsite("MyPortfolio"),
            PowerShellScripts.DeleteWebsiteFolder("MyPortfolio", @"C:\inetpub\MyPortfolio", @"C:\inetpub"),
            PowerShellScripts.UpdateBindings("MyPortfolio", [IisBindingBuilder.Create("http", 50082, null)])
        };

        foreach (var script in scripts)
        {
            Assert.Contains("__WSM_RESULT__", script.ScriptText, StringComparison.Ordinal);
            Assert.Contains("Write-WsmSuccess", script.ScriptText, StringComparison.Ordinal);
            Assert.Contains("Write-WsmFailure", script.ScriptText, StringComparison.Ordinal);
            Assert.StartsWith("powershell.exe ", PowerShellCommandBuilder.Build(script), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DeleteFolderScript_RejectsPathOutsideAllowedRoot()
    {
        var exception = Assert.Throws<ValidationException>(() =>
            PowerShellScripts.DeleteWebsiteFolder("Site", @"C:\Windows\System32", @"C:\inetpub"));
        Assert.Contains("outside", exception.Message, StringComparison.OrdinalIgnoreCase);
    }


}

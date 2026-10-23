using System.Diagnostics;
using System.Text;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services;
using WindowsServerManagement.Core.Services.Remote;

namespace WindowsServerManagement.Tests;

public sealed class PowerShellSyntaxTests
{
    [Fact]
    public async Task GeneratedRemoteScripts_AreValidPowerShell()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var directory = Path.Combine(Path.GetTempPath(), "wsm-scripts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var scripts = new Dictionary<string, string>
            {
                ["capabilities.ps1"] = PowerShellScripts.Capabilities().ScriptText,
                ["list-sites.ps1"] = PowerShellScripts.ListWebsites().ScriptText,
                ["certificates.ps1"] = PowerShellScripts.GetCertificates().ScriptText,
                ["create-site.ps1"] = PowerShellScripts.CreateWebsite(new WebsiteCreateRequest
                {
                    Name = "MyPortfolio",
                    FolderName = "MyPortfolio",
                    Port = 50082,
                    ApplicationPoolName = "MyPortfolio",
                    IisRoot = @"C:\inetpub"
                }).ScriptText,
                ["start-site.ps1"] = PowerShellScripts.StartWebsite("MyPortfolio").ScriptText,
                ["stop-site.ps1"] = PowerShellScripts.StopWebsite("MyPortfolio").ScriptText,
                ["restart-site.ps1"] = PowerShellScripts.RestartWebsite("MyPortfolio").ScriptText,
                ["delete-site.ps1"] = PowerShellScripts.DeleteWebsite("MyPortfolio").ScriptText,
                ["delete-folder.ps1"] = PowerShellScripts.DeleteWebsiteFolder(
                    "MyPortfolio",
                    @"C:\inetpub\MyPortfolio",
                    @"C:\inetpub").ScriptText,
                ["bindings.ps1"] = PowerShellScripts.UpdateBindings(
                    "MyPortfolio",
                    [
                        IisBindingBuilder.Create("http", 50082, null),
                        IisBindingBuilder.Create("https", 443, "portfolio.example.com", "A909502DD82AE41433E6F83886B00D4277A32A7B")
                    ]).ScriptText
            };

            foreach (var pair in scripts)
            {
                await File.WriteAllTextAsync(
                    Path.Combine(directory, pair.Key),
                    pair.Value,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }

            var parserPath = Path.Combine(directory, "parse.ps1");
            const string parser = """
                                  param([string]$Directory)
                                  $failed = $false
                                  Get-ChildItem -Path $Directory -Filter *.ps1 | Where-Object { $_.Name -ne 'parse.ps1' } | ForEach-Object {
                                      $tokens = $null
                                      $errors = $null
                                      [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors) | Out-Null
                                      if ($errors.Count -gt 0) {
                                          $failed = $true
                                          Write-Output ("FILE: " + $_.Name)
                                          $errors | ForEach-Object { Write-Output $_.Message }
                                      }
                                  }
                                  if ($failed) { exit 1 }
                                  exit 0
                                  """;
            await File.WriteAllTextAsync(parserPath, parser, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(parserPath);
            startInfo.ArgumentList.Add("-Directory");
            startInfo.ArgumentList.Add(directory);

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, output + Environment.NewLine + error);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task CapabilityScript_ExecutesLocally_AndReturnsParseableEnvelope()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "wsm-capability-" + Guid.NewGuid().ToString("N") + ".ps1");
        try
        {
            var script = PowerShellScripts.Capabilities();
            await File.WriteAllTextAsync(path, script.ScriptText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var startInfo = new ProcessStartInfo("powershell.exe")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(path);

            using var process = Process.Start(startInfo);
            Assert.NotNull(process);
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.True(process.ExitCode == 0, error);
            var result = new RemoteExecutionResult(process.ExitCode, output, error, false, false, TimeSpan.Zero);
            var report = RemoteResultParser.ParseData<CapabilityReport>(result);

            Assert.True(report.IsWindows);
            Assert.False(string.IsNullOrWhiteSpace(report.PowerShellVersion));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

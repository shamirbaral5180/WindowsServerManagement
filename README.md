# Windows Server Management

A Windows desktop application for connecting to multiple remote Windows Servers over SSH and creating and managing IIS websites on the selected server.

The UI is an IIS-style management console with a Connections sidebar, server tree, site table, and Actions pane. The light theme uses soft blue, green, teal, purple, and amber accents. Dark mode is also available. The sidebar and Actions panes are resizable, and F11 or the toolbar button toggles full-screen mode.

- **Framework:** C# 13, .NET 10 WPF, MVVM
- **UI:** CommunityToolkit.Mvvm, custom WPF styles and resources
- **Remote access:** SSH.NET (`Renci.SshNet`)
- **Remote IIS operations:** PowerShell over SSH, using an encoded PowerShell command payload
- **Local storage:** JSON profiles/settings and a JSONL activity log
- **Secrets:** Windows DPAPI (`CurrentUser`) – no plaintext passwords or passphrases in configuration or logs
- **Tests:** xUnit; validation, path safety, binding generation, configuration, DPAPI, command construction, response parsing, and PowerShell syntax validation

## What works

The source builds cleanly and all unit tests pass. The implementation includes:

- Multiple saved server profiles with independent SSH sessions.
- Password and private-key authentication, including optional key passphrases.
- SSH host-key fingerprint verification and pinning.
- Test Connection with OS, PowerShell, IIS, WebAdministration, and elevation diagnostics.
- Connect, disconnect, reconnect, timeout, cancellation, and error reporting.
- IIS website list with state, physical path, bindings, application pool, and server name.
- Create Website workflow that:
  1. Validates website, folder, port, hostname, HTTPS certificate, and pool inputs.
  2. Verifies the remote Windows/IIS/PowerShell/elevation environment.
  3. Checks for an existing site and binding conflict.
  4. Creates the remote folder under the configured IIS root (default `C:\inetpub`).
  5. Creates or reuses the application pool with the selected CLR version, pipeline mode, and start state.
  6. Creates the IIS site and binding.
  7. Grants the app-pool identity `Read & Execute` access to the folder.
  8. Rolls back resources it created if a step fails.
  9. Reports the exact folder path, binding, state, warnings, and errors.
- Start, stop, restart, delete, edit bindings, view details, open in browser, and refresh.
- Website deletion removes only IIS configuration. Physical file deletion is a separate confirmation and is refused for paths outside the configured IIS root or containing reparse points.
- Activity log with search, status filter, row details, and local persistence.
- Settings for theme, IIS root, default port, application pool pattern/runtime/pipeline, timeouts, and file logging.
- Self-contained publish instructions and a publish script.

## Important verification status

The solution was built and tested on this development machine. The following were verified locally:

- `dotnet build WindowsServerManagement.sln` succeeds with zero warnings/errors.
- `dotnet test` passes **35/35** tests.
- All generated remote PowerShell scripts pass the Windows PowerShell parser.
- The WPF application launches and the main pages and Add Server dialog render successfully.

**Not verified locally:** there was no configured Windows Server with IIS and OpenSSH available in this environment. The final acceptance test therefore still requires two real Windows Servers. Use [docs/INTEGRATION_TESTING.md](docs/INTEGRATION_TESTING.md) as the test checklist and [docs/REMOTE_SERVER_SETUP.md](docs/REMOTE_SERVER_SETUP.md) to prepare the servers. The layer design and remote execution flow are described in [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

## Solution structure

```text
WindowsServerManagement.sln
src/
  WindowsServerManagement.Core/
    Models/                 Profiles, settings, IIS models, operation results
    Exceptions/             Typed application exceptions
    Interfaces/             Repository, session, IIS, settings contracts
    Services/Validation/    Server and website validation
    Services/Remote/        Safe PowerShell construction, scripts, result parser
    Services/               Binding/path safety, remote IIS service
  WindowsServerManagement.Infrastructure/
    Configuration/          Application paths, JSON profile/settings repositories
    Security/               DPAPI secret store
    Ssh/                    SSH.NET session manager and host-key verification
    Storage/                Activity log repository/service
    Logging/                File logger provider
  WindowsServerManagement.App/
    ViewModels/             Shell, dashboard, servers, websites, logs, settings, dialogs
    Views/                  WPF pages and dialogs
    Services/               Server catalog, website operations, dialogs, navigation, theme
    Themes/                 Light/dark palettes
    Styles/                 Shared control styles
tests/
  WindowsServerManagement.Tests/
docs/
  REMOTE_SERVER_SETUP.md
  INTEGRATION_TESTING.md
scripts/
  publish.ps1
```

## Requirements

- Windows 10 or Windows 11, x64.
- .NET 10 SDK. The solution targets `net10.0-windows`.
- Visual Studio 2022/2026 with the .NET desktop workload, or the `dotnet` CLI.
- For remote management:
  - Windows Server with IIS and WebAdministration installed.
  - OpenSSH Server installed and running.
  - An SSH account that is a local administrator (IIS management requires elevation).
  - Windows PowerShell 5.1 available as `powershell.exe`.
  - `C:\inetpub` (or the configured IIS root) present on the server.
- For HTTPS creation: a certificate with a private key in `LocalMachine\My` on the remote server.

## Build and run

From the solution directory:

```powershell
dotnet restore WindowsServerManagement.sln
dotnet build WindowsServerManagement.sln
dotnet run --project src/WindowsServerManagement.App/WindowsServerManagement.App.csproj
```

Or open `WindowsServerManagement.sln` in Visual Studio and run `WindowsServerManagement.App`.

### Tests

```powershell
dotnet test tests/WindowsServerManagement.Tests/WindowsServerManagement.Tests.csproj
```

The generated remote scripts are parsed by the locally installed Windows PowerShell during the test run. They are not executed.

## First use

1. Open **Servers** → **Add server**.
2. Enter a display name, hostname/IP, SSH port (usually 22), username, and authentication.
   - For password authentication, enter the password.
   - For private-key authentication, choose the key file and enter a passphrase if the key is encrypted.
3. Use **Test connection**.
4. Review the SHA-256 host-key fingerprint and trust it only after verifying it against the server administrator or another trusted out-of-band source.
5. Save the profile.
6. Repeat for a second server.
7. Select a server in the top bar and click **Connect** on the Servers page.
8. Open **IIS Websites**, select the target server, and click **Create website**.
9. Enter a name, folder, port, optional hostname, protocol, and application pool. For HTTPS, select a certificate or enter its thumbprint.
10. Monitor the progress and the final folder path/binding/state. If a step fails, the UI displays the error and the service attempts to remove resources created by that attempt.

## Data and secret storage

All local data is stored under:

```text
%LOCALAPPDATA%\WindowsServerManagement\
  servers.json      Server profiles and trusted host-key fingerprints
  settings.json     Application settings
  secrets.dat       DPAPI-encrypted credentials
  activity.jsonl    Local activity log
  logs\             Application diagnostics
```

- Passwords and private-key passphrases are encrypted with `ProtectedData.Protect` using `DataProtectionScope.CurrentUser`. They can only be decrypted by the same Windows user on the same machine/account context.
- Private key files are not copied into the application. The profile stores only the key path; the passphrase is encrypted.
- Trusted host-key fingerprints are public, persisted in `servers.json`.
- Activity log details are explicitly authored by the application and do not include credentials.
- The application logger writes connection metadata and operation outcomes, not passwords, passphrases, or private key contents.

## SSH / remote execution design

The application does **not** assume that an interactive SSH shell accepts PowerShell. Each remote script is wrapped into a single command:

```text
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand <base64 UTF-16LE script>
```

The entire PowerShell script is base64-encoded before being passed to the SSH command, so user-provided values cannot break out into the default shell. All user values are also rendered as escaped PowerShell single-quoted literals and validated by the C# layer. Results are returned as a marker plus a base64-encoded JSON envelope to avoid console encoding problems.

This works when the OpenSSH default shell is either `cmd.exe` or PowerShell. See [docs/REMOTE_SERVER_SETUP.md](docs/REMOTE_SERVER_SETUP.md) for the remote requirements and verification commands.

## Security behavior

- SSH host-key verification is never disabled. Unknown keys require explicit review; changed keys are rejected as a possible man-in-the-middle event.
- The application refuses to trust an unknown host key automatically.
- Destructive operations require confirmation. Website file deletion has a second, separate confirmation.
- Creating or changing IIS sites requires an elevated remote account; the application checks this before administrative operations.
- Folder deletion is restricted to the configured IIS root and refuses reparse points/junctions.
- The application does not modify firewall rules, SSH configuration, IIS security settings, or Windows file-system security automatically (aside from the one `Read & Execute` ACL for the created site folder).
- Website creation grants only `Read & Execute` to the application-pool identity. It does not grant write/modify permissions.
- HTTPS operations require an existing certificate with a private key; the application does not weaken certificate validation.
- No secrets are logged. See [docs/INTEGRATION_TESTING.md](docs/INTEGRATION_TESTING.md) for the security verification checklist.

## IIS website behavior

- Default remote root: `C:\inetpub` (configurable in Settings).
- Default physical path: `<IIS root>\<website name>`, editable before creation.
- Default binding: `http://*:<port>/`.
- Port conflict check compares protocol, port, and host header.
- Existing folders are rejected unless **Use existing folder** is explicitly enabled.
- A new application pool is created with the selected CLR version and pipeline mode. The pool can be started immediately or left stopped. If the pool already exists, it is reused without changing its settings.
- The site is created, the app-pool identity receives `Read & Execute`, and then the site is started if requested.
- If any failure occurs after resources are created, only those resources are rolled back. Existing folders/pools are not deleted.
- Deleting a site removes only IIS configuration. Files remain unless the separate folder-deletion confirmation is accepted.
- Folder deletion is refused if another IIS site still uses the folder.

## Publishing a self-contained executable

Using the provided script:

```powershell
./scripts/publish.ps1
```

Or manually:

```powershell
dotnet publish src/WindowsServerManagement.App/WindowsServerManagement.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:PublishReadyToRun=true `
  -o artifacts/publish/win-x64
```

The resulting `Server Manager` executable does not require .NET to be installed on the destination machine. WPF single-file publishing should not be trimmed.

Framework-dependent alternatives:

```powershell
# Requires .NET 10 Desktop Runtime on the target machine
dotnet publish src/WindowsServerManagement.App/WindowsServerManagement.App.csproj -c Release -r win-x64 --self-contained false -o artifacts/publish/framework-dependent
```

## Troubleshooting

| Problem | Check |
| --- | --- |
| `Permission denied (publickey,password)` | Verify username, password, or key/passphrase. For admin key auth on Windows OpenSSH, keys must be in `administrators_authorized_keys` with correct ACLs. |
| `The remote command did not return a valid result` | Verify `powershell.exe` is available to the SSH account. Run the diagnostic test and inspect the application log. |
| `WebAdministration module is not available` | Install IIS management tools and ensure `Import-Module WebAdministration` works in an elevated PowerShell session. |
| `SSH account is not elevated` | Use a member of the local Administrators group. IIS management requires an elevated session. |
| Port conflict on Create Website | Another site is already using the same protocol/port/host. Choose another port or host header. |
| HTTPS certificate not found | Import the PFX to the remote `LocalMachine\My` store and ensure it has a private key. |
| Host key changed / connection rejected | Verify the server was intentionally re-keyed. Use **Edit server → Forget trusted SSH host key**, then reconnect and verify the new fingerprint. |
| Connection times out | Verify host, SSH port, firewall/VPN, and increase the connection timeout in Settings. |
| UI appears stuck | Use the Cancel button during a long operation; remote commands are serialized per server and have timeouts. |

## Known limitations

- The application manages IIS on remote Windows Servers, not local IIS via `Microsoft.Web.Administration`. The remote target case is implemented end-to-end with PowerShell over SSH; a local IIS provider would be a separate feature.
- HTTPS certificate selection lists certificates from `LocalMachine\My` but does not upload or create certificates.
- The app does not manage IIS application pools as a separate top-level feature; it creates and reuses pools as part of website creation.
- A real-server end-to-end acceptance test remains outstanding in this environment.

## Acceptance test checklist

1. Prepare two Windows Servers with IIS, OpenSSH, an elevated SSH account, and `C:\inetpub`.
2. Add both servers to the app; verify both host-key fingerprints out of band.
3. Connect to Server A, select Server A, create `MyPortfolio` on port `50082`.
4. On Server A verify:
   - `C:\inetpub\MyPortfolio` exists.
   - `Get-Website -Name MyPortfolio` exists.
   - `Get-WebBinding -Name MyPortfolio` shows port 50082.
   - The app shows the same path/binding.
5. Connect to Server B, create a different site on a different port.
6. Confirm Server A did not receive Server B's site and vice versa.
7. Test start/stop/restart, edit bindings, delete site (keep files), then separately delete the folder.
8. Review Activity Logs and confirm no password, passphrase, or key material appears in `secrets.dat`, `servers.json`, `activity.jsonl`, or `logs\`.

See [docs/INTEGRATION_TESTING.md](docs/INTEGRATION_TESTING.md) for the full integration and security test matrix.

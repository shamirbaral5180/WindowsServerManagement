# Windows Server Management v1.0.0

First public release of the Windows Server Management WPF desktop application.

## Highlights

- Light, IIS-style WPF interface with a light theme by default and optional dark mode.
- Manage multiple remote Windows Server SSH profiles with independent sessions.
- Password and private-key authentication with DPAPI-protected secrets.
- SSH host-key verification and persistent SHA-256 fingerprint pinning.
- Remote IIS capability diagnostics: Windows, PowerShell, IIS, WebAdministration, W3SVC, and administrator rights.
- Create IIS websites on the selected remote server:
  - Validate name, folder, port, hostname, binding, and certificate.
  - Check site and binding conflicts.
  - Create the remote folder under the configured IIS root.
  - Create or reuse the application pool.
  - Create the site and HTTP/HTTPS binding.
  - Grant the app-pool identity Read & Execute access.
  - Roll back resources created by a failed attempt.
  - Report the exact folder path, binding, state, and warnings.
- Manage existing sites: start, stop, restart, edit bindings, view configuration, open in browser, delete site, and optionally delete the physical folder after a separate confirmation.
- Dashboard, server profiles, searchable IIS website list, activity log with filters, and settings.
- Self-contained Windows x64 executable included as a release asset.

## Verification

- Release build succeeds with zero warnings/errors.
- 33 automated tests pass.
- Generated remote PowerShell scripts pass the Windows PowerShell parser.
- The capability probe executes locally and returns a parseable result.
- The published self-contained executable launches successfully.

## Requirements

- Windows 10/11 x64.
- Remote Windows Server with IIS, the WebAdministration module, and OpenSSH Server.
- An elevated SSH account for IIS management.
- Windows PowerShell 5.1 on the remote server.
- See `docs/REMOTE_SERVER_SETUP.md`.

## Known limitation

The final remote end-to-end acceptance test must be run against real Windows Servers with IIS and SSH configured. The development environment did not include such a server. Use `docs/INTEGRATION_TESTING.md` for the complete test checklist.

## Data and security

- Profiles/settings: `%LOCALAPPDATA%\WindowsServerManagement\`
- Credentials are encrypted with Windows DPAPI for the current user.
- Plaintext passwords and private keys are not stored in configuration or logs.
- Host-key verification is never disabled.
- Website files are never deleted automatically; folder deletion requires a second explicit confirmation.

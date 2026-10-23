# Integration Testing Guide

The automated unit tests cover validation, path safety, configuration, DPAPI secret storage, remote command construction, result parsing, and PowerShell syntax. They do not prove that a particular Windows Server is configured correctly or that IIS can be modified on it.

Use this guide to verify the complete remote workflow against two real Windows Servers.

## Test environment

- Two Windows Servers, for example `WEBS01` and `WEBS02`.
- IIS installed with the WebAdministration module.
- OpenSSH Server running.
- An elevated SSH account on each server.
- `C:\inetpub` exists on both.
- Different IP addresses/hostnames and preferably different credentials.
- For HTTPS: a certificate with private key in `LocalMachine\My` on at least one server.

Prepare servers using [REMOTE_SERVER_SETUP.md](REMOTE_SERVER_SETUP.md).

## 1. Connection and host-key tests

1. Add Server A and Server B profiles.
2. Use **Test connection** on each.
3. Verify the SHA-256 fingerprints out of band.
4. Trust the unknown host keys only after verification.
5. Confirm the diagnostics dialog reports:
   - Windows OS
   - PowerShell version
   - IIS installed
   - WebAdministration available
   - W3SVC running
   - elevated administrator = Yes
   - IIS management ready = Yes
6. Connect to both servers at the same time.
7. Confirm each server shows its own `Connected` state and independent website counts.
8. Disconnect Server A; confirm Server B remains connected.
9. Reconnect Server A; confirm its state and diagnostics recover.

### Negative tests

- Use a wrong password: expect an authentication error without secret leakage.
- Use a wrong SSH port: expect a timeout/connection error.
- Use an account that is not an administrator: expect the capability check to block site creation.
- Stop `sshd` and attempt a connection: expect a clear connection error.
- Edit a saved host-key fingerprint in `servers.json` to a different value, then reconnect: expect a host-key mismatch rejection. Restore or forget the key afterward.

## 2. Create Website acceptance test

1. Connect to WEBS01.
2. Select WEBS01 in the top server selector.
3. Open **IIS Websites** and click **Create website**.
4. Enter:
   - Name: `MyPortfolio`
   - Folder: `MyPortfolio`
   - Port: `50082`
   - Hostname: blank
   - Protocol: HTTP
   - Application pool: `MyPortfolio`
5. Click **Create website**.
6. Confirm the app reports:
   - Folder: `C:\inetpub\MyPortfolio`
   - Binding: `http://*:50082/`
   - Application pool: `MyPortfolio`
   - State: Started (if Start Immediately is enabled)
7. On WEBS01 verify:

```powershell
Test-Path 'C:\inetpub\MyPortfolio'
Get-Website -Name 'MyPortfolio' | Format-List Name, State, PhysicalPath, ApplicationPool
Get-WebBinding -Name 'MyPortfolio' | Format-Table protocol, bindingInformation
Get-Acl 'C:\inetpub\MyPortfolio' | Format-List
```

8. Confirm the ACL includes `IIS AppPool\MyPortfolio` with `ReadAndExecute` (or an appropriate identity for the configured pool).
9. Confirm the folder does **not** exist on WEBS02.

### Repeat on the second server

1. Connect to WEBS02.
2. Select WEBS02 and create `SecondSite` on port `50083`.
3. Verify `C:\inetpub\SecondSite` and the IIS site exist only on WEBS02.
4. Confirm `MyPortfolio` is not present on WEBS02 and `SecondSite` is not present on WEBS01.

This proves the selected server is the target for both folder creation and IIS operations.

## 3. Validation and conflict tests

- Try to create a site with an existing name: expect a conflict before changes.
- Try to reuse an existing folder without **Use existing folder**: expect a conflict.
- Try to create a second site on the same protocol/port/host: expect a binding conflict.
- Try a folder name containing `..`, `/`, `\`, or a reserved device name: expect validation failure.
- Try an invalid port: expect validation failure.
- Try HTTPS without a certificate thumbprint: expect validation failure.
- Try HTTPS with a nonexistent thumbprint: expect a clear remote error and no partial site left behind.
- Try **Use existing folder** with an existing folder: confirm the folder is reused and the site is created.
- Simulate a failure after site creation (for example, stop W3SVC or use an invalid certificate) and confirm the app attempts rollback and does not report success.

## 4. Website management tests

For each site:

- Refresh the list; confirm state/path/bindings/pool.
- Stop; confirm state changes to Stopped in IIS and in the app.
- Start; confirm state changes to Started.
- Restart; confirm the site is running afterward.
- Edit bindings:
  - Add a second HTTP binding on another port.
  - Edit the hostname.
  - Add/update an HTTPS binding with a certificate.
  - Remove a binding.
  - Confirm IIS bindings match the app.
- Open in browser when the server/port is reachable.
- View configuration and confirm the displayed path matches IIS.

## 5. Deletion tests

1. Select a test website and click **Delete website**.
2. Confirm the first dialog explicitly says files are not deleted.
3. After deletion, verify the IIS site is gone and the folder still exists.
4. Confirm the optional second dialog offers to delete the physical folder.
5. Decline once; confirm files remain.
6. Repeat on another test site and accept folder deletion.
7. Verify:
   - The site is gone.
   - The folder is gone.
   - A folder outside the configured IIS root is never offered for automatic deletion.
   - A folder containing a junction/symlink is refused.
   - The site's application pool is left in place (not deleted automatically).

## 6. Security tests

After running the workflow, inspect local data:

```powershell
Get-Content "$env:LOCALAPPDATA\WindowsServerManagement\servers.json"
Get-Content "$env:LOCALAPPDATA\WindowsServerManagement\settings.json"
Get-Content "$env:LOCALAPPDATA\WindowsServerManagement\activity.jsonl"
Get-ChildItem "$env:LOCALAPPDATA\WindowsServerManagement\logs" | ForEach-Object { Get-Content $_.FullName }
```

Verify:

- No plaintext password or passphrase appears anywhere.
- `secrets.dat` does not contain recognizable plaintext.
- Private key contents are not copied to app data; only the key path is present.
- Activity log details contain useful error context but no secrets.
- Host-key mismatch is rejected.
- Confirmations are required for delete and folder delete.
- The app does not change firewall/SSH/IIS security automatically.
- Website folder ACLs grant only the intended read/execute access.

### DPAPI scope check

If you copy `secrets.dat` and `servers.json` to another Windows account or machine, saved credentials should not decrypt. The app should show a clear credential error rather than exposing data.

## 7. Cancellation, timeout, and recovery tests

- Set a very low connection timeout in Settings and connect to an unreachable IP. Confirm the UI stays responsive and reports a timeout.
- Start a long operation and press Cancel. Confirm the operation is cancelled and the UI remains usable.
- Disconnect the server while a website operation is running. Confirm the error is reported and the session state changes to Error.
- Restart the application while a session is active. Confirm it starts with no active sessions and profiles persist.
- Corrupt `servers.json` (on a test machine) and start the app. Confirm it fails with an actionable error rather than silently losing data. Restore the backup.

## 8. UI checks

- Add/Edit/Delete server profile.
- Connection status colors: Disconnected, Connecting, Connected, Error.
- Server selector switches the active server.
- Dashboard counts update after connection, website creation, start/stop, deletion.
- Activity Logs search and status filter work.
- Settings persist and the theme changes between light and dark.
- No operation freezes the UI (SSH and remote scripts run asynchronously).
- Error and success notifications can be cleared.

## 9. Expected outcome

The final acceptance test passes when:

- Two profiles are saved and independently connectable.
- The target server selector determines which machine receives an operation.
- `MyPortfolio` on port `50082` creates `C:\inetpub\MyPortfolio` and the IIS site on the correct remote server.
- The second server receives only its own site.
- The app reports exact paths, bindings, state, and any error details.
- No secrets appear in configuration or logs.

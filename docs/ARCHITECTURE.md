# Architecture

## Layers

### `WindowsServerManagement.Core`

Domain and application contracts with no WPF or SSH dependencies.

- `Models`: server profiles, encrypted-secret DTOs, IIS website/binding/certificate models, settings, activity entries, capability and operation results.
- `Interfaces`: persistence, secret store, activity log, SSH session manager, IIS website service, settings service.
- `Services/Validation`: server and website validation rules.
- `Services`: Windows path safety, IIS binding construction, `IisWebsiteService`.
- `Services/Remote`: safe PowerShell literal construction, encoded-command builder, script templates, and result-envelope parser.

### `WindowsServerManagement.Infrastructure`

Concrete I/O and platform implementations.

- JSON profile/settings repositories with atomic file replacement.
- DPAPI secret storage (CurrentUser scope).
- JSONL activity log storage.
- File logging provider.
- SSH.NET session manager:
  - Independent sessions keyed by profile ID.
  - Host-key pinning with SHA-256 fingerprints.
  - Serialized commands per server.
  - Connection/command timeouts and cancellation.
  - Encoded PowerShell command execution.
  - No host-key verification bypass.

### `WindowsServerManagement.App`

WPF shell, MVVM view models, UI services, and dialogs.

- `ServerCatalog`: loads profiles, coordinates connection/host-key trust, and exposes observable server state.
- `WebsiteManagementService`: application-level IIS workflow orchestration, activity logging, and count refresh.
- `DialogService`: modal confirmation, editor, create-website, bindings, message, and diagnostics dialogs.
- `ThemeService`: swaps light/dark palettes.
- View models: dashboard, servers, websites, logs, settings, and dialogs.
- Views: WPF XAML; code-behind is limited to window mechanics (drag, close, window state).

## Remote operation flow

```text
UI command
  -> ServerCatalog ensures a connection (prompting for unknown host key if needed)
  -> WebsiteManagementService validates and logs the operation
  -> Core remote service verifies capabilities and builds a RemoteScript
  -> SshSessionManager creates an encoded PowerShell command
  -> SSH.NET executes the command on the selected server
  -> PowerShell returns a marker + base64 JSON envelope
  -> RemoteResultParser deserializes and validates the result
  -> WebsiteManagementService logs success/failure
  -> UI refreshes state and shows notifications
```

## Connection and credential flow

1. The editor collects profile fields and a secret.
2. `ServerCatalog` validates the profile and resolves the secret:
   - a newly typed secret wins;
   - otherwise a stored secret is reused;
   - the user can explicitly clear the stored secret.
3. `DpapiSecretStore` encrypts passwords/passphrases for the current Windows user.
4. `SshSessionManager` resolves the authentication method and connects with a timeout.
5. The host key handler compares the server fingerprint with the pinned profile fingerprint:
   - unknown key: connection is rejected, then the user may review/trust it;
   - different key: connection is rejected as a mismatch, with no automatic trust.
6. On successful trust/connect, the fingerprint is persisted in the profile.

## Error handling

- Typed exceptions (`ValidationException`, `RemoteOperationException`, `RemoteCapabilityException`, `HostKeyVerificationException`) carry actionable, secret-free messages.
- Remote PowerShell scripts wrap work in `try/catch`, return a structured failure, and exit non-zero.
- Website creation tracks created resources and rolls back only resources it created.
- Binding edits snapshot current bindings and attempt restoration on failure.
- File deletion is a separate operation with path, in-use, and reparse-point checks.

## Threading

- UI operations are asynchronous; view models use CommunityToolkit.Mvvm async commands.
- SSH sessions have a dedicated command semaphore, so multiple servers can operate independently.
- UI-bound collection changes and state notifications are marshalled through the WPF dispatcher where operations originate off the UI thread.
- Cancellation tokens flow from dialogs/page commands into SSH connection/command execution.

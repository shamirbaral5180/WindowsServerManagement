# Remote Windows Server Setup

This guide prepares a Windows Server for the application's SSH + PowerShell + IIS workflow. Run the commands in an elevated PowerShell session on the **remote server** unless stated otherwise.

> The application connects over SSH and executes an encoded `powershell.exe` command. It does not use WinRM. IIS management requires an elevated remote account.

## 1. Install IIS and management tools

Windows Server (Desktop Experience):

```powershell
Install-WindowsFeature Web-Server, Web-Mgmt-Tools -IncludeManagementTools
Start-Service W3SVC
Set-Service W3SVC -StartupType Automatic
```

Verify:

```powershell
Import-Module WebAdministration
Get-Service W3SVC
Get-Website
```

The `WebAdministration` module is required. If `Import-Module WebAdministration` fails, install the IIS Management Console/management tools.

Make sure the configured IIS root exists. The application default is:

```powershell
Test-Path 'C:\inetpub'
# Create it only if your server policy allows it:
# New-Item -ItemType Directory -Path 'C:\inetpub' -Force
```

## 2. Install and start OpenSSH Server

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Start-Service sshd
Set-Service sshd -StartupType Automatic
```

If the firewall rule does not exist:

```powershell
New-NetFirewallRule -Name sshd -DisplayName 'OpenSSH Server (sshd)' -Enabled True -Direction Inbound -Protocol TCP -Action Allow -LocalPort 22
```

Check the service:

```powershell
Get-Service sshd
```

Do not expose SSH more broadly than necessary. Restrict source addresses with firewall/security-group rules according to your environment.

## 3. Create or select an administrative SSH account

IIS site creation and management require administrator rights. Use a dedicated local administrator account where possible.

```powershell
# Example only. Choose a strong password and follow your security policy.
$password = Read-Host -AsSecureString 'Password for iisadmin'
New-LocalUser -Name 'iisadmin' -Password $password -Description 'Remote IIS management'
Add-LocalGroupMember -Group 'Administrators' -Member 'iisadmin'
```

Verify the account can log in over SSH. The account's session must be elevated for `New-Website`, `Set-Acl`, and related operations. The application's capability check reports whether the session is elevated.

## 4. Configure password or public-key authentication

### Password authentication

Check `C:\ProgramData\ssh\sshd_config` on the server:

```text
PasswordAuthentication yes
```

Then restart:

```powershell
Restart-Service sshd
```

### Public-key authentication

For a **non-administrator** account, place the public key in:

```text
C:\Users\<username>\.ssh\authorized_keys
```

For an **administrator** account, Windows OpenSSH uses:

```text
C:\ProgramData\ssh\administrators_authorized_keys
```

The `administrators_authorized_keys` file must be owned by `Administrators`/`SYSTEM` and must not be writable by standard users. A typical hardened ACL is:

```powershell
icacls.exe "C:\ProgramData\ssh\administrators_authorized_keys" /inheritance:r /grant "Administrators:F" /grant "SYSTEM:F"
```

Restart `sshd` after changes.

The application supports encrypted OpenSSH, PKCS#8, and PuTTY PPK key formats through SSH.NET. The private key stays on the client machine; only the passphrase is stored encrypted with DPAPI.

## 5. Verify PowerShell is reachable over SSH

The application runs PowerShell with:

```text
powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -OutputFormat Text -EncodedCommand <base64 UTF-16LE script>
```

This works whether the SSH default shell is `cmd.exe` or PowerShell, as long as `powershell.exe` is on `PATH`. From a **client** PowerShell window:

```powershell
ssh iisadmin@SERVER powershell.exe -NoProfile -NonInteractive -Command "$PSVersionTable.PSVersion"
```

On Windows OpenSSH, the default shell can be configured in the registry:

```powershell
New-ItemProperty -Path 'HKLM:\SOFTWARE\OpenSSH' -Name DefaultShell `
  -Value 'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe' `
  -PropertyType String -Force
Restart-Service sshd
```

Do not set a restricted/custom shell that cannot launch `powershell.exe`. The application expects Windows PowerShell 5.1 with the IIS module.

## 6. Verify an elevated remote IIS operation

SSH into the server as the account the application will use:

```powershell
ssh iisadmin@SERVER
```

Then run:

```powershell
powershell.exe -NoProfile -Command "Import-Module WebAdministration; (New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator); Get-Service W3SVC"
```

Expected: `True` for the administrator check and `Running` for W3SVC.

The application runs an equivalent capability probe and blocks administrative IIS operations if any requirement is missing.

## 7. Host-key fingerprint verification

The application pins the SSH host key on first use. Do not trust a fingerprint you have not verified.

On the server, view the host public key fingerprint:

```powershell
ssh-keygen.exe -lf C:\ProgramData\ssh\ssh_host_ed25519_key.pub
ssh-keygen.exe -lf C:\ProgramData\ssh\ssh_host_rsa_key.pub
```

Compare the displayed SHA-256 fingerprint with the fingerprint shown by the application. The application displays it as `SHA256:<value>`.

You can also obtain the key from a trusted network path:

```powershell
ssh-keyscan -t ed25519 SERVER
```

Do not use `ssh-keyscan` output alone as proof of identity; verify it through a trusted channel or console access.

If a server is intentionally re-keyed, the application will reject the new key as a possible man-in-the-middle event. Use **Edit server → Forget the trusted SSH host key on save**, connect again, and re-verify the new fingerprint.

## 8. HTTPS certificates

To create an HTTPS site, the certificate must exist in the remote computer store:

```text
Store: LocalMachine\My
Requirement: private key present
```

List certificates:

```powershell
Get-ChildItem Cert:\LocalMachine\My | Where-Object HasPrivateKey |
  Select-Object Subject, Thumbprint, NotAfter
```

For a test self-signed certificate on the remote server (do not use this for production public sites):

```powershell
$cert = New-SelfSignedCertificate `
  -DnsName 'portfolio.example.com' `
  -CertStoreLocation 'Cert:\LocalMachine\My' `
  -KeyAlgorithm RSA -KeyLength 2048
$cert.Thumbprint
```

The application does not install certificates or change IIS certificate trust settings.

## 9. Recommended hardening

- Use key-based authentication for automation; disable password authentication when practical.
- Restrict SSH source IP ranges.
- Use a dedicated administrator account for remote IIS management, not a shared domain admin.
- Keep Windows, OpenSSH, and IIS updated.
- Do not enable `PermitRootLogin`/root-style unrestricted access. Follow your organization's hardened OpenSSH baseline.
- Do not weaken file-system ACLs. The application grants only `Read & Execute` to the application-pool identity.
- Keep the IIS root on a controlled volume and avoid junction points/symlinks; the app refuses folder deletion when reparse points are present.
- Review Activity Logs and the app diagnostic logs for unexpected operations.

## 10. What the app does not change automatically

The application does not:

- Disable SSH host-key verification.
- Disable password or key authentication.
- Change Windows Firewall rules.
- Modify `sshd_config`.
- Grant Modify/Full Control on website folders.
- Install or trust certificates.
- Delete website files without a second explicit confirmation.
- Delete folders outside the configured IIS root or folders containing reparse points.

using WindowsServerManagement.Core.Exceptions;
using WindowsServerManagement.Core.Models;
using WindowsServerManagement.Core.Services.Validation;

namespace WindowsServerManagement.Core.Services.Remote;

public static class PowerShellScripts
{
    private const string Header = """
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

function Write-WsmPayload {
    param([System.Collections.IDictionary]$Payload)
    $json = ConvertTo-Json -InputObject $Payload -Compress -Depth 12
    $base64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
    Write-Output "__WSM_RESULT__$base64"
}

function Write-WsmSuccess {
    param([System.Collections.IDictionary]$Data)
    Write-WsmPayload ([ordered]@{ success = $true; data = $Data })
}

function Write-WsmFailure {
    param([string]$Message)
    Write-WsmPayload ([ordered]@{ success = $false; error = $Message })
}

function Get-WsmBindingParts {
    param([string]$BindingInformation)
    $segments = $BindingInformation.Split(':')
    if ($segments.Length -lt 3) {
        return $null
    }

    $port = 0
    if (-not [int]::TryParse($segments[$segments.Length - 2], [ref]$port)) {
        return $null
    }

    return [pscustomobject]@{
        IpAddress = ($segments[0..($segments.Length - 3)] -join ':')
        Port = $port
        Hostname = $segments[$segments.Length - 1]
    }
}

function Get-WsmThumbprint {
    param($Value)
    if ($null -eq $Value) {
        return ''
    }

    if ($Value -is [byte[]]) {
        return ([BitConverter]::ToString($Value)).Replace('-', '').ToUpperInvariant()
    }

    return ([string]$Value).Replace(' ', '').Replace(':', '').ToUpperInvariant()
}

""";

    public static RemoteScript Capabilities()
    {
        const string body = """
$os = $null
try {
    $os = Get-CimInstance -ClassName Win32_OperatingSystem -ErrorAction Stop
} catch {
    $os = $null
}

$isWindows = $env:OS -eq 'Windows_NT'
$webAdministrationAvailable = $false
try {
    $webAdministrationAvailable = @(Get-Module -ListAvailable -Name WebAdministration -ErrorAction Stop).Count -gt 0
} catch {
    $webAdministrationAvailable = $false
}

$iisInstalled = Test-Path -LiteralPath 'HKLM:\SOFTWARE\Microsoft\InetStp'
$w3svc = Get-Service -Name W3SVC -ErrorAction SilentlyContinue
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
$isElevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

$result = [ordered]@{
    osCaption = $(if ($null -ne $os) { [string]$os.Caption } else { '' })
    osVersion = $(if ($null -ne $os) { [string]$os.Version } else { '' })
    computerName = [Environment]::MachineName
    userName = [Environment]::UserName
    powerShellVersion = $PSVersionTable.PSVersion.ToString()
    w3svcStatus = $(if ($null -ne $w3svc) { [string]$w3svc.Status } else { 'NotInstalled' })
    isWindows = [bool]$isWindows
    iisInstalled = [bool]$iisInstalled
    webAdministrationAvailable = [bool]$webAdministrationAvailable
    isElevated = [bool]$isElevated
    canManageIis = [bool]($isWindows -and $iisInstalled -and $webAdministrationAvailable -and $isElevated)
}

Write-WsmSuccess $result
""";

        return Wrap("Server capability check", body, importIis: false, timeoutSeconds: 30);
    }

    public static RemoteScript ListWebsites()
    {
        const string body = """
$sites = @()
foreach ($site in @(Get-Website)) {
    $siteBindings = @()
    foreach ($binding in @(Get-WebBinding -Name $site.Name -ErrorAction SilentlyContinue)) {
        $parts = Get-WsmBindingParts ([string]$binding.bindingInformation)
        if ($null -eq $parts) {
            continue
        }

        $siteBindings += [ordered]@{
            protocol = [string]$binding.protocol
            ipAddress = [string]$parts.IpAddress
            port = [int]$parts.Port
            hostname = [string]$parts.Hostname
            certificateThumbprint = (Get-WsmThumbprint $binding.certificateHash)
            bindingInformation = [string]$binding.bindingInformation
        }
    }

    $applicationPool = ''
    try {
        $applicationPool = [string](Get-Item "IIS:\Sites\$($site.Name)" -ErrorAction Stop).applicationPool
    } catch {
        try {
            $applicationPool = [string]$site.applicationPool
        } catch {
            $applicationPool = ''
        }
    }

    $sites += [ordered]@{
        name = [string]$site.Name
        state = [string]$site.State
        physicalPath = [string]$site.PhysicalPath
        applicationPool = $applicationPool
        bindings = @($siteBindings)
    }
}

$result = [ordered]@{
    sites = @($sites)
    serverTime = (Get-Date).ToString('o')
}

Write-WsmSuccess $result
""";

        return Wrap("List IIS websites", body, importIis: true, timeoutSeconds: 60);
    }

    public static RemoteScript GetCertificates()
    {
        const string body = """
$certificates = @()
foreach ($certificate in @(Get-ChildItem -Path 'Cert:\LocalMachine\My' | Where-Object { $_.HasPrivateKey })) {
    $certificates += [ordered]@{
        subject = [string]$certificate.Subject
        thumbprint = [string]$certificate.Thumbprint
        notAfter = $certificate.NotAfter.ToString('o')
        friendlyName = [string]$certificate.FriendlyName
    }
}

$result = [ordered]@{
    certificates = @($certificates)
}

Write-WsmSuccess $result
""";

        return Wrap("List server certificates", body, importIis: false, timeoutSeconds: 45);
    }

    public static RemoteScript CreateWebsite(WebsiteCreateRequest request)
    {
        var validation = WebsiteValidator.ValidateCreateRequest(request);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        if (!WebsitePathSafety.BuildPhysicalPath(request.IisRoot, request.FolderName, out _, out var pathError))
        {
            throw new ValidationException(pathError);
        }

        var siteName = request.Name.Trim();
        var folderName = request.FolderName.Trim();
        var hostName = request.Hostname.Trim();
        var protocol = request.Protocol;
        var thumbprint = WebsiteValidator.NormalizeThumbprint(request.CertificateThumbprint);
        var applicationPool = request.ApplicationPoolName.Trim();
        var runtimeVersion = string.Equals(request.ApplicationPoolRuntimeVersion, "No Managed Code", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : request.ApplicationPoolRuntimeVersion.Trim();
        var pipelineMode = request.ApplicationPoolPipelineMode.Trim();

        var body = $$"""
$siteName = {{PowerShellLiterals.Quote(siteName)}}
$folderName = {{PowerShellLiterals.Quote(folderName)}}
$rootPath = {{PowerShellLiterals.Quote(request.IisRoot.Trim())}}
$port = {{request.Port}}
$hostName = {{PowerShellLiterals.Quote(hostName)}}
$protocol = {{PowerShellLiterals.Quote(protocol)}}
$thumbprint = {{PowerShellLiterals.Quote(thumbprint)}}
$appPoolName = {{PowerShellLiterals.Quote(applicationPool)}}
$runtimeVersion = {{PowerShellLiterals.Quote(runtimeVersion)}}
$pipelineMode = {{PowerShellLiterals.Quote(pipelineMode)}}
$useExistingFolder = {{PowerShellLiterals.Boolean(request.UseExistingFolder)}}
$startImmediately = {{PowerShellLiterals.Boolean(request.StartImmediately)}}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'The SSH account is not elevated. IIS website creation requires an administrator session.'
}

if (Get-Website -Name $siteName -ErrorAction SilentlyContinue) {
    throw "An IIS website named '$siteName' already exists."
}

$conflict = $null
foreach ($existingBinding in @(Get-WebBinding -ErrorAction SilentlyContinue)) {
    $parts = Get-WsmBindingParts ([string]$existingBinding.bindingInformation)
    if ($null -eq $parts) {
        continue
    }

    if (([string]$existingBinding.protocol).ToLowerInvariant() -eq $protocol -and
        [int]$parts.Port -eq [int]$port -and
        ([string]$parts.Hostname).ToLowerInvariant() -eq $hostName.ToLowerInvariant()) {
        $conflict = [pscustomobject]@{
            Site = [string]$existingBinding.Name
            BindingInformation = [string]$existingBinding.bindingInformation
        }
        break
    }
}

if ($null -ne $conflict) {
    throw "The binding $protocol port $port with host '$hostName' is already used by website '$($conflict.Site)'."
}

if (-not [IO.Path]::IsPathRooted($rootPath)) {
    throw "IIS root '$rootPath' must be an absolute path."
}

$rootFull = [IO.Path]::GetFullPath($rootPath).TrimEnd('\')
if ($rootFull.Length -le 3) {
    throw 'IIS root cannot be the root of a drive.'
}

if (-not (Test-Path -LiteralPath $rootFull -PathType Container)) {
    throw "IIS root '$rootFull' does not exist on the remote server."
}

$invalidChars = [IO.Path]::GetInvalidFileNameChars()
if ([string]::IsNullOrWhiteSpace($folderName) -or
    $folderName -eq '.' -or
    $folderName -eq '..' -or
    $folderName.Contains('\') -or
    $folderName.Contains('/') -or
    $folderName.IndexOfAny($invalidChars) -ge 0) {
    throw "Physical folder name '$folderName' is not valid."
}

$physicalPath = [IO.Path]::GetFullPath([IO.Path]::Combine($rootFull, $folderName))
$rootPrefix = $rootFull + '\'
if (-not $physicalPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The resolved physical folder '$physicalPath' is outside the configured IIS root."
}

$normalizedThumbprint = ''
if ($protocol -eq 'https') {
    if ([string]::IsNullOrWhiteSpace($thumbprint)) {
        throw 'An HTTPS binding requires a certificate thumbprint.'
    }

    $normalizedThumbprint = $thumbprint.Replace(' ', '').Replace(':', '').ToUpperInvariant()
    $certificate = @(Get-ChildItem -Path 'Cert:\LocalMachine\My' | Where-Object { $_.Thumbprint -eq $normalizedThumbprint })
    if ($certificate.Count -eq 0) {
        throw "Certificate '$normalizedThumbprint' was not found in LocalMachine\My with a private key."
    }
}

$warnings = @()
$folderCreated = $false
$appPoolCreated = $false
$siteCreated = $false

if (Test-Path -LiteralPath $physicalPath) {
    if (-not (Test-Path -LiteralPath $physicalPath -PathType Container)) {
        throw "The path '$physicalPath' exists but is not a folder."
    }

    if (-not $useExistingFolder) {
        throw "The folder '$physicalPath' already exists. Choose another folder name or enable 'Use existing folder'."
    }
} else {
    New-Item -ItemType Directory -Path $physicalPath -Force | Out-Null
    $folderCreated = $true
}

if (-not (Test-Path -LiteralPath "IIS:\AppPools\$appPoolName")) {
    New-WebAppPool -Name $appPoolName | Out-Null
    $appPoolCreated = $true
    Set-ItemProperty -Path "IIS:\AppPools\$appPoolName" -Name managedRuntimeVersion -Value $runtimeVersion
    Set-ItemProperty -Path "IIS:\AppPools\$appPoolName" -Name managedPipelineMode -Value $pipelineMode
} else {
    $warnings += "Application pool '$appPoolName' already existed and was reused without changing its settings."
}

try {
    $siteParameters = @{
        Name = $siteName
        PhysicalPath = $physicalPath
        ApplicationPool = $appPoolName
        Port = [uint32]$port
        IPAddress = '*'
    }

    if (-not [string]::IsNullOrWhiteSpace($hostName)) {
        $siteParameters['HostHeader'] = $hostName
    }

    if ($protocol -eq 'https') {
        $siteParameters['Ssl'] = $true
        $siteParameters['SslFlags'] = 0
    }

    New-Website @siteParameters | Out-Null
    $siteCreated = $true
    Stop-Website -Name $siteName -ErrorAction SilentlyContinue

    if ($protocol -eq 'https') {
        $httpsBinding = Get-WebBinding -Name $siteName -Protocol 'https' | Select-Object -First 1
        if ($null -eq $httpsBinding) {
            throw 'The HTTPS binding was not created.'
        }

        $httpsBinding.AddSslCertificate($normalizedThumbprint, 'My')
    }

    $poolItem = Get-Item -Path "IIS:\AppPools\$appPoolName"
    $identityType = [string]$poolItem.processModel.identityType
    $aclIdentity = $null
    if ($identityType -eq 'ApplicationPoolIdentity') {
        $aclIdentity = "IIS AppPool\$appPoolName"
    } elseif ($identityType -eq 'SpecificUser') {
        $aclIdentity = [string]$poolItem.processModel.userName
        if ([string]::IsNullOrWhiteSpace($aclIdentity)) {
            throw "Application pool '$appPoolName' uses a specific user but no username is configured."
        }
    } elseif ($identityType -eq 'NetworkService') {
        $aclIdentity = 'NETWORK SERVICE'
    } elseif ($identityType -eq 'LocalService') {
        $aclIdentity = 'LOCAL SERVICE'
    } elseif ($identityType -eq 'LocalSystem') {
        $aclIdentity = $null
    } else {
        throw "Unsupported application pool identity type '$identityType'. Grant read access to the folder manually."
    }

    if (-not [string]::IsNullOrWhiteSpace($aclIdentity)) {
        $acl = Get-Acl -LiteralPath $physicalPath
        $rights = [System.Security.AccessControl.FileSystemRights]::ReadAndExecute
        $inheritance = [System.Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [System.Security.AccessControl.InheritanceFlags]::ObjectInherit
        $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($aclIdentity, $rights, $inheritance, [System.Security.AccessControl.PropagationFlags]::None, [System.Security.AccessControl.AccessControlType]::Allow)
        $acl.SetAccessRule($rule)
        Set-Acl -LiteralPath $physicalPath -AclObject $acl
    }

    if ($startImmediately) {
        Start-Website -Name $siteName | Out-Null
    }

    $finalState = [string](Get-Website -Name $siteName).State
    $result = [ordered]@{
        siteName = $siteName
        folderPath = $physicalPath
        applicationPool = $appPoolName
        siteState = $finalState
        folderCreated = [bool]$folderCreated
        appPoolCreated = [bool]$appPoolCreated
        warnings = @($warnings)
    }

    Write-WsmSuccess $result
} catch {
    $originalError = $_.Exception.Message
    try {
        if ($siteCreated -and (Get-Website -Name $siteName -ErrorAction SilentlyContinue)) {
            Remove-Website -Name $siteName -ErrorAction SilentlyContinue
        }

        if ($appPoolCreated -and (Test-Path -LiteralPath "IIS:\AppPools\$appPoolName")) {
            Remove-WebAppPool -Name $appPoolName -ErrorAction SilentlyContinue
        }

        if ($folderCreated -and (Test-Path -LiteralPath $physicalPath -PathType Container)) {
            $children = @(Get-ChildItem -LiteralPath $physicalPath -Force -ErrorAction SilentlyContinue)
            if ($children.Count -eq 0) {
                Remove-Item -LiteralPath $physicalPath -Force -ErrorAction SilentlyContinue
            }
        }
    } catch {
        $originalError = "$originalError Cleanup also failed: $($_.Exception.Message)"
    }

    throw $originalError
}
""";

        return Wrap("Create IIS website", body, importIis: true, timeoutSeconds: 300);
    }

    public static RemoteScript StartWebsite(string siteName)
    {
        ValidateSiteName(siteName);
        var body = $$"""
$siteName = {{PowerShellLiterals.Quote(siteName.Trim())}}
$site = Get-Website -Name $siteName -ErrorAction SilentlyContinue
if ($null -eq $site) {
    throw "IIS website '$siteName' was not found."
}

if ([string]$site.State -ne 'Started') {
    Start-Website -Name $siteName | Out-Null
}

$result = [ordered]@{
    siteName = $siteName
    siteState = [string](Get-Website -Name $siteName).State
}

Write-WsmSuccess $result
""";

        return Wrap("Start IIS website", body, importIis: true, timeoutSeconds: 60);
    }

    public static RemoteScript StopWebsite(string siteName)
    {
        ValidateSiteName(siteName);
        var body = $$"""
$siteName = {{PowerShellLiterals.Quote(siteName.Trim())}}
$site = Get-Website -Name $siteName -ErrorAction SilentlyContinue
if ($null -eq $site) {
    throw "IIS website '$siteName' was not found."
}

if ([string]$site.State -ne 'Stopped') {
    Stop-Website -Name $siteName | Out-Null
}

$result = [ordered]@{
    siteName = $siteName
    siteState = [string](Get-Website -Name $siteName).State
}

Write-WsmSuccess $result
""";

        return Wrap("Stop IIS website", body, importIis: true, timeoutSeconds: 60);
    }

    public static RemoteScript RestartWebsite(string siteName)
    {
        ValidateSiteName(siteName);
        var body = $$"""
$siteName = {{PowerShellLiterals.Quote(siteName.Trim())}}
$site = Get-Website -Name $siteName -ErrorAction SilentlyContinue
if ($null -eq $site) {
    throw "IIS website '$siteName' was not found."
}

Stop-Website -Name $siteName -ErrorAction SilentlyContinue
Start-Website -Name $siteName | Out-Null

$result = [ordered]@{
    siteName = $siteName
    siteState = [string](Get-Website -Name $siteName).State
}

Write-WsmSuccess $result
""";

        return Wrap("Restart IIS website", body, importIis: true, timeoutSeconds: 90);
    }

    public static RemoteScript DeleteWebsite(string siteName)
    {
        ValidateSiteName(siteName);
        var body = $$"""
$siteName = {{PowerShellLiterals.Quote(siteName.Trim())}}
$site = Get-Website -Name $siteName -ErrorAction SilentlyContinue
if ($null -eq $site) {
    throw "IIS website '$siteName' was not found."
}

Remove-Website -Name $siteName | Out-Null
$result = [ordered]@{
    siteName = $siteName
}

Write-WsmSuccess $result
""";

        return Wrap("Delete IIS website", body, importIis: true, timeoutSeconds: 90);
    }

    public static RemoteScript DeleteWebsiteFolder(string siteName, string folderPath, string allowedRoot)
    {
        ValidateSiteName(siteName);
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ValidationException("Folder path is required.");
        }

        if (!WebsitePathSafety.IsValidRoot(allowedRoot, out var rootError))
        {
            throw new ValidationException(rootError);
        }

        if (!WebsitePathSafety.IsUnderRoot(folderPath, allowedRoot))
        {
            throw new ValidationException("The folder is outside the configured IIS root and cannot be deleted by this tool.");
        }

        var body = $$"""
$siteName = {{PowerShellLiterals.Quote(siteName.Trim())}}
$expectedPath = {{PowerShellLiterals.Quote(folderPath.Trim())}}
$allowedRoot = {{PowerShellLiterals.Quote(allowedRoot.Trim())}}

$rootFull = [IO.Path]::GetFullPath($allowedRoot).TrimEnd('\')
$fullPath = [IO.Path]::GetFullPath($expectedPath).TrimEnd('\')
$rootPrefix = $rootFull + '\'

if ($rootFull.Length -le 3) {
    throw 'The configured IIS root cannot be the root of a drive.'
}

if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "The folder '$fullPath' is outside the allowed IIS root '$rootFull'."
}

$inUse = @(Get-Website | Where-Object {
    $sitePath = ([string]$_.PhysicalPath).TrimEnd('\')
    $sitePath -ieq $fullPath -or $sitePath.StartsWith($fullPath + '\', [StringComparison]::OrdinalIgnoreCase)
})
if ($inUse.Count -gt 0) {
    throw "The folder is still used by IIS website '$([string]$inUse[0].Name)'."
}

if (-not (Test-Path -LiteralPath $fullPath)) {
    $result = [ordered]@{ folderPath = $fullPath; folderExisted = $false }
    Write-WsmSuccess $result
    exit 0
}

if ((Get-Item -LiteralPath $fullPath).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'The folder is a reparse point. Folder deletion was refused for safety.'
}

$reparsePoints = @(Get-ChildItem -LiteralPath $fullPath -Recurse -Force -ErrorAction Stop | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 })
if ($reparsePoints.Count -gt 0) {
    throw 'The folder contains reparse points (junctions or symbolic links). Folder deletion was refused for safety.'
}

Remove-Item -LiteralPath $fullPath -Recurse -Force -Confirm:$false
$result = [ordered]@{ folderPath = $fullPath; folderExisted = $true }
Write-WsmSuccess $result
""";

        return Wrap("Delete website folder", body, importIis: true, timeoutSeconds: 180);
    }

    public static RemoteScript UpdateBindings(string siteName, IReadOnlyList<WebsiteBinding> bindings)
    {
        ValidateSiteName(siteName);
        var validation = WebsiteValidator.ValidateBindings(bindings);
        if (!validation.IsValid)
        {
            throw new ValidationException(validation.Errors);
        }

        var payload = bindings.Select(binding => new
        {
            protocol = binding.Protocol.Trim().ToLowerInvariant(),
            port = binding.Port,
            hostname = binding.Hostname.Trim(),
            certificateThumbprint = WebsiteValidator.NormalizeThumbprint(binding.CertificateThumbprint)
        });

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var encoded = PowerShellLiterals.Base64Utf8(json);

        var body = $$"""
$siteName = {{PowerShellLiterals.Quote(siteName.Trim())}}
$desiredBase64 = {{PowerShellLiterals.Quote(encoded)}}
$site = Get-Website -Name $siteName -ErrorAction SilentlyContinue
if ($null -eq $site) {
    throw "IIS website '$siteName' was not found."
}

$desiredJson = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($desiredBase64))
$rawDesired = @($desiredJson | ConvertFrom-Json)
if ($rawDesired.Count -eq 0) {
    throw 'At least one binding is required.'
}

$desired = @()
$seen = @{}
foreach ($item in $rawDesired) {
    $protocol = ([string]$item.protocol).Trim().ToLowerInvariant()
    if ($protocol -ne 'http' -and $protocol -ne 'https') {
        throw "Binding protocol '$protocol' is not supported."
    }

    $port = 0
    if (-not [int]::TryParse([string]$item.port, [ref]$port) -or $port -lt 1 -or $port -gt 65535) {
        throw "Binding port '$($item.port)' is not valid."
    }

    $hostname = ([string]$item.hostname).Trim()
    if ($hostname.Contains(' ') -or $hostname.Contains('/') -or $hostname.Contains('\') -or $hostname.Contains(':')) {
        throw "Binding hostname '$hostname' is not valid."
    }

    $thumbprint = ([string]$item.certificateThumbprint).Replace(' ', '').Replace(':', '').ToUpperInvariant()
    if ($protocol -eq 'https') {
        if ($thumbprint.Length -ne 40) {
            throw "HTTPS binding on port $port needs a 40-character certificate thumbprint."
        }

        $certificate = @(Get-ChildItem -Path 'Cert:\LocalMachine\My' | Where-Object { $_.Thumbprint -eq $thumbprint })
        if ($certificate.Count -eq 0) {
            throw "Certificate '$thumbprint' was not found in LocalMachine\My."
        }
    } else {
        $thumbprint = ''
    }

    $key = "$protocol|$port|$($hostname.ToLowerInvariant())"
    if ($seen.ContainsKey($key)) {
        throw "Duplicate binding: $protocol port $port host '$hostname'."
    }

    $seen[$key] = $true
    $desired += [pscustomobject]@{
        protocol = $protocol
        port = [int]$port
        hostname = $hostname
        certificateThumbprint = $thumbprint
        bindingInformation = "*:$port`:$hostname"
    }
}

foreach ($existingBinding in @(Get-WebBinding -ErrorAction SilentlyContinue)) {
    if (([string]$existingBinding.Name) -eq $siteName) {
        continue
    }

    $existingParts = Get-WsmBindingParts ([string]$existingBinding.bindingInformation)
    if ($null -eq $existingParts) {
        continue
    }

    $existingProtocol = ([string]$existingBinding.protocol).ToLowerInvariant()
    $existingHost = ([string]$existingParts.Hostname).ToLowerInvariant()
    foreach ($item in $desired) {
        if ($existingProtocol -eq $item.protocol -and
            [int]$existingParts.Port -eq [int]$item.port -and
            $existingHost -eq $item.hostname.ToLowerInvariant()) {
            throw "The binding $($item.protocol) port $($item.port) host '$($item.hostname)' is already used by website '$([string]$existingBinding.Name)'."
        }
    }
}

$snapshot = @()
foreach ($binding in @(Get-WebBinding -Name $siteName)) {
    $parts = Get-WsmBindingParts ([string]$binding.bindingInformation)
    if ($null -eq $parts) {
        continue
    }

    $snapshot += [pscustomobject]@{
        protocol = ([string]$binding.protocol).ToLowerInvariant()
        port = [int]$parts.Port
        hostname = [string]$parts.Hostname
        certificateThumbprint = (Get-WsmThumbprint $binding.certificateHash)
        original = $binding
    }
}

try {
    foreach ($current in $snapshot) {
        $match = @($desired | Where-Object {
            $_.protocol -eq $current.protocol -and
            [int]$_.port -eq [int]$current.port -and
            $_.hostname.ToLowerInvariant() -eq $current.hostname.ToLowerInvariant() -and
            ($_.protocol -ne 'https' -or $_.certificateThumbprint -eq $current.certificateThumbprint)
        })

        if ($match.Count -eq 0) {
            Remove-WebBinding -InputObject $current.original
        }
    }

    foreach ($item in $desired) {
        $match = @($snapshot | Where-Object {
            $_.protocol -eq $item.protocol -and
            [int]$_.port -eq [int]$item.port -and
            $_.hostname.ToLowerInvariant() -eq $item.hostname.ToLowerInvariant() -and
            ($item.protocol -ne 'https' -or $_.certificateThumbprint -eq $item.certificateThumbprint)
        })

        if ($match.Count -eq 0) {
            $parameters = @{
                Name = $siteName
                Protocol = $item.protocol
                Port = [uint32]$item.port
                IPAddress = '*'
                HostHeader = $item.hostname
                Force = $true
            }

            if ($item.protocol -eq 'https') {
                $parameters['SslFlags'] = 0
            }

            New-WebBinding @parameters | Out-Null

            if ($item.protocol -eq 'https') {
                $newBinding = Get-WebBinding -Name $siteName -Protocol 'https' -Port $item.port |
                    Where-Object { ([string]$_.bindingInformation).EndsWith(":$($item.hostname)") } |
                    Select-Object -First 1
                if ($null -eq $newBinding) {
                    throw "The HTTPS binding for port $($item.port) was not created."
                }

                $newBinding.AddSslCertificate($item.certificateThumbprint, 'My')
            }
        }
    }
} catch {
    $failure = $_.Exception.Message
    try {
        foreach ($binding in @(Get-WebBinding -Name $siteName)) {
            Remove-WebBinding -InputObject $binding
        }

        foreach ($item in $snapshot) {
            $parameters = @{
                Name = $siteName
                Protocol = $item.protocol
                Port = [uint32]$item.port
                IPAddress = '*'
                HostHeader = $item.hostname
                Force = $true
            }

            if ($item.protocol -eq 'https') {
                $parameters['SslFlags'] = 0
            }

            New-WebBinding @parameters | Out-Null
            if ($item.protocol -eq 'https' -and -not [string]::IsNullOrWhiteSpace($item.certificateThumbprint)) {
                $restored = Get-WebBinding -Name $siteName -Protocol 'https' -Port $item.port |
                    Where-Object { ([string]$_.bindingInformation).EndsWith(":$($item.hostname)") } |
                    Select-Object -First 1
                if ($null -ne $restored) {
                    $restored.AddSslCertificate($item.certificateThumbprint, 'My')
                }
            }
        }
    } catch {
        $failure = "$failure Restoring the original bindings also failed: $($_.Exception.Message)"
    }

    throw $failure
}

$result = [ordered]@{
    siteName = $siteName
    bindings = @($desired | ForEach-Object {
        [ordered]@{
            protocol = $_.protocol
            ipAddress = '*'
            port = $_.port
            hostname = $_.hostname
            certificateThumbprint = $_.certificateThumbprint
            bindingInformation = $_.bindingInformation
        }
    })
}

Write-WsmSuccess $result
""";

        return Wrap("Update IIS bindings", body, importIis: true, timeoutSeconds: 180);
    }

    private static RemoteScript Wrap(string name, string body, bool importIis, int timeoutSeconds)
    {
        var script = Header + "try {\n";
        if (importIis)
        {
            script += "    Import-Module WebAdministration -ErrorAction Stop\n";
        }

        script += body + "\n} catch {\n    Write-WsmFailure $_.Exception.Message\n    exit 1\n}\n";
        return new RemoteScript(name, script, timeoutSeconds, RequiresElevation: importIis);
    }

    private static void ValidateSiteName(string siteName)
    {
        var result = new ValidationResult();
        WebsiteValidator.ValidateSiteName(siteName, result);
        if (!result.IsValid)
        {
            throw new ValidationException(result.Errors);
        }
    }
}

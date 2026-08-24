[CmdletBinding()]
param(
    [string]$WorkspaceRoot
)

Microsoft.PowerShell.Core\Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-LocalInspectionPath {
    param(
        [Parameter(Mandatory)]
        [string]$Path,

        [Parameter(Mandatory)]
        [bool]$MustExist,

        [Parameter(Mandatory)]
        [bool]$MustBeDirectory
    )

    if ($Path.StartsWith('\\', [StringComparison]::Ordinal)) {
        throw 'Network and device paths are not accepted for local inspection.'
    }

    $suppliedRoot = [IO.Path]::GetPathRoot($Path)
    if (-not [IO.Path]::IsPathRooted($Path) -or
        [string]::IsNullOrWhiteSpace($suppliedRoot) -or
        $suppliedRoot -notmatch '^[A-Za-z]:[\\/]$') {
        throw 'Inspection paths must be fully qualified.'
    }

    $resolved = [IO.Path]::GetFullPath($Path)
    if ($resolved.StartsWith('\\', [StringComparison]::Ordinal)) {
        throw 'Network and device paths are not accepted for local inspection.'
    }

    $pathRoot = [IO.Path]::GetPathRoot($resolved)
    if ([string]::IsNullOrWhiteSpace($pathRoot)) {
        throw 'The inspection path has no local volume root.'
    }

    $drive = [IO.DriveInfo]::new($pathRoot)
    if ($drive.DriveType -ne [IO.DriveType]::Fixed) {
        throw 'Inspection paths must be on a fixed local volume.'
    }

    $cursor = $resolved
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Microsoft.PowerShell.Management\Test-Path -LiteralPath $cursor) {
            $item = Microsoft.PowerShell.Management\Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Reparse points are not accepted in inspection paths.'
            }
        }

        $trimCharacters = [char[]]@(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar)
        if ([string]::Equals(
                $cursor.TrimEnd($trimCharacters),
                $pathRoot.TrimEnd($trimCharacters),
                [StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $cursor = Microsoft.PowerShell.Management\Split-Path -Parent $cursor
    }

    if ($MustExist) {
        $expectedType = if ($MustBeDirectory) { 'Container' } else { 'Leaf' }
        if (-not (Microsoft.PowerShell.Management\Test-Path -LiteralPath $resolved -PathType $expectedType)) {
            throw 'The requested inspection path does not exist with the expected type.'
        }
    }

    return $resolved
}

function Get-SignatureEvidence {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Microsoft.PowerShell.Management\Test-Path -LiteralPath $Path -PathType Leaf)) {
        return [ordered]@{
            Exists = $false
            SignatureStatus = 'missing'
            SignerSubject = $null
            Sha256 = $null
            FileVersion = $null
            ProductVersion = $null
            Length = $null
            ReparsePoint = $false
            StableRead = $false
        }
    }

    $resolvedPath = Resolve-LocalInspectionPath $Path $true $false
    $stream = [IO.FileStream]::new(
        $resolvedPath,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        [IO.FileShare]::Read,
        4096,
        [IO.FileOptions]::SequentialScan)
    $hashAlgorithm = [Security.Cryptography.SHA256]::Create()
    $beforeDigest = $null
    $afterDigest = $null
    try {
        $length = $stream.Length
        $beforeDigest = $hashAlgorithm.ComputeHash($stream)
        $signature = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $resolvedPath
        $versionInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolvedPath)
        $attributes = [IO.File]::GetAttributes($resolvedPath)
        $stream.Position = 0
        $afterDigest = $hashAlgorithm.ComputeHash($stream)
        $beforeHex = ([BitConverter]::ToString($beforeDigest)).Replace('-', '').ToLowerInvariant()
        $afterHex = ([BitConverter]::ToString($afterDigest)).Replace('-', '').ToLowerInvariant()
        $stableRead = $stream.Length -eq $length -and $beforeHex -ceq $afterHex
    } finally {
        if ($null -ne $beforeDigest) {
            [Array]::Clear($beforeDigest, 0, $beforeDigest.Length)
        }
        if ($null -ne $afterDigest) {
            [Array]::Clear($afterDigest, 0, $afterDigest.Length)
        }
        $hashAlgorithm.Dispose()
        $stream.Dispose()
    }
    return [ordered]@{
        Exists = $true
        SignatureStatus = $signature.Status.ToString()
        SignerSubject = if ($null -eq $signature.SignerCertificate) {
            $null
        } else {
            $signature.SignerCertificate.Subject
        }
        Sha256 = if ($stableRead) { $beforeHex } else { $null }
        FileVersion = if ($stableRead) { $versionInfo.FileVersion } else { $null }
        ProductVersion = if ($stableRead) { $versionInfo.ProductVersion } else { $null }
        Length = [long]$length
        ReparsePoint = ($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0
        StableRead = $stableRead
    }
}

function Get-CommandEvidence {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    $command = Microsoft.PowerShell.Core\Get-Command $Name -CommandType Application `
        -ErrorAction SilentlyContinue | Microsoft.PowerShell.Utility\Select-Object -First 1
    return [ordered]@{
        Name = $Name
        Available = $null -ne $command
        CommandType = if ($null -eq $command) { $null } else { $command.CommandType.ToString() }
    }
}

function Get-OptionalFeatureEvidence {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    try {
        $feature = CimCmdlets\Get-CimInstance Win32_OptionalFeature -Filter "Name='$Name'" -ErrorAction Stop |
            Microsoft.PowerShell.Utility\Select-Object -First 1
        return [ordered]@{
            Name = $Name
            QuerySucceeded = $true
            Found = $null -ne $feature
            InstallState = if ($null -eq $feature) { $null } else { [int]$feature.InstallState }
            Enabled = $null -ne $feature -and [int]$feature.InstallState -eq 1
        }
    } catch {
        return [ordered]@{
            Name = $Name
            QuerySucceeded = $false
            Found = $false
            InstallState = $null
            Enabled = $false
        }
    }
}

function Get-ServerFeatureEvidence {
    param(
        [Parameter(Mandatory)]
        [string]$Name
    )

    if ($null -eq (Microsoft.PowerShell.Core\Get-Command ServerManager\Get-WindowsFeature `
            -CommandType Function, Cmdlet -ErrorAction SilentlyContinue)) {
        return [ordered]@{
            Name = $Name
            QuerySucceeded = $false
            Found = $false
            Installed = $false
        }
    }

    try {
        $feature = ServerManager\Get-WindowsFeature -Name $Name -ErrorAction Stop
        return [ordered]@{
            Name = $Name
            QuerySucceeded = $true
            Found = $null -ne $feature
            Installed = $null -ne $feature -and [bool]$feature.Installed
        }
    } catch {
        return [ordered]@{
            Name = $Name
            QuerySucceeded = $false
            Found = $false
            Installed = $false
        }
    }
}

function Get-WslEvidence {
    $windowsRoot = [Environment]::GetFolderPath([Environment+SpecialFolder]::Windows)
    $systemWsl = Microsoft.PowerShell.Management\Join-Path $windowsRoot 'System32\wsl.exe'
    $lxssPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Lxss'
    try {
        $distributionCount = if (Microsoft.PowerShell.Management\Test-Path -LiteralPath $lxssPath) {
            @(Microsoft.PowerShell.Management\Get-ChildItem -LiteralPath $lxssPath -ErrorAction Stop).Count
        } else {
            0
        }

        return [ordered]@{
            CommandAvailable = Microsoft.PowerShell.Management\Test-Path -LiteralPath $systemWsl -PathType Leaf
            QuerySucceeded = $true
            InspectionMethod = 'registry-only-no-wsl-execution'
            DistributionCount = $distributionCount
        }
    } catch {
        return [ordered]@{
            CommandAvailable = Microsoft.PowerShell.Management\Test-Path -LiteralPath $systemWsl -PathType Leaf
            QuerySucceeded = $false
            InspectionMethod = 'registry-only-no-wsl-execution'
            DistributionCount = 0
        }
    }
}

if ([string]::IsNullOrWhiteSpace($WorkspaceRoot)) {
    $scriptDirectory = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $PSScriptRoot
    } else {
        Microsoft.PowerShell.Management\Split-Path -Parent $MyInvocation.MyCommand.Path
    }

    $WorkspaceRoot = Microsoft.PowerShell.Management\Split-Path -Parent $scriptDirectory
}

$resolvedWorkspace = Resolve-LocalInspectionPath $WorkspaceRoot $true $true
$probeScript = Get-SignatureEvidence $MyInvocation.MyCommand.Path
if (-not $probeScript.StableRead -or [string]::IsNullOrWhiteSpace($probeScript.Sha256)) {
    throw 'The probe script could not be bound to one stable read.'
}

$vendorRoot = Microsoft.PowerShell.Management\Join-Path $resolvedWorkspace 'mt5-net-api-full-binaries-main'
if (Microsoft.PowerShell.Management\Test-Path -LiteralPath $vendorRoot -PathType Container) {
    $vendorRoot = Resolve-LocalInspectionPath $vendorRoot $true $true
}

$vendorDll = Microsoft.PowerShell.Management\Join-Path $vendorRoot 'mt5api.dll'
$vendorExample = Microsoft.PowerShell.Management\Join-Path $vendorRoot 'Examples.cs'
$licenseNames = @()
if (Microsoft.PowerShell.Management\Test-Path -LiteralPath $vendorRoot -PathType Container) {
    $licenseNames = @(Microsoft.PowerShell.Management\Get-ChildItem -LiteralPath $vendorRoot -File |
        Microsoft.PowerShell.Core\Where-Object { $_.Name -match '^(LICENSE|LICENCE|COPYING|NOTICE|README)(\.|$)' } |
        Microsoft.PowerShell.Utility\Select-Object -ExpandProperty Name |
        Microsoft.PowerShell.Utility\Sort-Object)
}

$vendorExamplePresent = Microsoft.PowerShell.Management\Test-Path -LiteralPath $vendorExample -PathType Leaf
$credentialLikeExampleLines = 0
$credentialConstructorTupleCount = 0
$orderSendReferences = 0
if ($vendorExamplePresent) {
    $vendorExample = Resolve-LocalInspectionPath $vendorExample $true $false
    $credentialLikeExampleLines = @(Microsoft.PowerShell.Utility\Select-String -LiteralPath $vendorExample `
        -Pattern '(?i)(password|passwd|secret|token)\s*[:=]' -AllMatches).Count
    $credentialTupleMatches = @(Microsoft.PowerShell.Utility\Select-String -LiteralPath $vendorExample `
        -Pattern '(?i)new\s+MT5API\s*\(\s*\d+\s*,\s*"[^"\r\n]+"\s*,\s*"[^"\r\n]+"' -AllMatches)
    $credentialConstructorTupleCount = [int](($credentialTupleMatches |
        Microsoft.PowerShell.Core\ForEach-Object { $_.Matches.Count } |
        Microsoft.PowerShell.Utility\Measure-Object -Sum).Sum)
    $orderSendMatches = @(Microsoft.PowerShell.Utility\Select-String -LiteralPath $vendorExample `
        -Pattern '(?i)OrderSend' -AllMatches)
    $orderSendReferences = [int](($orderSendMatches |
        Microsoft.PowerShell.Core\ForEach-Object { $_.Matches.Count } |
        Microsoft.PowerShell.Utility\Measure-Object -Sum).Sum)
}

$installedBinaries = [ordered]@{
    MetaEditor64 = Get-SignatureEvidence 'C:\Program Files\MetaTrader 5\MetaEditor64.exe'
    Terminal64 = Get-SignatureEvidence 'C:\Program Files\MetaTrader 5\terminal64.exe'
    MetaTester64 = Get-SignatureEvidence 'C:\Program Files\MetaTrader 5\metatester64.exe'
}

$processCount = @(Microsoft.PowerShell.Management\Get-Process -Name metaeditor64, terminal64, metatester64 `
    -ErrorAction SilentlyContinue).Count

$computerSystem = CimCmdlets\Get-CimInstance Win32_ComputerSystem
$operatingSystem = CimCmdlets\Get-CimInstance Win32_OperatingSystem
$processors = @(CimCmdlets\Get-CimInstance Win32_Processor)
$firmwareVirtualization = $processors.Count -gt 0 -and
    -not ($processors | Microsoft.PowerShell.Core\Where-Object { -not [bool]$_.VirtualizationFirmwareEnabled })
$secondLevelAddressTranslation = $processors.Count -gt 0 -and
    -not ($processors | Microsoft.PowerShell.Core\Where-Object { -not [bool]$_.SecondLevelAddressTranslationExtensions })

$deviceGuard = $null
$deviceGuardQuerySucceeded = $false
try {
    $deviceGuard = CimCmdlets\Get-CimInstance -Namespace 'root\Microsoft\Windows\DeviceGuard' `
        -ClassName Win32_DeviceGuard -ErrorAction Stop
    $deviceGuardQuerySucceeded = $true
} catch {
    $deviceGuard = $null
}

$appLockerCollectionCount = 0
$appLockerQuerySucceeded = $false
if ($null -ne (Microsoft.PowerShell.Core\Get-Command AppLocker\Get-AppLockerPolicy `
        -CommandType Function, Cmdlet -ErrorAction SilentlyContinue)) {
    try {
        $policy = AppLocker\Get-AppLockerPolicy -Effective -ErrorAction Stop
        $appLockerCollectionCount = @($policy.RuleCollections |
            Microsoft.PowerShell.Core\Where-Object {
                $_.Rules.Count -gt 0 -and
                $_.EnforcementMode.ToString() -in @('Enabled', 'Enforced')
            }).Count
        $appLockerQuerySucceeded = $true
    } catch {
        $appLockerCollectionCount = 0
    }
}

$firewallProfiles = @()
$firewallQuerySucceeded = $false
if ($null -ne (Microsoft.PowerShell.Core\Get-Command NetSecurity\Get-NetFirewallProfile `
        -CommandType Function, Cmdlet -ErrorAction SilentlyContinue)) {
    try {
        $firewallProfiles = @(NetSecurity\Get-NetFirewallProfile -ErrorAction Stop |
            Microsoft.PowerShell.Core\ForEach-Object {
                [ordered]@{
                    Name = $_.Name
                    Enabled = [bool]$_.Enabled
                    DefaultOutboundAction = $_.DefaultOutboundAction.ToString()
                }
            })
        $firewallQuerySucceeded = $true
    } catch {
        $firewallProfiles = @()
    }
}

$defender = [ordered]@{
    Available = $false
    QuerySucceeded = $false
    AntivirusEnabled = $false
    RealTimeProtectionEnabled = $false
}
if ($null -ne (Microsoft.PowerShell.Core\Get-Command Defender\Get-MpComputerStatus `
        -CommandType Function, Cmdlet -ErrorAction SilentlyContinue)) {
    try {
        $status = Defender\Get-MpComputerStatus -ErrorAction Stop
        $defender = [ordered]@{
            Available = $true
            QuerySucceeded = $true
            AntivirusEnabled = [bool]$status.AntivirusEnabled
            RealTimeProtectionEnabled = [bool]$status.RealTimeProtectionEnabled
        }
    } catch {
        $defender = [ordered]@{
            Available = $false
            QuerySucceeded = $false
            AntivirusEnabled = $false
            RealTimeProtectionEnabled = $false
        }
    }
}

$sandboxPath = Microsoft.PowerShell.Management\Join-Path $env:SystemRoot 'System32\WindowsSandbox.exe'
$toolCommands = @('docker', 'podman', 'VBoxManage', 'vmrun') |
    Microsoft.PowerShell.Core\ForEach-Object { Get-CommandEvidence $_ }
$readyContainerCommandCount = @($toolCommands |
    Microsoft.PowerShell.Core\Where-Object { $_.Available }).Count
$optionalFeatures = @(
    'Containers',
    'HypervisorPlatform',
    'VirtualMachinePlatform',
    'Microsoft-Windows-Subsystem-Linux'
) | Microsoft.PowerShell.Core\ForEach-Object { Get-OptionalFeatureEvidence $_ }
$serverFeatures = @('Hyper-V', 'Containers') |
    Microsoft.PowerShell.Core\ForEach-Object { Get-ServerFeatureEvidence $_ }
$wsl = Get-WslEvidence

$evidence = [ordered]@{
    SchemaVersion = 'yo4x.mt5-toolchain-isolation-evidence.v4'
    EvidenceAuthority = 'unsigned-local-observation'
    CryptographicallyAttested = $false
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString(
        'yyyy-MM-ddTHH:mm:ss.fffZ',
        [Globalization.CultureInfo]::InvariantCulture)
    InspectionMode = 'read-only-host-query-no-vendor-code-execution'
    Probe = [ordered]@{
        ScriptSha256 = $probeScript.Sha256
        StableRead = [bool]$probeScript.StableRead
        Binding = 'consistent-file-bytes-two-pass-sha256-under-nonwrite-nondelete-share'
    }
    Host = [ordered]@{
        OperatingSystem = $operatingSystem.Caption
        Version = $operatingSystem.Version
        BuildNumber = $operatingSystem.BuildNumber
        Architecture = $operatingSystem.OSArchitecture
        Manufacturer = $computerSystem.Manufacturer
        Model = $computerSystem.Model
    }
    VendorBundle = [ordered]@{
        Dll = Get-SignatureEvidence $vendorDll
        TopLevelLicenseOrNoticeFileCount = $licenseNames.Count
        TopLevelLicenseOrNoticeFileNames = $licenseNames
        ExampleSourcePresent = [bool]$vendorExamplePresent
        ExampleCredentialLikeLineCount = $credentialLikeExampleLines
        ExampleCredentialConstructorTupleCount = $credentialConstructorTupleCount
        ExampleOrderSendReferenceCount = $orderSendReferences
        ExampleValuesRendered = $false
    }
    InstalledMetaTrader = [ordered]@{
        Binaries = $installedBinaries
        RelatedProcessCount = $processCount
        ExecutablesLaunchedByProbe = $false
    }
    Isolation = [ordered]@{
        ServerFeatures = $serverFeatures
        OptionalFeatures = $optionalFeatures
        HypervisorPresent = [bool]$computerSystem.HypervisorPresent
        VirtualizationFirmwareEnabled = $firmwareVirtualization
        SecondLevelAddressTranslation = $secondLevelAddressTranslation
        WindowsSandboxExecutablePresent = Microsoft.PowerShell.Management\Test-Path `
            -LiteralPath $sandboxPath -PathType Leaf
        ToolCommands = $toolCommands
        Wsl = $wsl
        DeviceGuardAvailable = $null -ne $deviceGuard
        DeviceGuardQuerySucceeded = $deviceGuardQuerySucceeded
        VirtualizationBasedSecurityStatus = if ($null -eq $deviceGuard) {
            $null
        } else {
            [int]$deviceGuard.VirtualizationBasedSecurityStatus
        }
        AppLocker = [ordered]@{
            QuerySucceeded = $appLockerQuerySucceeded
            EnforcedCollectionCount = $appLockerCollectionCount
        }
        FirewallQuerySucceeded = $firewallQuerySucceeded
        FirewallProfiles = $firewallProfiles
        Defender = $defender
        DiscoveredThirdPartyContainerOrVmCommandCount = $readyContainerCommandCount
    }
    Verdict = [ordered]@{
        IsolatedRunnerConfigured = $false
        SafeToCompileUntrustedMqlOnHost = $false
        SafeToExecuteSuppliedMqlOnHost = $false
        Code = 'isolated_runner_not_configured'
    }
}

$canonicalEvidence = $evidence |
    Microsoft.PowerShell.Utility\ConvertTo-Json -Depth 12 -Compress
$canonicalBytes = [Text.Encoding]::UTF8.GetBytes($canonicalEvidence)
$contentHashAlgorithm = [Security.Cryptography.SHA256]::Create()
$contentDigest = $null
try {
    $contentDigest = $contentHashAlgorithm.ComputeHash($canonicalBytes)
    $evidence['EvidenceContentSha256'] = ([BitConverter]::ToString($contentDigest)).Replace(
        '-', '').ToLowerInvariant()
} finally {
    if ($null -ne $contentDigest) {
        [Array]::Clear($contentDigest, 0, $contentDigest.Length)
    }
    [Array]::Clear($canonicalBytes, 0, $canonicalBytes.Length)
    $contentHashAlgorithm.Dispose()
}

$evidence | Microsoft.PowerShell.Utility\ConvertTo-Json -Depth 12
$global:LASTEXITCODE = 0

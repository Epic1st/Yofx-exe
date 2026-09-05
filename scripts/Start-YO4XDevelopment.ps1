[CmdletBinding()]
param(
    [switch] $SkipBuild,
    [switch] $NoDesktop
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$developmentRoot = Join-Path $workspaceRoot ".local\development"
$statePath = Join-Path $developmentRoot "processes.json"
$secretsPath = Join-Path $developmentRoot "secrets.clixml"
$logsRoot = Join-Path $developmentRoot "logs"
$certificateRoot = Join-Path $developmentRoot "certificates"
$postgresData = Join-Path $developmentRoot "postgres-data"
$postgresPort = 55432
$dotnet = Join-Path $workspaceRoot ".tools\dotnet-sdk-10.0.400\dotnet.exe"
$node = Join-Path $workspaceRoot ".tools\node-v24.19.0-win-x64\node.exe"
$npm = Join-Path $workspaceRoot ".tools\node-v24.19.0-win-x64\npm.cmd"
$postgresBin = Join-Path $workspaceRoot ".tools\postgresql-local\package-native-18.6-1\pgsql\bin"
$postgres = Join-Path $postgresBin "postgres.exe"
$pgCtl = Join-Path $postgresBin "pg_ctl.exe"
$initdb = Join-Path $postgresBin "initdb.exe"
$bootstrapProject = Join-Path $workspaceRoot "src\Tools\YO4X.DevelopmentBootstrap\YO4X.DevelopmentBootstrap.csproj"
$bootstrapDll = Join-Path $workspaceRoot "src\Tools\YO4X.DevelopmentBootstrap\bin\Release\net10.0\YO4X.DevelopmentBootstrap.dll"
$roleScript = Join-Path $workspaceRoot "src\BuildingBlocks\YO4X.Persistence.Postgres\Security\least_privilege_roles.sql"
$identityProject = Join-Path $workspaceRoot "src\Apps\YO4X.DevelopmentIdentity\YO4X.DevelopmentIdentity.csproj"
$identityDll = Join-Path $workspaceRoot "src\Apps\YO4X.DevelopmentIdentity\bin\Release\net10.0\YO4X.DevelopmentIdentity.dll"
$apiProject = Join-Path $workspaceRoot "src\Apps\YO4X.ControlPlane.Api\YO4X.ControlPlane.Api.csproj"
$apiDll = Join-Path $workspaceRoot "src\Apps\YO4X.ControlPlane.Api\bin\Release\net10.0-windows10.0.19041.0\YO4X.ControlPlane.Api.dll"
$mt5CanaryProject = Join-Path $workspaceRoot "src\Tools\YO4X.Mt5.DemoCanary\YO4X.Mt5.DemoCanary.csproj"
$mt5CanaryExe = Join-Path $workspaceRoot "src\Tools\YO4X.Mt5.DemoCanary\bin\Release\net10.0-windows10.0.19041.0\YO4X.Mt5.DemoCanary.exe"
$mt5WorkerProject = Join-Path $workspaceRoot "src\Runtime\YO4X.Mt5.ConnectionProbe.WorkerHost.Windows\YO4X.Mt5.ConnectionProbe.WorkerHost.Windows.csproj"
$mt5WorkerRoot = Join-Path $workspaceRoot "src\Runtime\YO4X.Mt5.ConnectionProbe.WorkerHost.Windows\bin\Release\net10.0-windows10.0.19041.0"
$mt5WorkerExe = Join-Path $mt5WorkerRoot "YO4X.Mt5.ConnectionProbe.WorkerHost.Windows.exe"
$mt5WorkerManifest = Join-Path $mt5WorkerRoot "broker-worker.launch.v1.json"
$mt5ManifestGenerator = Join-Path $workspaceRoot "scripts\New-BrokerWorkerLaunchManifest.ps1"
$mt5Artifact = Join-Path $workspaceRoot "mt5-net-api-full-binaries-main\mt5api.dll"
$mt5VaultRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) "YO4X\credentials"
$credentialWriterProject = Join-Path $workspaceRoot "src\Tools\YO4X.LocalCredentialWriter\YO4X.LocalCredentialWriter.csproj"
$credentialWriterExe = Join-Path $workspaceRoot "src\Tools\YO4X.LocalCredentialWriter\bin\Release\net10.0-windows10.0.19041.0\YO4X.LocalCredentialWriter.exe"
$frontendRoot = Join-Path $workspaceRoot "src\Frontend\YO4X.Web"
$vite = Join-Path $frontendRoot "node_modules\vite\bin\vite.js"
$desktopProject = Join-Path $workspaceRoot "src\Apps\YO4X.Desktop\YO4X.Desktop.csproj"
$desktopExe = Join-Path $developmentRoot "desktop\YO4X.exe"
$identityPfx = Join-Path $certificateRoot "loopback-https.pfx"
$postgresCertificate = Join-Path $certificateRoot "postgres-server.crt"
$postgresPrivateKey = Join-Path $certificateRoot "postgres-server.key"
$postgresLog = Join-Path $logsRoot "postgres.log"

$runtimeRoles = @(
    "yo4x_context_issuer", "yo4x_local_identity", "yo4x_control_api",
    "yo4x_admin_bff", "yo4x_emergency", "yo4x_secret_ingestion",
    "yo4x_conversion_worker", "yo4x_strategy_verifier",
    "yo4x_runtime_evidence", "yo4x_worker", "yo4x_supervisor_runtime",
    "yo4x_trade_authorizer", "yo4x_gateway_runtime", "yo4x_credential_runtime")

function Assert-WorkspaceChild {
    param([Parameter(Mandatory=$true)][string] $Path)
    $root = $workspaceRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to use a development path outside the workspace."
    }
}

function New-SecretText {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
        $generator.Dispose()
    }
}

function New-ProofKeyText {
    $bytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
        return [Convert]::ToBase64String($bytes)
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
        $generator.Dispose()
    }
}

function ConvertTo-PlainText {
    param([Parameter(Mandatory=$true)][Security.SecureString] $Value)
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function Test-PortAvailable {
    param([Parameter(Mandatory=$true)][int] $Port)
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $listener.Start(); return $true }
    catch [Net.Sockets.SocketException] { return $false }
    finally { try { $listener.Stop() } catch {} }
}

function Wait-Endpoint {
    param(
        [Parameter(Mandatory=$true)][string] $Uri,
        [Parameter(Mandatory=$true)][Diagnostics.Process] $Process,
        [int] $Seconds = 45
    )
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($Seconds)
    $parsed = [Uri]$Uri
    if (-not $parsed.IsLoopback) { throw "Readiness checks are restricted to loopback endpoints." }
    $curl = Get-Command curl.exe -ErrorAction Stop
    do {
        if ($Process.HasExited) { throw "A YO4X service exited before '$Uri' became ready." }
        $curlArguments = @('--silent', '--output', 'NUL',
            '--write-out', '%{http_code}', '--max-time', '2')
        if ($parsed.Scheme -eq 'https') { $curlArguments += '--insecure' }
        $status = (& $curl.Source @curlArguments $Uri | Out-String).Trim()
        if ($status -match '^[234][0-9]{2}$') { return }
        Start-Sleep -Milliseconds 250
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    throw "Timed out waiting for '$Uri'."
}

function Start-LoggedProcess {
    param(
        [Parameter(Mandatory=$true)][string] $Name,
        [Parameter(Mandatory=$true)][string] $FilePath,
        [Parameter(Mandatory=$true)][string[]] $Arguments,
        [Parameter(Mandatory=$true)][hashtable] $Environment,
        [Parameter(Mandatory=$true)][string] $WorkingDirectory
    )
    $original = @{}
    try {
        foreach ($entry in $Environment.GetEnumerator()) {
            $original[$entry.Key] = [Environment]::GetEnvironmentVariable($entry.Key, "Process")
            [Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, "Process")
        }
        return Start-Process -FilePath $FilePath -ArgumentList $Arguments `
            -WorkingDirectory $WorkingDirectory -WindowStyle Hidden -PassThru `
            -RedirectStandardOutput (Join-Path $logsRoot "$Name.stdout.log") `
            -RedirectStandardError (Join-Path $logsRoot "$Name.stderr.log")
    }
    finally {
        foreach ($entry in $original.GetEnumerator()) {
            [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, "Process")
        }
    }
}

function Save-DevelopmentState {
    param([Parameter(Mandatory=$true)][object[]] $Processes)
    [pscustomobject]@{
        schemaVersion = 1
        workspace = $workspaceRoot
        postgresData = $postgresData
        postgresPort = $postgresPort
        certificateSha256 = $identityCertificateSha256
        startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        processes = $Processes
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding UTF8
}

foreach ($path in @($developmentRoot, $statePath, $secretsPath, $logsRoot,
    $certificateRoot, $postgresData, $identityPfx, $postgresCertificate,
    $postgresPrivateKey, $desktopExe)) { Assert-WorkspaceChild $path }
foreach ($required in @($dotnet, $node, $npm, $postgres, $pgCtl, $initdb,
    $bootstrapProject, $roleScript, $identityProject, $apiProject, $desktopProject,
    $mt5CanaryProject, $mt5WorkerProject, $mt5ManifestGenerator, $mt5Artifact)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required pinned development input is missing: '$required'."
    }
}

New-Item -ItemType Directory -Force -Path $developmentRoot, $logsRoot, $certificateRoot | Out-Null
if (Test-Path -LiteralPath $statePath) {
    $existingState = Get-Content -Raw -LiteralPath $statePath | ConvertFrom-Json
    $ownedAlive = @($existingState.processes | Where-Object {
        Get-Process -Id $_.pid -ErrorAction SilentlyContinue
    })
    if ($ownedAlive.Count -gt 0) {
        throw "A YO4X development stack recorded in '$statePath' is still running. Use Stop-YO4XDevelopment.ps1 first."
    }
    Remove-Item -LiteralPath $statePath -Force
}

if (-not (Test-Path -LiteralPath $secretsPath -PathType Leaf)) {
    $secretRecord = [ordered]@{
        Administrator = (ConvertTo-SecureString (New-SecretText) -AsPlainText -Force)
        Certificate = (ConvertTo-SecureString (New-SecretText) -AsPlainText -Force)
        CredentialProof = (ConvertTo-SecureString (New-ProofKeyText) -AsPlainText -Force)
        ImportProof = (ConvertTo-SecureString (New-ProofKeyText) -AsPlainText -Force)
        Roles = [ordered]@{}
    }
    foreach ($role in $runtimeRoles) {
        $secretRecord.Roles[$role] = ConvertTo-SecureString (New-SecretText) -AsPlainText -Force
    }
    [pscustomobject]$secretRecord | Export-Clixml -LiteralPath $secretsPath
}
$secrets = Import-Clixml -LiteralPath $secretsPath

$certificate = Get-ChildItem Cert:\CurrentUser\My | Where-Object {
    $_.HasPrivateKey -and $_.NotAfter -gt [DateTime]::UtcNow.AddDays(7) -and
    ($_.Extensions | Where-Object { $_.Oid.Value -eq '1.3.6.1.4.1.311.84.1.1' })
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if ($null -eq $certificate) {
    throw "A current, exportable ASP.NET Core HTTPS development certificate was not found. Run dotnet dev-certs https --trust."
}
$san = ($certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' }).Format($true)
if ($san -notmatch '127\.0\.0\.1') {
    throw "The HTTPS development certificate does not contain the required 127.0.0.1 IP SAN."
}
$certificatePassword = ConvertTo-PlainText $secrets.Certificate
$secureCertificatePassword = ConvertTo-SecureString $certificatePassword -AsPlainText -Force
Export-PfxCertificate -Cert $certificate -FilePath $identityPfx `
    -Password $secureCertificatePassword -Force | Out-Null
$identityCertificateSha256 = $certificate.GetCertHashString(
    [Security.Cryptography.HashAlgorithmName]::SHA256)

if (-not $SkipBuild) {
    & $dotnet build $bootstrapProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Development bootstrap build failed." }
}
$env:YO4X_BOOTSTRAP_CERTIFICATE_PASSWORD = $certificatePassword
try {
    & $dotnet $bootstrapDll export-postgres-certificate `
        $identityPfx $postgresCertificate $postgresPrivateKey
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL certificate export failed." }
}
finally { Remove-Item Env:\YO4X_BOOTSTRAP_CERTIFICATE_PASSWORD -ErrorAction SilentlyContinue }

$visualCppDirectory = Get-ChildItem 'C:\Program Files (x86)\Microsoft\Edge\Application' -Directory `
    -ErrorAction SilentlyContinue | Sort-Object Name -Descending | Where-Object {
        Test-Path (Join-Path $_.FullName 'vcruntime140.dll')
    } | Select-Object -First 1 -ExpandProperty FullName
if ([string]::IsNullOrWhiteSpace($visualCppDirectory)) {
    throw "The Microsoft-signed Visual C++ runtime required by PostgreSQL was not found."
}
$postgresPath = "$postgresBin;$visualCppDirectory;$env:PATH"
$postgresProcess = $null
if (-not (Test-Path -LiteralPath (Join-Path $postgresData 'PG_VERSION'))) {
    New-Item -ItemType Directory -Force -Path $postgresData | Out-Null
    $passwordFile = Join-Path $developmentRoot "postgres-admin-password.tmp"
    try {
        [IO.File]::WriteAllText($passwordFile, (ConvertTo-PlainText $secrets.Administrator) + [Environment]::NewLine)
        $oldPath = $env:PATH; $env:PATH = $postgresPath
        & $initdb -D $postgresData --username=postgres --pwfile=$passwordFile `
            --auth-host=scram-sha-256 --auth-local=scram-sha-256 --encoding=UTF8 --locale=C
        if ($LASTEXITCODE -ne 0) { throw "PostgreSQL initdb failed." }
    }
    finally {
        $env:PATH = $oldPath
        if (Test-Path -LiteralPath $passwordFile) { Remove-Item -LiteralPath $passwordFile -Force }
    }
    $postgresConfig = @"

# YO4X persistent native Development cluster.
listen_addresses = '127.0.0.1'
port = $postgresPort
ssl = on
ssl_cert_file = '$($postgresCertificate.Replace('\','/'))'
ssl_key_file = '$($postgresPrivateKey.Replace('\','/'))'
password_encryption = 'scram-sha-256'
max_connections = 50
log_statement = 'none'
log_parameter_max_length = 0
log_parameter_max_length_on_error = 0
"@
    [IO.File]::AppendAllText((Join-Path $postgresData 'postgresql.conf'), $postgresConfig)
    $hba = @"
# TLS-only loopback authentication for the workspace-local cluster.
hostssl all all 127.0.0.1/32 scram-sha-256
hostnossl all all 127.0.0.1/32 reject
host all all 0.0.0.0/0 reject
host all all ::/0 reject
"@
    [IO.File]::WriteAllText((Join-Path $postgresData 'pg_hba.conf'), $hba)
}
if (-not (Test-PortAvailable $postgresPort)) {
    throw "Loopback port $postgresPort is already in use by a process not owned by this launcher."
}
$oldPath = $env:PATH; $env:PATH = $postgresPath
try {
    & $pgCtl -D $postgresData -l $postgresLog -w -t 30 start
    if ($LASTEXITCODE -ne 0) { throw "PostgreSQL failed to start." }
}
finally { $env:PATH = $oldPath }
$postgresPid = [int](Get-Content -LiteralPath (Join-Path $postgresData 'postmaster.pid') -TotalCount 1)
$postgresProcess = Get-Process -Id $postgresPid -ErrorAction Stop
$processRecords = @(
    [pscustomobject]@{ name='postgres'; pid=$postgresProcess.Id; executable=$postgres; startTimeUtc=$postgresProcess.StartTime.ToUniversalTime().ToString('O') }
)
Save-DevelopmentState $processRecords

$administratorPassword = ConvertTo-PlainText $secrets.Administrator
$baseConnection = "Host=127.0.0.1;Port=$postgresPort;Database=yo4x_development;SSL Mode=VerifyFull;Root Certificate=$postgresCertificate;Include Error Detail=false;Log Parameters=false;Multiplexing=false;No Reset On Close=false;Timeout=5;Command Timeout=30"
$env:YO4X_BOOTSTRAP_ADMIN_CONNECTION = "Host=127.0.0.1;Port=$postgresPort;Database=postgres;Username=postgres;Password=$administratorPassword;SSL Mode=VerifyFull;Root Certificate=$postgresCertificate;Include Error Detail=false;Log Parameters=false;Pooling=false;Timeout=5;Command Timeout=180"
foreach ($role in $runtimeRoles) {
    $variable = 'YO4X_BOOTSTRAP_PASSWORD_' + $role.Substring(5).ToUpperInvariant()
    [Environment]::SetEnvironmentVariable($variable, (ConvertTo-PlainText $secrets.Roles.$role), 'Process')
}
try {
    & $dotnet $bootstrapDll database $roleScript
    if ($LASTEXITCODE -ne 0) { throw "YO4X schema or role provisioning failed." }
}
finally {
    Remove-Item Env:\YO4X_BOOTSTRAP_ADMIN_CONNECTION -ErrorAction SilentlyContinue
    foreach ($role in $runtimeRoles) {
        $variable = 'YO4X_BOOTSTRAP_PASSWORD_' + $role.Substring(5).ToUpperInvariant()
        [Environment]::SetEnvironmentVariable($variable, $null, 'Process')
    }
    $administratorPassword = $null
}

if (-not $SkipBuild) {
    & $dotnet build $identityProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Development identity build failed." }
    & $dotnet build $apiProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "ControlPlane API build failed." }
    & $dotnet build $mt5WorkerProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "MT5 connection-probe worker build failed." }
    & $mt5ManifestGenerator -DeploymentRoot $mt5WorkerRoot -Entrypoint $mt5WorkerExe | Out-Null
    & $dotnet build $mt5CanaryProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "MT5 development canary build failed." }
    & $dotnet build $credentialWriterProject --configuration Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "Local credential writer build failed." }
    if (-not (Test-Path -LiteralPath $vite)) {
        Push-Location $frontendRoot
        try { & $npm ci --ignore-scripts; if ($LASTEXITCODE -ne 0) { throw "Frontend dependency restore failed." } }
        finally { Pop-Location }
    }
    & $dotnet publish $desktopProject --configuration Release --runtime win-x64 `
        --self-contained false --output (Split-Path $desktopExe -Parent) --nologo `
        -m:1 -p:BuildInParallel=false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "Desktop publish failed." }
}

if (-not (Test-Path -LiteralPath $identityDll) -or
    -not (Test-Path -LiteralPath $apiDll) -or
    -not (Test-Path -LiteralPath $mt5CanaryExe) -or
    -not (Test-Path -LiteralPath $credentialWriterExe) -or
    -not (Test-Path -LiteralPath $mt5WorkerExe) -or
    -not (Test-Path -LiteralPath $mt5WorkerManifest) -or
    -not (Test-Path -LiteralPath $vite) -or
    (-not $NoDesktop -and -not (Test-Path -LiteralPath $desktopExe))) {
    throw "One or more release launch artifacts are missing. Run without -SkipBuild."
}
foreach ($port in @(7210, 7209, 5173)) {
    if (-not (Test-PortAvailable $port)) { throw "Required loopback port $port is already in use." }
}

function RoleConnection([string] $role) {
    return "$baseConnection;Username=$role;Password=$(ConvertTo-PlainText $secrets.Roles.$role);Maximum Pool Size=4;Minimum Pool Size=0"
}
$commonKestrel = @{
    'Kestrel__Certificates__Default__Path' = $identityPfx
    'Kestrel__Certificates__Default__Password' = $certificatePassword
}
$identityEnvironment = @{
    'ASPNETCORE_ENVIRONMENT' = 'Development'
    'ASPNETCORE_URLS' = 'https://127.0.0.1:7210'
    'LocalIdentity__Enabled' = 'true'
    'LocalIdentity__DatabasePath' = (Join-Path $developmentRoot 'identity\identity.db')
    'ConnectionStrings__LocalIdentityPostgres' = (RoleConnection 'yo4x_local_identity')
}
$commonKestrel.GetEnumerator() | ForEach-Object { $identityEnvironment[$_.Key] = $_.Value }
$env:YO4X_BOOTSTRAP_LOCAL_IDENTITY_CONNECTION = $identityEnvironment['ConnectionStrings__LocalIdentityPostgres']
try {
    & $dotnet $bootstrapDll validate-local-identity-connection
    if ($LASTEXITCODE -ne 0) { throw "Local identity connection preflight failed." }
}
finally { Remove-Item Env:\YO4X_BOOTSTRAP_LOCAL_IDENTITY_CONNECTION -ErrorAction SilentlyContinue }
$identityProcess = Start-LoggedProcess 'identity' $dotnet @($identityDll) $identityEnvironment $workspaceRoot
$processRecords += [pscustomobject]@{ name='identity'; pid=$identityProcess.Id; executable=$dotnet; startTimeUtc=$identityProcess.StartTime.ToUniversalTime().ToString('O') }
Save-DevelopmentState $processRecords
Wait-Endpoint 'https://127.0.0.1:7210/.well-known/openid-configuration' $identityProcess

$policyPublicKey = (& $dotnet $bootstrapDll new-policy-public-key | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($policyPublicKey)) {
    throw "Development policy trust key generation failed."
}
$mt5CredentialKey = [Environment]::GetEnvironmentVariable(
    'YO4X_DEVELOPMENT_MT5_CREDENTIAL_KEY',
    'Process')
if ([string]::IsNullOrWhiteSpace($mt5CredentialKey) -or
    $mt5CredentialKey -cnotmatch '^[0-9a-f]{64}$' -or
    -not (Test-Path -LiteralPath (Join-Path $mt5VaultRoot ($mt5CredentialKey + '.yo4xcred')) -PathType Leaf)) {
    throw "Set YO4X_DEVELOPMENT_MT5_CREDENTIAL_KEY to the approved opaque Vantage demo vault key."
}
$mt5CanarySha256 = (Get-FileHash -LiteralPath $mt5CanaryExe -Algorithm SHA256).Hash.ToLowerInvariant()
$credentialWriterSha256 = (Get-FileHash -LiteralPath $credentialWriterExe -Algorithm SHA256).Hash.ToLowerInvariant()
$mt5WorkerSha256 = (Get-FileHash -LiteralPath $mt5WorkerExe -Algorithm SHA256).Hash.ToLowerInvariant()
$mt5ManifestSha256 = (Get-FileHash -LiteralPath $mt5WorkerManifest -Algorithm SHA256).Hash.ToLowerInvariant()
$mt5ArtifactSha256 = (Get-FileHash -LiteralPath $mt5Artifact -Algorithm SHA256).Hash.ToLowerInvariant()
if ($mt5ArtifactSha256 -cne 'eb238c958a4d9f80c8a3eeaca07636ae53bc5a78a093bc3fe63923fa50a309c6') {
    throw "The development MT5 bridge does not match the pinned approved artifact."
}
$apiEnvironment = @{
    'DOTNET_ROOT' = (Split-Path $dotnet -Parent)
    'ASPNETCORE_ENVIRONMENT' = 'Development'
    'ASPNETCORE_URLS' = 'https://127.0.0.1:7209'
    'Authentication__User__Authority' = 'https://127.0.0.1:7210/'
    'Authentication__User__Audience' = 'yo4x-control-plane'
    'Authentication__User__DevelopmentAuthorityCertificateSha256' = $identityCertificateSha256
    'Authentication__Workload__Authority' = 'https://127.0.0.1:7210/'
    'Authentication__Workload__Audience' = 'yo4x-runtime'
    'ConnectionStrings__Postgres' = (RoleConnection 'yo4x_control_api')
    'ConnectionStrings__ContextIssuer' = (RoleConnection 'yo4x_context_issuer')
    'ConnectionStrings__RuntimePostgres' = (RoleConnection 'yo4x_worker')
    'ConnectionStrings__RuntimeEvidencePostgres' = (RoleConnection 'yo4x_runtime_evidence')
    'SecretIngestion__CredentialProofKeyBase64' = (ConvertTo-PlainText $secrets.CredentialProof)
    'SecretIngestion__Origin' = 'https://127.0.0.1:7211/'
    'SecretIngestion__ApprovedClientOrigin' = 'https://127.0.0.1:7211/'
    'Conversion__ImportProofKeyBase64' = (ConvertTo-PlainText $secrets.ImportProof)
    'PolicyTrust__EcdsaP256Keys__development' = $policyPublicKey
    'U0__ApprovedGatewayDigest' = ('0' * 64)
    'U0__ApprovedRegion' = 'local-development'
    'U0__ApprovedBrokerServer' = 'MetaQuotes-Demo'
    'U0__ApprovedBrokerProfileId' = '019c8d27-763d-7000-8000-000000000002'
    'RuntimePostgres__ApprovedRuntimeImageDigest' = ('sha256:' + ('0' * 64))
    'MarketplacePublication__SharedSecretFile' = 'C:\Users\Dev23\Desktop\admin\data\marketplace-publication.secret'
    'MarketplacePublication__PackageKeyDocumentFile' = 'C:\Users\Dev23\Desktop\admin\data\package-keys.json'
    'MarketplacePublication__ArtifactRoot' = (Join-Path $developmentRoot 'strategy-packages')
    'MarketplacePublication__TenantId' = '019c8d27-763d-7000-8000-000000000001'
    'MarketplacePublication__ActorId' = '019c8d27-763d-7000-8000-000000000002'
    'DevelopmentMt5ConnectionProbe__Enabled' = 'true'
    'DevelopmentMt5ConnectionProbe__CanaryPath' = $mt5CanaryExe
    'DevelopmentMt5ConnectionProbe__CanarySha256' = $mt5CanarySha256
    'DevelopmentMt5ConnectionProbe__BrokerAccountId' = '019c8d27-763d-7000-8000-000000000002'
    'DevelopmentMt5ConnectionProbe__CredentialKey' = $mt5CredentialKey
    'DevelopmentMt5ConnectionProbe__ArtifactId' = '019c8d27-763d-7000-8000-000000000004'
    'DevelopmentMt5ConnectionProbe__ArtifactSha256' = $mt5ArtifactSha256
    'DevelopmentMt5ConnectionProbe__ArtifactPath' = $mt5Artifact
    'DevelopmentMt5ConnectionProbe__VaultRoot' = $mt5VaultRoot
    'DevelopmentMt5ConnectionProbe__BrokerCompany' = 'Vantage Markets (Pty) Ltd'
    'DevelopmentMt5ConnectionProbe__ServerName' = 'VantageMarkets-Demo'
    'DevelopmentMt5ConnectionProbe__Host' = 'a2ccde13c297ed6bd.awsglobalaccelerator.com'
    'DevelopmentMt5ConnectionProbe__Port' = '700'
    'DevelopmentMt5ConnectionProbe__WorkerPath' = $mt5WorkerExe
    'DevelopmentMt5ConnectionProbe__WorkerSha256' = $mt5WorkerSha256
    'DevelopmentMt5ConnectionProbe__ManifestPath' = $mt5WorkerManifest
    'DevelopmentMt5ConnectionProbe__ManifestSha256' = $mt5ManifestSha256
    'DevelopmentMt5ConnectionProbe__TimeoutMilliseconds' = '10000'
    # The API keeps a broker password only long enough to hand it to this pinned
    # writer, which is the sole process allowed to touch the DPAPI vault. Without
    # this section the link dialog fails closed instead of storing anything.
    'LocalBrokerCredentialVault__Enabled' = 'true'
    'LocalBrokerCredentialVault__WriterPath' = $credentialWriterExe
    'LocalBrokerCredentialVault__WriterSha256' = $credentialWriterSha256
    'LocalBrokerCredentialVault__VaultRoot' = $mt5VaultRoot
    'LocalBrokerCredentialVault__TimeoutMilliseconds' = '15000'
}
$commonKestrel.GetEnumerator() | ForEach-Object { $apiEnvironment[$_.Key] = $_.Value }
$apiProcess = Start-LoggedProcess 'control-plane' $dotnet @($apiDll) $apiEnvironment $workspaceRoot
$processRecords += [pscustomobject]@{ name='control-plane'; pid=$apiProcess.Id; executable=$dotnet; startTimeUtc=$apiProcess.StartTime.ToUniversalTime().ToString('O') }
Save-DevelopmentState $processRecords
Wait-Endpoint 'https://127.0.0.1:7209/health/live' $apiProcess

$frontendEnvironment = @{
    'VITE_YO4X_DEVELOPMENT_IDENTITY_ENABLED' = 'true'
    'VITE_YO4X_CONTROL_API_ORIGIN' = 'http://127.0.0.1:5173'
}
$frontendProcess = Start-LoggedProcess 'frontend' $node @($vite, '--host', '127.0.0.1', '--port', '5173', '--strictPort') $frontendEnvironment $frontendRoot
$processRecords += [pscustomobject]@{ name='frontend'; pid=$frontendProcess.Id; executable=$node; startTimeUtc=$frontendProcess.StartTime.ToUniversalTime().ToString('O') }
Save-DevelopmentState $processRecords
Wait-Endpoint 'http://127.0.0.1:5173/' $frontendProcess

$desktopProcess = $null
if (-not $NoDesktop) {
    $desktopProcess = Start-Process -FilePath $desktopExe -WorkingDirectory (Split-Path $desktopExe -Parent) `
        -ArgumentList @('--app-url', 'http://127.0.0.1:5173/', '--identity-url',
        'https://127.0.0.1:7210/', '--development-identity-certificate-sha256',
        $identityCertificateSha256, '--control-api-url', 'https://127.0.0.1:7209/') -PassThru
}
if ($null -ne $desktopProcess) {
    $processRecords += [pscustomobject]@{ name='desktop'; pid=$desktopProcess.Id; executable=$desktopExe; startTimeUtc=$desktopProcess.StartTime.ToUniversalTime().ToString('O') }
}
Save-DevelopmentState $processRecords

Write-Host "YO4X is live at http://127.0.0.1:5173/."
if (-not $NoDesktop) { Write-Host "The release YO4X.exe desktop UI has been launched." }
Write-Host "Use scripts\Stop-YO4XDevelopment.ps1 to stop this workspace stack."

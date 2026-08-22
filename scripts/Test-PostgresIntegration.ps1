[CmdletBinding()]
param(
    [string] $Configuration = "Release",
    [string] $Filter,
    [string] $VisualCppRuntimeDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$toolRoot = [IO.Path]::GetFullPath((Join-Path $workspaceRoot ".tools\postgresql-local"))
$lockPath = Join-Path $PSScriptRoot "postgresql-windows-x64.lock.json"
$lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json

function Assert-ChildPath {
    param(
        [Parameter(Mandatory = $true)][string] $Parent,
        [Parameter(Mandatory = $true)][string] $Child
    )

    $resolvedParent = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolvedChild = [IO.Path]::GetFullPath($Child)
    if (-not $resolvedChild.StartsWith($resolvedParent, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing filesystem operation outside the expected tool directory."
    }
}

function Assert-Sha256 {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Expected
    )

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash
    if (-not [string]::Equals($actual, $Expected, [StringComparison]::OrdinalIgnoreCase)) {
        throw "SHA-256 verification failed for '$Path'."
    }
}

function New-EphemeralPassword {
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

function Get-FreeLoopbackPort {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    try {
        $listener.Start()
        return ([Net.IPEndPoint]$listener.LocalEndpoint).Port
    }
    finally {
        $listener.Stop()
    }
}

function Resolve-VisualCppRuntime {
    param([string] $ExplicitDirectory)

    $candidates = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($ExplicitDirectory)) {
        $candidates.Add([IO.Path]::GetFullPath($ExplicitDirectory))
    }

    foreach ($edgeRoot in @(
        "C:\Program Files (x86)\Microsoft\Edge\Application",
        "C:\Program Files\Microsoft\Edge\Application")) {
        if (Test-Path -LiteralPath $edgeRoot) {
            Get-ChildItem -LiteralPath $edgeRoot -Directory |
                Sort-Object Name -Descending |
                ForEach-Object { $candidates.Add($_.FullName) }
        }
    }

    $required = @("msvcp140.dll", "vcruntime140.dll", "vcruntime140_1.dll")
    foreach ($candidate in $candidates) {
        $valid = $true
        foreach ($fileName in $required) {
            $path = Join-Path $candidate $fileName
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                $valid = $false
                break
            }

            $signature = Get-AuthenticodeSignature -LiteralPath $path
            $isMicrosoftSigned =
                $signature.Status -eq [System.Management.Automation.SignatureStatus]::Valid -and
                $null -ne $signature.SignerCertificate -and
                $signature.SignerCertificate.Subject.IndexOf(
                    "Microsoft Corporation",
                    [StringComparison]::Ordinal) -ge 0
            if (-not $isMicrosoftSigned) {
                $valid = $false
                break
            }
        }

        if ($valid) {
            return $candidate
        }
    }

    throw "A Microsoft-signed x64 Visual C++ runtime was not found. Pass -VisualCppRuntimeDirectory."
}

$lockIsValid =
    $lock.version -match '^18\.\d+-\d+$' -and
    $lock.postgresVersion -match '^18\.\d+$' -and
    $lock.archiveSha256 -match '^[0-9a-f]{64}$' -and
    $lock.archiveUrl.StartsWith(
        "https://get.enterprisedb.com/postgresql/",
        [StringComparison]::Ordinal)
if (-not $lockIsValid) {
    throw "The PostgreSQL binary lock file is invalid."
}

New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
$downloadRoot = Join-Path $toolRoot "downloads"
New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null
$archivePath = Join-Path $downloadRoot $lock.archiveFileName
Assert-ChildPath -Parent $toolRoot -Child $archivePath

if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
    $partialPath = "$archivePath.partial-$PID"
    Assert-ChildPath -Parent $toolRoot -Child $partialPath
    try {
        $curl = Get-Command curl.exe -ErrorAction Stop
        & $curl.Source -fL --retry 3 --retry-delay 2 -o $partialPath $lock.archiveUrl
        if ($LASTEXITCODE -ne 0) {
            throw "The PostgreSQL binary download failed with exit code $LASTEXITCODE."
        }

        Assert-Sha256 -Path $partialPath -Expected $lock.archiveSha256
        if ((Get-Item -LiteralPath $partialPath).Length -ne [long]$lock.archiveLength) {
            throw "The PostgreSQL archive length does not match the lock file."
        }

        Move-Item -LiteralPath $partialPath -Destination $archivePath
    }
    finally {
        if (Test-Path -LiteralPath $partialPath) {
            Remove-Item -LiteralPath $partialPath -Force
        }
    }
}

Assert-Sha256 -Path $archivePath -Expected $lock.archiveSha256
if ((Get-Item -LiteralPath $archivePath).Length -ne [long]$lock.archiveLength) {
    throw "The PostgreSQL archive length does not match the lock file."
}

$packageRoot = Join-Path $toolRoot ("package-native-" + $lock.version)
$postgresRoot = Join-Path $packageRoot "pgsql"
$postgresBin = Join-Path $postgresRoot "bin"
$postgresExe = Join-Path $postgresBin "postgres.exe"
Assert-ChildPath -Parent $toolRoot -Child $packageRoot

if (-not (Test-Path -LiteralPath $postgresExe -PathType Leaf)) {
    if (Test-Path -LiteralPath $packageRoot) {
        throw "The versioned PostgreSQL package cache is incomplete; remove only '$packageRoot' and retry."
    }

    $extractRoot = Join-Path $toolRoot ("extract-" + [guid]::NewGuid().ToString("N"))
    Assert-ChildPath -Parent $toolRoot -Child $extractRoot
    New-Item -ItemType Directory -Path $extractRoot | Out-Null
    try {
        $tar = Get-Command tar.exe -ErrorAction Stop
        & $tar.Source -xf $archivePath -C $extractRoot
        if ($LASTEXITCODE -ne 0) {
            throw "PostgreSQL archive extraction failed with exit code $LASTEXITCODE."
        }

        $extractedPostgres = Join-Path $extractRoot "pgsql\bin\postgres.exe"
        if (-not (Test-Path -LiteralPath $extractedPostgres -PathType Leaf)) {
            throw "The PostgreSQL archive did not contain pgsql/bin/postgres.exe."
        }

        Move-Item -LiteralPath $extractRoot -Destination $packageRoot
    }
    finally {
        if (Test-Path -LiteralPath $extractRoot) {
            Remove-Item -LiteralPath $extractRoot -Recurse -Force
        }
    }
}

foreach ($property in $lock.runtimeExecutables.PSObject.Properties) {
    Assert-Sha256 -Path (Join-Path $postgresBin $property.Name) -Expected ([string]$property.Value)
}

$visualCppRuntime = Resolve-VisualCppRuntime -ExplicitDirectory $VisualCppRuntimeDirectory
$originalPath = $env:PATH
$originalIntegrationConnection = $env:YO4X_POSTGRES_INTEGRATION_ADMIN
$env:PATH = "$postgresBin;$visualCppRuntime;$originalPath"

$versionOutput = (& $postgresExe --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $versionOutput -ne "postgres (PostgreSQL) $($lock.postgresVersion)") {
    throw "The verified PostgreSQL server did not report the locked version."
}

$runsRoot = Join-Path $toolRoot "runs"
New-Item -ItemType Directory -Force -Path $runsRoot | Out-Null
$runDirectory = Join-Path $runsRoot ([guid]::NewGuid().ToString("N"))
Assert-ChildPath -Parent $runsRoot -Child $runDirectory
New-Item -ItemType Directory -Path $runDirectory | Out-Null
$dataDirectory = Join-Path $runDirectory "data"
$passwordFile = Join-Path $runDirectory "admin-password.txt"
$serverLog = Join-Path $runDirectory "postgres.log"
$administratorPassword = New-EphemeralPassword
$serverStarted = $false
$testExitCode = 1

try {
    $utf8NoBom = New-Object Text.UTF8Encoding($false)
    [IO.File]::WriteAllText(
        $passwordFile,
        $administratorPassword + [Environment]::NewLine,
        $utf8NoBom)

    & (Join-Path $postgresBin "initdb.exe") `
        -D $dataDirectory `
        --username=postgres `
        --pwfile=$passwordFile `
        --auth-host=scram-sha-256 `
        --auth-local=scram-sha-256 `
        --encoding=UTF8 `
        --locale=C
    if ($LASTEXITCODE -ne 0) {
        throw "initdb failed with exit code $LASTEXITCODE."
    }

    Remove-Item -LiteralPath $passwordFile -Force
    $port = Get-FreeLoopbackPort
    $postgresConfiguration = @"
listen_addresses = '127.0.0.1'
port = $port
ssl = off
password_encryption = 'scram-sha-256'
max_connections = 40
fsync = off
synchronous_commit = off
full_page_writes = off
"@
    [IO.File]::AppendAllText(
        (Join-Path $dataDirectory "postgresql.conf"),
        [Environment]::NewLine + $postgresConfiguration,
        $utf8NoBom)

    $hostAuthentication = @"
# YO4X disposable integration cluster: loopback password authentication only.
host all all 127.0.0.1/32 scram-sha-256
host all all 0.0.0.0/0 reject
host all all ::/0 reject
"@
    [IO.File]::WriteAllText(
        (Join-Path $dataDirectory "pg_hba.conf"),
        $hostAuthentication,
        $utf8NoBom)

    & (Join-Path $postgresBin "pg_ctl.exe") `
        -D $dataDirectory `
        -l $serverLog `
        -w `
        -t 30 `
        start
    if ($LASTEXITCODE -ne 0) {
        throw "pg_ctl failed to start PostgreSQL (exit code $LASTEXITCODE)."
    }
    $serverStarted = $true

    $env:YO4X_POSTGRES_INTEGRATION_ADMIN =
        "Host=127.0.0.1;Port=$port;Database=postgres;Username=postgres;" `
        + "Password=$administratorPassword;SSL Mode=Disable;Pooling=false;" `
        + "Timeout=5;Command Timeout=30"

    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if ($null -eq $dotnet) {
        $localDotnet = "C:\Users\Dev23\AppData\Local\YO4X\dotnet\dotnet.exe"
        if (-not (Test-Path -LiteralPath $localDotnet -PathType Leaf)) {
            throw "dotnet.exe was not found."
        }
        $dotnetPath = $localDotnet
    }
    else {
        $dotnetPath = $dotnet.Source
    }

    $testProject = Join-Path $workspaceRoot "tests\YO4X.Postgres.IntegrationTests\YO4X.Postgres.IntegrationTests.csproj"
    $arguments = [Collections.Generic.List[string]]::new()
    $arguments.Add("test")
    $arguments.Add($testProject)
    $arguments.Add("--configuration")
    $arguments.Add($Configuration)
    $arguments.Add("--nologo")
    if (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $arguments.Add("--filter")
        $arguments.Add($Filter)
    }

    & $dotnetPath $arguments
    $testExitCode = $LASTEXITCODE
}
finally {
    $env:YO4X_POSTGRES_INTEGRATION_ADMIN = $originalIntegrationConnection
    $serverStopped = -not $serverStarted
    if ($serverStarted) {
        & (Join-Path $postgresBin "pg_ctl.exe") `
            -D $dataDirectory `
            -m immediate `
            -w `
            -t 30 `
            stop | Out-Null
        $serverStopped = $LASTEXITCODE -eq 0
    }

    $administratorPassword = $null
    $env:PATH = $originalPath
    if ($serverStopped -and (Test-Path -LiteralPath $runDirectory)) {
        Assert-ChildPath -Parent $runsRoot -Child $runDirectory
        Remove-Item -LiteralPath $runDirectory -Recurse -Force
    }
    elseif (-not $serverStopped) {
        Write-Warning "PostgreSQL did not stop cleanly; preserving the run directory at '$runDirectory'."
    }
}

exit $testExitCode

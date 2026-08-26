<#
.SYNOPSIS
    Imports the real 198-file MQL5 corpus into the running development database
    and projects it into the strategy catalog the web application reads.

    Nothing is fabricated. Every catalog row is derived from the persisted corpus:
    the name comes from the source file path, the category from its declared
    program kind, and the analysis state from the conversion classification.
    Fields the corpus genuinely does not carry (author, symbol, timeframe,
    version, rating) are written as explicit "unspecified" markers or zero.
#>
[CmdletBinding()]
param(
    [string] $SourceRoot = (Join-Path $PSScriptRoot "..\Testing\Mq5"),
    [string] $ApiOrigin = "https://127.0.0.1:7209",
    [string] $IdentityOrigin = "https://127.0.0.1:7210",
    [switch] $SkipCorpusImport
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$developmentRoot = Join-Path $workspaceRoot ".local\development"
$secretsPath = Join-Path $developmentRoot "secrets.clixml"
$postgresCertificate = Join-Path $developmentRoot "certificates\postgres-server.crt"
$dotnet = Join-Path $workspaceRoot ".tools\dotnet-sdk-10.0.400\dotnet.exe"
$workerProject = Join-Path $workspaceRoot "src\Apps\YO4X.Conversion.Worker\YO4X.Conversion.Worker.csproj"
$postgresPort = 55432
$scratch = Join-Path $developmentRoot ("corpus-import-{0}" -f [guid]::NewGuid().ToString("N"))
$cookieJar = "$scratch.cookies"
$requestBody = "$scratch.request"

function ConvertTo-PlainText {
    param([Parameter(Mandatory = $true)][Security.SecureString] $Value)
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

function ConvertTo-Base64Url {
    param([Parameter(Mandatory = $true)][byte[]] $Bytes)
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

if (-not (Test-Path -LiteralPath $secretsPath)) {
    throw "The development stack has not been started; secrets.clixml is missing."
}
$secrets = Import-Clixml -LiteralPath $secretsPath
$baseConnection = "Host=127.0.0.1;Port=$postgresPort;Database=yo4x_development;SSL Mode=VerifyFull;Root Certificate=$postgresCertificate;Include Error Detail=false;Log Parameters=false;Multiplexing=false;No Reset On Close=false;Timeout=5;Command Timeout=120"
function RoleConnection([string] $role) {
    "$baseConnection;Username=$role;Password=$(ConvertTo-PlainText $secrets.Roles.$role);Maximum Pool Size=4;Minimum Pool Size=0"
}

# ---------------------------------------------------------------- authenticate
Write-Host "1. Registering a local development identity and completing PKCE..."
$random = New-Object byte[] 32
$generator = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $generator.GetBytes($random) } finally { $generator.Dispose() }
$verifier = ConvertTo-Base64Url $random
[Array]::Clear($random, 0, $random.Length)
$sha = [Security.Cryptography.SHA256]::Create()
try { $challenge = ConvertTo-Base64Url $sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($verifier)) }
finally { $sha.Dispose() }
$state = [guid]::NewGuid().ToString("N")
$returnUrl = "/connect/authorize?client_id=yo4x-web-development" `
    + "&redirect_uri=" + [Uri]::EscapeDataString("http://127.0.0.1:4173/auth/callback") `
    + "&response_type=code&scope=" + [Uri]::EscapeDataString("openid email profile") `
    + "&code_challenge=$challenge&code_challenge_method=S256&state=$state&nonce=" + [guid]::NewGuid().ToString("N")

try {
    $html = (& curl.exe --silent --insecure --cookie-jar $cookieJar --cookie $cookieJar `
        ("{0}/account/register?returnUrl={1}" -f $IdentityOrigin, [Uri]::EscapeDataString($returnUrl)) | Out-String)
    $antiforgery = [regex]::Match($html, 'name="__RequestVerificationToken" value="([^"]+)"')
    if (-not $antiforgery.Success) { throw "Antiforgery token was not returned." }
    $email = "corpus-{0}@example.test" -f [guid]::NewGuid().ToString("N")
    $pb = New-Object byte[] 24
    $g2 = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $g2.GetBytes($pb) } finally { $g2.Dispose() }
    $password = "Aa9!" + (ConvertTo-Base64Url $pb)
    [Array]::Clear($pb, 0, $pb.Length)
    $form = "__RequestVerificationToken=" + [Uri]::EscapeDataString($antiforgery.Groups[1].Value) `
        + "&email=" + [Uri]::EscapeDataString($email) `
        + "&password=" + [Uri]::EscapeDataString($password) `
        + "&returnUrl=" + [Uri]::EscapeDataString($returnUrl)
    [IO.File]::WriteAllText($requestBody, $form, (New-Object Text.UTF8Encoding($false)))
    $headers = (& curl.exe --silent --insecure --cookie-jar $cookieJar --cookie $cookieJar `
        --dump-header - --output NUL --request POST `
        --header "Content-Type: application/x-www-form-urlencoded" `
        --data-binary "@$requestBody" ("{0}/account/register" -f $IdentityOrigin) | Out-String)
    $loc = [regex]::Match($headers, '(?im)^location:\s*([^\r\n]+)').Groups[1].Value.Trim()
    if ($loc.StartsWith('/')) { $loc = $IdentityOrigin + $loc }
    $ah = (& curl.exe --silent --insecure --cookie-jar $cookieJar --cookie $cookieJar `
        --dump-header - --output NUL $loc | Out-String)
    $cb = [Uri]([regex]::Match($ah, '(?im)^location:\s*([^\r\n]+)').Groups[1].Value.Trim())
    $code = [regex]::Match($cb.Query, '(?:^|[?&])code=([^&]+)').Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($code)) { throw "Authorization code was not issued." }
    $tokenForm = "grant_type=authorization_code&client_id=yo4x-web-development" `
        + "&redirect_uri=" + [Uri]::EscapeDataString("http://127.0.0.1:4173/auth/callback") `
        + "&code=" + [Uri]::EscapeDataString([Uri]::UnescapeDataString($code)) `
        + "&code_verifier=" + [Uri]::EscapeDataString($verifier)
    [IO.File]::WriteAllText($requestBody, $tokenForm, (New-Object Text.UTF8Encoding($false)))
    $token = (& curl.exe --silent --insecure --request POST `
        --header "Content-Type: application/x-www-form-urlencoded" --data-binary "@$requestBody" `
        ("{0}/connect/token" -f $IdentityOrigin) | Out-String) | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($token.access_token)) { throw "Token exchange failed." }
    Write-Host "   authenticated as $email"

    if (-not $SkipCorpusImport) {
        # ------------------------------------------------- strategy import session
        Write-Host "2. Creating a strategy-source import session..."
        $idempotency = (ConvertTo-Base64Url ([guid]::NewGuid().ToByteArray() + [guid]::NewGuid().ToByteArray()))
        $sessionBody = '{"sourceLabel":"testing-mq5-corpus"}'
        $bodyFile = "$scratch.session"
        [IO.File]::WriteAllText($bodyFile, $sessionBody, (New-Object Text.UTF8Encoding($false)))
        # curl config files treat a backslash as an escape; use forward slashes.
        $bodyForward = $bodyFile.Replace('\', '/')
        $configuration = "url = `"$ApiOrigin/v1/strategy-source-import-sessions`"`n" `
            + "insecure`nsilent`nshow-error`nrequest = `"POST`"`n" `
            + "header = `"Content-Type: application/json`"`n" `
            + "header = `"Idempotency-Key: $idempotency`"`n" `
            + "header = `"Authorization: Bearer $($token.access_token)`"`n" `
            + "data-binary = `"@$bodyForward`""
        [IO.File]::WriteAllText($requestBody, $configuration, (New-Object Text.UTF8Encoding($false)))
        $sessionRaw = (& curl.exe --config $requestBody | Out-String).Trim()
        Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue
        $session = $null
        try { $session = $sessionRaw | ConvertFrom-Json } catch { }
        if ($null -eq $session -or -not $session.PSObject.Properties.Name.Contains('importJobId')) {
            throw "Import session was refused: $sessionRaw"
        }
        Write-Host "   import job $($session.importJobId)"

        # ------------------------------------------------------- persist the corpus
        Write-Host "3. Analysing and persisting the corpus (198 files)..."
        $env:YO4X_CONVERSION_POSTGRES_CONNECTION = RoleConnection 'yo4x_conversion_worker'
        $env:YO4X_CONVERSION_IMPORT_CAPABILITY = $session.singleUseCapability
        $outputRoot = Join-Path $developmentRoot "corpus-artifacts"
        New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
        try {
            & $dotnet run --project $workerProject -c Debug --no-build -- `
                --static-inventory `
                --source-root ([IO.Path]::GetFullPath($SourceRoot)) `
                --manifest-output (Join-Path $outputRoot "manifest.json") `
                --report-output (Join-Path $outputRoot "report.md") `
                --conversion-evidence-output (Join-Path $outputRoot "evidence.json") `
                --conversion-evidence-report-output (Join-Path $outputRoot "evidence.md") `
                --persist-postgres `
                --import-job-id $session.importJobId
            if ($LASTEXITCODE -ne 0) { throw "Corpus persistence failed with exit code $LASTEXITCODE." }
        }
        finally {
            $env:YO4X_CONVERSION_IMPORT_CAPABILITY = $null
            $env:YO4X_CONVERSION_POSTGRES_CONNECTION = $null
        }
    }

    Write-Host "4. Projecting the persisted corpus into the strategy catalog..."
    $projection = Join-Path $PSScriptRoot "project-corpus-to-catalog.sql"
    if (-not (Test-Path -LiteralPath $projection)) { throw "Projection script is missing: $projection" }
    $env:PGPASSWORD = ConvertTo-PlainText $secrets.Administrator
    $psql = Join-Path $workspaceRoot ".tools\postgresql-local\package-native-18.6-1\pgsql\bin\psql.exe"
    if (-not (Test-Path -LiteralPath $psql)) { throw "psql was not found at $psql" }
    try {
        & $psql --host 127.0.0.1 --port $postgresPort --username postgres --dbname yo4x_development `
            --set=ON_ERROR_STOP=1 --file $projection
        if ($LASTEXITCODE -ne 0) { throw "Catalog projection failed with exit code $LASTEXITCODE." }
    }
    finally { $env:PGPASSWORD = $null }

    Write-Host "5. Verifying through the authenticated API..."
    $configuration = "url = `"$ApiOrigin/v1/catalog/strategies?pageSize=3`"`ninsecure`nsilent`n" `
        + "header = `"Authorization: Bearer $($token.access_token)`""
    [IO.File]::WriteAllText($requestBody, $configuration, (New-Object Text.UTF8Encoding($false)))
    $catalog = (& curl.exe --config $requestBody | Out-String).Trim()
    $parsed = $catalog | ConvertFrom-Json
    Write-Host ("   catalog totalCount = {0}" -f $parsed.totalCount)
    Write-Host ("   categories        = {0}" -f ($parsed.categories -join ', '))
    foreach ($item in $parsed.items) { Write-Host ("   - {0}  [{1}]" -f $item.name, $item.category) }
}
finally {
    $token = $null
    Remove-Item -LiteralPath $cookieJar, $requestBody -Force -ErrorAction SilentlyContinue
}

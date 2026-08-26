<#
.SYNOPSIS
    End-to-end check of the frontend projection endpoints against the running
    development stack. Registers a throwaway local identity, completes the PKCE
    exchange, then calls every /v1 projection route with a real bearer token.

    No data is stubbed. Empty collections are the expected result on a fresh
    database and are reported as such.
#>
[CmdletBinding()]
param(
    [string] $ApiOrigin = "https://127.0.0.1:7209",
    [string] $IdentityOrigin = "https://127.0.0.1:7210"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$temporaryRoot = Join-Path $workspaceRoot ".local\development"
$cookieJar = Join-Path $temporaryRoot ("projection-{0}.cookies" -f [guid]::NewGuid().ToString("N"))
$requestBody = Join-Path $temporaryRoot ("projection-{0}.request" -f [guid]::NewGuid().ToString("N"))

function ConvertTo-Base64Url {
    param([Parameter(Mandatory = $true)][byte[]] $Bytes)
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$random = New-Object byte[] 32
$generator = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $generator.GetBytes($random) } finally { $generator.Dispose() }
$verifier = ConvertTo-Base64Url $random
[Array]::Clear($random, 0, $random.Length)
$sha = [Security.Cryptography.SHA256]::Create()
try { $challenge = ConvertTo-Base64Url $sha.ComputeHash([Text.Encoding]::ASCII.GetBytes($verifier)) }
finally { $sha.Dispose() }
$state = [guid]::NewGuid().ToString("N")
$nonce = [guid]::NewGuid().ToString("N")
$returnUrl = "/connect/authorize?client_id=yo4x-web-development" `
    + "&redirect_uri=" + [Uri]::EscapeDataString("http://127.0.0.1:4173/auth/callback") `
    + "&response_type=code&scope=" + [Uri]::EscapeDataString("openid email profile") `
    + "&code_challenge=$challenge&code_challenge_method=S256&state=$state&nonce=$nonce"

try {
    $html = (& curl.exe --silent --insecure --cookie-jar $cookieJar --cookie $cookieJar `
        ("{0}/account/register?returnUrl={1}" -f $IdentityOrigin, [Uri]::EscapeDataString($returnUrl)) | Out-String)
    $antiforgery = [regex]::Match($html, 'name="__RequestVerificationToken" value="([^"]+)"')
    if (-not $antiforgery.Success) { throw "Antiforgery token was not returned." }

    $email = "projection-{0}@example.test" -f [guid]::NewGuid().ToString("N")
    $passwordBytes = New-Object byte[] 24
    $passwordGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $passwordGenerator.GetBytes($passwordBytes) } finally { $passwordGenerator.Dispose() }
    $password = "Aa9!" + (ConvertTo-Base64Url $passwordBytes)
    [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)

    $form = "__RequestVerificationToken=" + [Uri]::EscapeDataString($antiforgery.Groups[1].Value) `
        + "&email=" + [Uri]::EscapeDataString($email) `
        + "&password=" + [Uri]::EscapeDataString($password) `
        + "&returnUrl=" + [Uri]::EscapeDataString($returnUrl)
    [IO.File]::WriteAllText($requestBody, $form, (New-Object Text.UTF8Encoding($false)))
    $headers = (& curl.exe --silent --insecure --cookie-jar $cookieJar --cookie $cookieJar `
        --dump-header - --output NUL --request POST `
        --header "Content-Type: application/x-www-form-urlencoded" `
        --data-binary "@$requestBody" ("{0}/account/register" -f $IdentityOrigin) | Out-String)
    $location = [regex]::Match($headers, '(?im)^location:\s*([^\r\n]+)')
    if (-not $location.Success) { throw "Registration did not return an authorization redirect." }
    $authorizeLocation = $location.Groups[1].Value.Trim()
    if ($authorizeLocation.StartsWith('/')) { $authorizeLocation = $IdentityOrigin + $authorizeLocation }

    $authorizeHeaders = (& curl.exe --silent --insecure --cookie-jar $cookieJar --cookie $cookieJar `
        --dump-header - --output NUL $authorizeLocation | Out-String)
    $callbackLocation = [regex]::Match($authorizeHeaders, '(?im)^location:\s*([^\r\n]+)')
    if (-not $callbackLocation.Success) { throw "Authorization did not return a callback." }
    $callback = [Uri]$callbackLocation.Groups[1].Value.Trim()
    $code = [regex]::Match($callback.Query, '(?:^|[?&])code=([^&]+)').Groups[1].Value
    if ([string]::IsNullOrWhiteSpace($code)) { throw "No authorization code was returned." }

    $tokenForm = "grant_type=authorization_code&client_id=yo4x-web-development" `
        + "&redirect_uri=" + [Uri]::EscapeDataString("http://127.0.0.1:4173/auth/callback") `
        + "&code=" + [Uri]::EscapeDataString([Uri]::UnescapeDataString($code)) `
        + "&code_verifier=" + [Uri]::EscapeDataString($verifier)
    [IO.File]::WriteAllText($requestBody, $tokenForm, (New-Object Text.UTF8Encoding($false)))
    $token = (& curl.exe --silent --insecure --request POST `
        --header "Content-Type: application/x-www-form-urlencoded" --data-binary "@$requestBody" `
        ("{0}/connect/token" -f $IdentityOrigin) | Out-String) | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($token.access_token)) { throw "Token exchange failed." }

    $routes = @(
        '/v1/me',
        '/v1/me/sessions',
        '/v1/broker-accounts',
        '/v1/broker-account-registration-options',
        # The same route with a search term answers from the imported MetaTrader 5
        # server directory instead of the tenant approved list.
        '/v1/broker-account-registration-options?query=vantage',
        '/v1/dashboard/summary',
        '/v1/bridge/status',
        '/v1/catalog/strategies',
        '/v1/strategy-source-corpora',
        '/v1/bots',
        '/v1/bots/uptime',
        '/v1/backtests',
        '/v1/cloud/plans',
        '/v1/cloud/runners',
        '/v1/cloud/regions',
        '/v1/journal'
    )

    $results = foreach ($route in $routes) {
        $bodyFile = Join-Path $temporaryRoot ("projection-body-{0}" -f [guid]::NewGuid().ToString("N"))
        # curl config files treat a backslash as an escape, so a Windows path
        # written verbatim silently discards the response body and every route
        # reports an unknown shape even when it answered correctly.
        $configuration = "url = `"$ApiOrigin$route`"`n" `
            + "insecure`nsilent`nwrite-out = `"%{http_code}`"`n" `
            + "output = `"$($bodyFile.Replace('\', '/'))`"`n" `
            + "header = `"Authorization: Bearer $($token.access_token)`""
        [IO.File]::WriteAllText($requestBody, $configuration, (New-Object Text.UTF8Encoding($false)))
        $status = (& curl.exe --config $requestBody | Out-String).Trim()
        $payload = if (Test-Path $bodyFile) { (Get-Content -Raw -LiteralPath $bodyFile) } else { '' }
        Remove-Item -LiteralPath $bodyFile -Force -ErrorAction SilentlyContinue

        $shape = '—'
        if ($payload) {
            try {
                $parsed = $payload | ConvertFrom-Json
                if ($parsed -is [Array]) { $shape = "array[$($parsed.Count)]" }
                elseif ($parsed.PSObject.Properties.Name -contains 'items') { $shape = "items[$($parsed.items.Count)]" }
                elseif ($parsed.PSObject.Properties.Name -contains 'samples') { $shape = "samples[$($parsed.samples.Count)]" }
                elseif ($parsed.PSObject.Properties.Name -contains 'stats') { $shape = "stats[$($parsed.stats.Count)] running[$($parsed.runningBots.Count)]" }
                else { $shape = 'object' }
            } catch { $shape = 'non-json' }
        }

        [PSCustomObject]@{
            Route  = $route
            Status = $status
            Shape  = $shape
            Result = if ($status -eq '200') { 'ok' } else { 'FAIL' }
        }
    }

    $results | Format-Table -AutoSize
    $failed = @($results | Where-Object { $_.Result -ne 'ok' })
    if ($failed.Count -gt 0) {
        Write-Host ("{0} of {1} routes failed." -f $failed.Count, $results.Count)
        exit 1
    }
    Write-Host ("All {0} routes returned 200 against the live backend." -f $results.Count)
}
finally {
    $token = $null
    Remove-Item -LiteralPath $cookieJar, $requestBody -Force -ErrorAction SilentlyContinue
}

<#
.SYNOPSIS
    Dumps the raw /v1/catalog/strategies/{id}/inputs payload for one strategy, so a
    decoder rejection can be compared against what the service actually returned.
#>
[CmdletBinding()]
param(
    [string] $ApiOrigin = "https://127.0.0.1:7209",
    [string] $IdentityOrigin = "https://127.0.0.1:7210",
    [string] $StrategyId = "REPLACE"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$temporaryRoot = Join-Path $workspaceRoot ".local\development"
$cookieJar = Join-Path $temporaryRoot ("inputs-{0}.cookies" -f [guid]::NewGuid().ToString("N"))
$requestBody = Join-Path $temporaryRoot ("inputs-{0}.request" -f [guid]::NewGuid().ToString("N"))

function ConvertTo-Base64Url {
    param([Parameter(Mandatory = $true)][byte[]] $Bytes)
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$random = New-Object byte[] 32
$generator = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $generator.GetBytes($random) } finally { $generator.Dispose() }
$verifier = ConvertTo-Base64Url $random
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

    $email = "inputs-{0}@example.test" -f [guid]::NewGuid().ToString("N")
    $passwordBytes = New-Object byte[] 24
    $passwordGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $passwordGenerator.GetBytes($passwordBytes) } finally { $passwordGenerator.Dispose() }
    $password = "Aa9!" + (ConvertTo-Base64Url $passwordBytes)

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

    $tokenForm = "grant_type=authorization_code&client_id=yo4x-web-development" `
        + "&redirect_uri=" + [Uri]::EscapeDataString("http://127.0.0.1:4173/auth/callback") `
        + "&code=" + [Uri]::EscapeDataString([Uri]::UnescapeDataString($code)) `
        + "&code_verifier=" + [Uri]::EscapeDataString($verifier)
    [IO.File]::WriteAllText($requestBody, $tokenForm, (New-Object Text.UTF8Encoding($false)))
    $token = (& curl.exe --silent --insecure --request POST `
        --header "Content-Type: application/x-www-form-urlencoded" --data-binary "@$requestBody" `
        ("{0}/connect/token" -f $IdentityOrigin) | Out-String) | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($token.access_token)) { throw "Token exchange failed." }

    $payload = (& curl.exe --silent --insecure `
        --header ("Authorization: Bearer {0}" -f $token.access_token) `
        ("{0}/v1/broker-symbols?server=VantageMarkets-Demo&query=XAU" -f $ApiOrigin) | Out-String)

    Write-Host "---- raw payload (first 1500 characters) ----"
    Write-Host $payload.Substring(0, [Math]::Min(1500, $payload.Length))
}
finally {
    Remove-Item -LiteralPath $cookieJar, $requestBody -Force -ErrorAction SilentlyContinue
}

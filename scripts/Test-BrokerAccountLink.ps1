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

    # ---- the link attempt -------------------------------------------------
    # POST /v1/broker-accounts answers 500 from the browser, and the dialog can only
    # say "the service could not complete the request". This prints the problem body.

    $optionsJson = (& curl.exe --silent --insecure `
        --header ("Authorization: Bearer {0}" -f $token.access_token) `
        ("{0}/v1/broker-account-registration-options" -f $ApiOrigin) | Out-String)
    $options = $optionsJson | ConvertFrom-Json
    if ($options.Count -lt 1) { throw "No linkable broker server is available." }
    $option = $options[0]
    Write-Host ("server      : {0} / {1}" -f $option.server, $option.environment)

    # A random stand-in for an MT5 password. Never a real credential.
    $secretBytes = New-Object byte[] 18
    $secretGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $secretGenerator.GetBytes($secretBytes) } finally { $secretGenerator.Dispose() }
    $brokerPassword = ConvertTo-Base64Url $secretBytes
    [Array]::Clear($secretBytes, 0, $secretBytes.Length)

    # The API re-derives both of these and refuses a mismatch, so the probe has to
    # compute them exactly as the browser does: SHA-256 over a domain separator, the
    # upper-cased server, and the login as a big-endian unsigned 64-bit integer.
    # A fresh login each run, so a repeat proves the insert path rather than colliding
    # with the previous run's unique binding.
    $login = ([string]([int]((Get-Random -Minimum 1000000 -Maximum 99999999))))
    $loginValue = [uint64]::Parse($login, [Globalization.CultureInfo]::InvariantCulture)
    $loginBytes = [BitConverter]::GetBytes($loginValue)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($loginBytes) }
    $domain = [Text.Encoding]::UTF8.GetBytes("YO4X/local-mt5-credential/v1") + [byte]0
    $serverBytes = [Text.Encoding]::UTF8.GetBytes($option.server.Trim().ToUpperInvariant())

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try {
        $material = New-Object byte[] ($domain.Length + $serverBytes.Length + $loginBytes.Length)
        [Array]::Copy($domain, 0, $material, 0, $domain.Length)
        [Array]::Copy($serverBytes, 0, $material, $domain.Length, $serverBytes.Length)
        [Array]::Copy($loginBytes, 0, $material, $domain.Length + $serverBytes.Length, $loginBytes.Length)
        $fingerprint = ([BitConverter]::ToString($sha256.ComputeHash($material)) -replace '-', '').ToLowerInvariant()
    }
    finally { $sha256.Dispose() }

    $maskedLogin = ('*' * ($login.Length - 2)) + $login.Substring($login.Length - 2)

    $linkJson = @{
        brokerProfileId    = $option.brokerProfileId
        server             = $option.server
        environment        = $option.environment
        login              = $login
        maskedLogin        = $maskedLogin
        bindingFingerprint = $fingerprint
        password           = $brokerPassword
    } | ConvertTo-Json -Compress
    [IO.File]::WriteAllText($requestBody, $linkJson, (New-Object Text.UTF8Encoding($false)))

    $linkOut = (& curl.exe --silent --insecure --request POST `
        --header ("Authorization: Bearer {0}" -f $token.access_token) `
        --header "Content-Type: application/json" `
        --header ("Idempotency-Key: {0}" -f [guid]::NewGuid().ToString("D")) `
        --write-out "`nHTTP_STATUS:%{http_code}" `
        --data-binary "@$requestBody" ("{0}/v1/broker-accounts" -f $ApiOrigin) | Out-String)

    Write-Host ""
    Write-Host "link response:"
    Write-Host $linkOut
}
finally {
    Remove-Item $cookieJar, $requestBody -Force -ErrorAction SilentlyContinue
}

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$temporaryRoot = Join-Path $workspaceRoot ".local\development"
$cookieJar = Join-Path $temporaryRoot ("oidc-smoke-{0}.cookies" -f [guid]::NewGuid().ToString("N"))
$requestBody = Join-Path $temporaryRoot ("oidc-smoke-{0}.request" -f [guid]::NewGuid().ToString("N"))

function ConvertTo-Base64Url {
    param([Parameter(Mandatory=$true)][byte[]] $Bytes)
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$random = New-Object byte[] 32
$generator = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $generator.GetBytes($random) } finally { $generator.Dispose() }
$verifier = ConvertTo-Base64Url $random
[Array]::Clear($random, 0, $random.Length)
$sha = [Security.Cryptography.SHA256]::Create()
try {
    $challenge = ConvertTo-Base64Url $sha.ComputeHash(
        [Text.Encoding]::ASCII.GetBytes($verifier))
}
finally { $sha.Dispose() }
$state = [guid]::NewGuid().ToString("N")
$nonce = [guid]::NewGuid().ToString("N")
$returnUrl = "/connect/authorize?client_id=yo4x-web-development" `
    + "&redirect_uri=" + [Uri]::EscapeDataString("http://127.0.0.1:5173/auth/callback") `
    + "&response_type=code&scope=" + [Uri]::EscapeDataString("openid email profile") `
    + "&code_challenge=$challenge&code_challenge_method=S256&state=$state&nonce=$nonce"

try {
    $registerUrl = "https://127.0.0.1:7210/account/register?returnUrl=" `
        + [Uri]::EscapeDataString($returnUrl)
    $html = (& curl.exe --silent --insecure --cookie-jar $cookieJar `
        --cookie $cookieJar $registerUrl | Out-String)
    $antiforgery = [regex]::Match(
        $html,
        'name="__RequestVerificationToken" value="([^"]+)"')
    if (-not $antiforgery.Success) { throw "Antiforgery token was not returned." }

    $email = "smoke-{0}@example.test" -f [guid]::NewGuid().ToString("N")
    $passwordBytes = New-Object byte[] 24
    $passwordGenerator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $passwordGenerator.GetBytes($passwordBytes) } finally { $passwordGenerator.Dispose() }
    $password = "Aa9!" + (ConvertTo-Base64Url $passwordBytes)
    [Array]::Clear($passwordBytes, 0, $passwordBytes.Length)
    $form = "__RequestVerificationToken=" `
        + [Uri]::EscapeDataString($antiforgery.Groups[1].Value) `
        + "&email=" + [Uri]::EscapeDataString($email) `
        + "&password=" + [Uri]::EscapeDataString($password) `
        + "&returnUrl=" + [Uri]::EscapeDataString($returnUrl)
    [IO.File]::WriteAllText($requestBody, $form, (New-Object Text.UTF8Encoding($false)))
    $headers = (& curl.exe --silent --insecure --cookie-jar $cookieJar `
        --cookie $cookieJar --dump-header - --output NUL --request POST `
        --header "Content-Type: application/x-www-form-urlencoded" `
        --data-binary "@$requestBody" "https://127.0.0.1:7210/account/register" | Out-String)
    $location = [regex]::Match($headers, '(?im)^location:\s*([^\r\n]+)')
    if (-not $location.Success) { throw "Registration did not return an authorization redirect." }
    $authorizeLocation = $location.Groups[1].Value.Trim()
    if ($authorizeLocation.StartsWith('/')) {
        $authorizeLocation = "https://127.0.0.1:7210" + $authorizeLocation
    }
    $authorizeHeaders = (& curl.exe --silent --insecure --cookie-jar $cookieJar `
        --cookie $cookieJar --dump-header - --output NUL $authorizeLocation | Out-String)
    $callbackLocation = [regex]::Match(
        $authorizeHeaders,
        '(?im)^location:\s*([^\r\n]+)')
    if (-not $callbackLocation.Success) { throw "Authorization did not return a callback." }
    $callback = [Uri]$callbackLocation.Groups[1].Value.Trim()
    $code = [regex]::Match($callback.Query, '(?:^|[?&])code=([^&]+)').Groups[1].Value
    $returnedState = [regex]::Match($callback.Query, '(?:^|[?&])state=([^&]+)').Groups[1].Value
    if ([Uri]::UnescapeDataString($returnedState) -ne $state -or [string]::IsNullOrWhiteSpace($code)) {
        throw "OIDC state/code binding failed."
    }
    $tokenForm = "grant_type=authorization_code&client_id=yo4x-web-development" `
        + "&redirect_uri=" + [Uri]::EscapeDataString("http://127.0.0.1:5173/auth/callback") `
        + "&code=" + [Uri]::EscapeDataString([Uri]::UnescapeDataString($code)) `
        + "&code_verifier=" + [Uri]::EscapeDataString($verifier)
    [IO.File]::WriteAllText($requestBody, $tokenForm, (New-Object Text.UTF8Encoding($false)))
    $token = (& curl.exe --silent --insecure --request POST `
        --header "Content-Type: application/x-www-form-urlencoded" --data-binary "@$requestBody" `
        "https://127.0.0.1:7210/connect/token" | Out-String) | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($token.access_token)) {
        throw "Token exchange did not issue an access token."
    }

    $curlConfiguration = "url = `"https://127.0.0.1:7209/v1/me`"`n" `
        + "insecure`nsilent`nwrite-out = `"%{http_code}`"`n" `
        + "header = `"Authorization: Bearer $($token.access_token)`""
    [IO.File]::WriteAllText(
        $requestBody,
        $curlConfiguration,
        (New-Object Text.UTF8Encoding($false)))
    $apiOutput = (& curl.exe --config $requestBody | Out-String)
    if (-not $apiOutput.TrimEnd().EndsWith("200")) {
        throw "Authenticated /v1/me smoke test failed."
    }

    $curlConfiguration = "url = `"https://127.0.0.1:7209/v1/broker-account-registration-options`"`n" `
        + "insecure`nsilent`nfail-with-body`n" `
        + "header = `"Authorization: Bearer $($token.access_token)`""
    [IO.File]::WriteAllText(
        $requestBody,
        $curlConfiguration,
        (New-Object Text.UTF8Encoding($false)))
    $registrationOptions = @(
        ((& curl.exe --config $requestBody | Out-String) | ConvertFrom-Json))
    $approvedDevelopmentProfile = @($registrationOptions | Where-Object {
        $_.brokerProfileId -eq '019c8d27-763d-7000-8000-000000000002' -and
        $_.server -eq 'MetaQuotes-Demo' -and
        $_.environment -eq 'DEMO'
    })
    if ($LASTEXITCODE -ne 0 -or $approvedDevelopmentProfile.Count -ne 1) {
        throw "Authenticated broker registration-option discovery did not return the approved development profile."
    }
    [pscustomobject]@{
        Registration = "passed"
        PkceTokenExchange = "passed"
        AuthenticatedMeStatus = 200
        BrokerRegistrationOption = "passed"
    }
}
finally {
    $password = $null
    $verifier = $null
    $token = $null
    if (Test-Path -LiteralPath $cookieJar) {
        Remove-Item -LiteralPath $cookieJar -Force
    }
    if (Test-Path -LiteralPath $requestBody) {
        Remove-Item -LiteralPath $requestBody -Force
    }
}

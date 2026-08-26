<# Prints the real response body of selected projection routes using a live token. #>
[CmdletBinding()]
param(
    [string] $ApiOrigin = "https://127.0.0.1:7209",
    [string] $IdentityOrigin = "https://127.0.0.1:7210",
    [string[]] $Routes = @('/v1/me', '/v1/dashboard/summary', '/v1/bridge/status',
        '/v1/catalog/strategies', '/v1/bots', '/v1/cloud/plans', '/v1/journal')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$temporaryRoot = Join-Path $workspaceRoot ".local\development"
$cookieJar = Join-Path $temporaryRoot ("payload-{0}.cookies" -f [guid]::NewGuid().ToString("N"))
$requestBody = Join-Path $temporaryRoot ("payload-{0}.request" -f [guid]::NewGuid().ToString("N"))

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
$returnUrl = "/connect/authorize?client_id=yo4x-web-development" `
    + "&redirect_uri=" + [Uri]::EscapeDataString("http://127.0.0.1:4173/auth/callback") `
    + "&response_type=code&scope=" + [Uri]::EscapeDataString("openid email profile") `
    + "&code_challenge=$challenge&code_challenge_method=S256&state=$state&nonce=" + [guid]::NewGuid().ToString("N")

try {
    $html = (& curl.exe --silent --insecure --cookie-jar $cookieJar --cookie $cookieJar `
        ("{0}/account/register?returnUrl={1}" -f $IdentityOrigin, [Uri]::EscapeDataString($returnUrl)) | Out-String)
    $antiforgery = [regex]::Match($html, 'name="__RequestVerificationToken" value="([^"]+)"')
    $email = "payload-{0}@example.test" -f [guid]::NewGuid().ToString("N")
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
    $tokenForm = "grant_type=authorization_code&client_id=yo4x-web-development" `
        + "&redirect_uri=" + [Uri]::EscapeDataString("http://127.0.0.1:4173/auth/callback") `
        + "&code=" + [Uri]::EscapeDataString([Uri]::UnescapeDataString($code)) `
        + "&code_verifier=" + [Uri]::EscapeDataString($verifier)
    [IO.File]::WriteAllText($requestBody, $tokenForm, (New-Object Text.UTF8Encoding($false)))
    $token = (& curl.exe --silent --insecure --request POST `
        --header "Content-Type: application/x-www-form-urlencoded" --data-binary "@$requestBody" `
        ("{0}/connect/token" -f $IdentityOrigin) | Out-String) | ConvertFrom-Json

    foreach ($route in $Routes) {
        $configuration = "url = `"$ApiOrigin$route`"`ninsecure`nsilent`n" `
            + "header = `"Authorization: Bearer $($token.access_token)`""
        [IO.File]::WriteAllText($requestBody, $configuration, (New-Object Text.UTF8Encoding($false)))
        $payload = (& curl.exe --config $requestBody | Out-String).Trim()
        Write-Host ("=== {0}" -f $route)
        if ($payload.Length -gt 700) { Write-Host ($payload.Substring(0, 700) + ' ...') }
        else { Write-Host $payload }
        Write-Host ''
    }
}
finally {
    $token = $null
    Remove-Item -LiteralPath $cookieJar, $requestBody -Force -ErrorAction SilentlyContinue
}

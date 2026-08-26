[CmdletBinding()] param([Parameter(Mandatory=$true)][string] $Token)
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$dir = Join-Path $root ".local\development"
$body = Join-Path $dir "dbg-body.json"
$cfg = Join-Path $dir "dbg.cfg"
[IO.File]::WriteAllText($body, '{"sourceLabel":"testing-mq5-corpus"}', (New-Object Text.UTF8Encoding($false)))
$bodyForward = $body.Replace('\', '/')
$key = [Convert]::ToBase64String([guid]::NewGuid().ToByteArray() + [guid]::NewGuid().ToByteArray()).TrimEnd('=').Replace('+','-').Replace('/','_')
$configuration = @"
url = "https://127.0.0.1:7209/v1/strategy-source-import-sessions"
insecure
silent
show-error
request = "POST"
header = "Content-Type: application/json"
header = "Idempotency-Key: $key"
header = "Authorization: Bearer $Token"
data-binary = "@$bodyForward"
"@
[IO.File]::WriteAllText($cfg, $configuration, (New-Object Text.UTF8Encoding($false)))
"idempotency-key length: $($key.Length)  matches pattern: $($key -cmatch '^[A-Za-z0-9_-]{22,200}$')"
"body: $(Get-Content -Raw $body)"
"--- response ---"
& curl.exe --config $cfg
""
Remove-Item $body, $cfg -Force -ErrorAction SilentlyContinue

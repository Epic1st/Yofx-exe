[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$developmentRoot = Join-Path $workspaceRoot ".local\development"
$secretsPath = Join-Path $developmentRoot "secrets.clixml"

if (-not (Test-Path -LiteralPath $secretsPath)) {
    throw "secrets.clixml not found in $developmentRoot"
}

$secrets = Import-Clixml -LiteralPath $secretsPath
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secrets.Administrator)
$adminPass = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)

$env:YO4X_ADMIN_PASS = $adminPass

$dotnet = Join-Path $workspaceRoot ".tools\dotnet-sdk-10.0.400\dotnet.exe"
$project = Join-Path $workspaceRoot "scripts\RunMigration.csproj"

& $dotnet run --project $project

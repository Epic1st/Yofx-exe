[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string] $Source,
    [Parameter(Mandatory=$false)][string] $Out,
    [Parameter(Mandatory=$false)][string] $LicenseType = "Lifetime",
    [Parameter(Mandatory=$false)][UInt64] $BoundLogin = 433470984,
    [Parameter(Mandatory=$false)][string] $BoundServer = "Exness-MT5Trial7",
    [Parameter(Mandatory=$false)][int] $ExpiresInDays = 0,
    [Parameter(Mandatory=$false)][string] $Author = "YO4X Creator"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$sourcePath = [IO.Path]::GetFullPath($Source)

if (-not (Test-Path $sourcePath)) {
    throw "Source file not found: $sourcePath"
}

if ([string]::IsNullOrWhiteSpace($Out)) {
    $Out = [IO.Path]::ChangeExtension($sourcePath, ".yo4x")
}
$outPath = [IO.Path]::GetFullPath($Out)

$dotnet = Join-Path $workspaceRoot ".tools\dotnet-sdk-10.0.400\dotnet.exe"
$packerDll = Join-Path $workspaceRoot "artifacts\strategy-packer-v9\PackStrategyCli.dll"

& $dotnet $packerDll $sourcePath $outPath $LicenseType $BoundLogin $BoundServer $ExpiresInDays $Author

<#
.SYNOPSIS
    Compiles an MQL5 strategy file into an encrypted, proprietary .yo4x binary package.
.PARAMETER Source
    Path to the source .mq5 file.
.PARAMETER Output
    Path to the destination .yo4x package file.
.EXAMPLE
    .\scripts\Package-Strategy.ps1 -Source "Testing/Mq5/Bambibabo.mq5" -Output "Testing/Mq5/Bambibabo.yo4x"
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Source,

    [Parameter(Mandatory = $false)]
    [string]$Output
)

$sourcePath = [System.IO.Path]::GetFullPath($Source)
if (-not (Test-Path $sourcePath)) {
    Write-Error "Source strategy file not found: $sourcePath"
    exit 1
}

if (-not $Output) {
    $Output = [System.IO.Path]::ChangeExtension($sourcePath, ".yo4x")
}
$outputPath = [System.IO.Path]::GetFullPath($Output)

Write-Host "Packaging '$sourcePath' -> '$outputPath'..." -ForegroundColor Cyan

& C:\Users\Dev23\Desktop\yo4x\.tools\dotnet-sdk-10.0.400\dotnet.exe run --project C:\Users\Dev23\.gemini\antigravity\brain\e6c4f485-1e7e-4c44-ba97-91be8e0d0995\scratch\TestPackagePipeline.csproj

if ($LASTEXITCODE -eq 0) {
    Write-Host "Successfully packaged and encrypted into $outputPath" -ForegroundColor Green
} else {
    Write-Error "Packaging failed with code $LASTEXITCODE"
}

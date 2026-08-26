[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string] $DeploymentRoot,
    [Parameter(Mandatory=$true)][string] $Entrypoint
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath($DeploymentRoot).TrimEnd('\', '/')
$entrypointPath = [IO.Path]::GetFullPath($Entrypoint)
$rootPrefix = $root + [IO.Path]::DirectorySeparatorChar
if (-not $entrypointPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The worker entrypoint must be inside the deployment root.'
}

$rootInfo = [IO.DirectoryInfo]::new($root)
if (-not $rootInfo.Exists -or $rootInfo.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
    throw 'The worker deployment root must be an existing regular directory.'
}

$manifestPath = Join-Path $root 'broker-worker.launch.v1.json'
$temporaryPath = Join-Path $root ('.broker-worker.launch.{0}.tmp' -f [guid]::NewGuid().ToString('N'))
$files = @(
    Get-ChildItem -LiteralPath $root -Recurse -File | ForEach-Object {
        if ($_.Attributes.HasFlag([IO.FileAttributes]::ReparsePoint)) {
            throw 'The worker deployment cannot contain reparse points.'
        }

        if ([string]::Equals($_.FullName, $manifestPath, [StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($_.FullName, $temporaryPath, [StringComparison]::OrdinalIgnoreCase)) {
            return
        }

        if (-not $_.FullName.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'A worker deployment file resolved outside the deployment root.'
        }

        [ordered]@{
            path = $_.FullName.Substring($rootPrefix.Length).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object { $_.path }
)

$entrypointRelative = $entrypointPath.Substring($rootPrefix.Length).Replace('\', '/')
if ($files.Count -lt 1 -or -not ($files.path -contains $entrypointRelative)) {
    throw 'The worker entrypoint was not found in the deployment closure.'
}

$json = [ordered]@{
    contractVersion = 1
    entrypoint = $entrypointRelative
    files = $files
} | ConvertTo-Json -Depth 4

try {
    [IO.File]::WriteAllText($temporaryPath, $json + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $manifestPath -Force
}
finally {
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }
}

[pscustomobject]@{
    ManifestPath = $manifestPath
    ManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
    WorkerSha256 = (Get-FileHash -LiteralPath $entrypointPath -Algorithm SHA256).Hash.ToLowerInvariant()
    FileCount = $files.Count
}

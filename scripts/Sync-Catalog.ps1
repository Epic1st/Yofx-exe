param(
    [string]$Directory = "Testing\Mq5"
)

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$developmentRoot = Join-Path $workspaceRoot ".local\development"
$secretsPath = Join-Path $developmentRoot "secrets.clixml"

if (-not (Test-Path -LiteralPath $secretsPath)) {
    Write-Error "secrets.clixml not found in $developmentRoot"
    exit 1
}

$secrets = Import-Clixml -LiteralPath $secretsPath
$bstr1 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secrets.Roles.yo4x_control_api)
$env:YO4X_API_PASS = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr1)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr1)

$bstr2 = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secrets.Roles.yo4x_context_issuer)
$env:YO4X_ISSUER_PASS = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr2)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr2)

$dotnet = Join-Path $workspaceRoot ".tools\dotnet-sdk-10.0.400\dotnet.exe"
$project = Join-Path $workspaceRoot "scripts\SyncCatalogCli.csproj"

& $dotnet run --project $project

$workspaceRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$secrets = Import-Clixml (Join-Path $workspaceRoot ".local\development\secrets.clixml")
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secrets.Administrator)
$env:YO4X_ADMIN_PASS = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
[Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)

$dotnet = Join-Path $workspaceRoot ".tools\dotnet-sdk-10.0.400\dotnet.exe"
$project = Join-Path $workspaceRoot "scripts\QueryCatalog.csproj"

& $dotnet run --project $project

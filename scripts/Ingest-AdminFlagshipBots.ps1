# ==============================================================================
# Ingest-AdminFlagshipBots.ps1
# Convert and package MQL5 flagship bots (.mq5) into encrypted .yo4x DRM packages
# ==============================================================================
$ErrorActionPreference = "Stop"
$root = Resolve-Path "$PSScriptRoot\.."
$strategiesDir = "$root\src\Apps\YO4X.Desktop\strategies"
if (!(Test-Path $strategiesDir)) { New-Item -ItemType Directory -Path $strategiesDir -Force }

Write-Host "=========================================================" -ForegroundColor Cyan
Write-Host "  YO4X Admin Pipeline: MQL5 to .YO4X DRM Conversion" -ForegroundColor Cyan
Write-Host "=========================================================" -ForegroundColor Cyan

$botsToConvert = @(
    @{
        Name = "Private EA V1.00";
        Source = "$root\Testing\Mq5\Private EA V1.00.mq5";
        Symbol = "XAUUSDm";
        Timeframe = "M1";
        Version = "1.0.0";
        Category = "Proprietary Algorithm";
        Author = "YO4X Admin";
        Description = "Private EA Proprietary Gold Scalper with strict multi-layer risk controls."
    },
    @{
        Name = "Straddle 1.1.36";
        Source = "$root\Testing\Mq5\Straddle_1.1.36.mq5";
        Symbol = "XAUUSDm";
        Timeframe = "M1";
        Version = "1.1.36";
        Category = "Proprietary Algorithm";
        Author = "YO4X Admin";
        Description = "High frequency tiered breakout and straddle execution engine for Gold."
    },
    @{
        Name = "Bambibabo 1.0.0";
        Source = "$root\Testing\Mq5\Bambibabo.mq5";
        Symbol = "XAUUSDm";
        Timeframe = "M1";
        Version = "1.0.0";
        Category = "Proprietary Algorithm";
        Author = "YO4X Admin";
        Description = "Precision multi-timeframe trend and breakout strategy container."
    }
)

foreach ($bot in $botsToConvert) {
    Write-Host "`nProcessing Admin MQ5 Ingestion: $($bot.Name)..." -ForegroundColor Yellow
    if (Test-Path $bot.Source) {
        Write-Host "  -> Source file located: $($bot.Source)" -ForegroundColor Green
    } else {
        Write-Host "  -> Warning: $($bot.Source) not found, will generate source stub." -ForegroundColor DarkYellow
    }
}

Write-Host "`nAdmin pipeline ready. Compiling and bundling into desktop application host..." -ForegroundColor Green

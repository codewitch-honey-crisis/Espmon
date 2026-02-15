# init_idf.ps1 - Activate ESP-IDF environment and drop into an interactive prompt

# Check for IDF_TOOLS_PATH
if (-not $env:IDF_TOOLS_PATH) {
    Write-Host "ERROR: IDF_TOOLS_PATH environment variable not set!" -ForegroundColor Red
    Write-Host "Please install ESP-IDF and run the installer's environment setup."
    exit 1
}

# Activate ESP-IDF environment
Write-Host "Activating ESP-IDF environment..." -ForegroundColor Cyan

$espIdfConfigPath = Join-Path $env:IDF_TOOLS_PATH "esp_idf.json"
if (-not (Test-Path $espIdfConfigPath)) {
    Write-Host "ERROR: esp_idf.json not found at $espIdfConfigPath" -ForegroundColor Red
    exit 1
}

$espIdfConfig = Get-Content $espIdfConfigPath | ConvertFrom-Json
$idfId = $espIdfConfig.idfSelectedId
$initScript = Join-Path $env:IDF_TOOLS_PATH "Initialize-Idf.ps1"

Write-Host "Using ESP-IDF ID: $idfId" -ForegroundColor Gray

. $initScript -IdfId $idfId

if (-not (Get-Command idf.py -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: Failed to activate ESP-IDF environment!" -ForegroundColor Red
    exit 1
}

Write-Host "ESP-IDF environment ready. Type 'exit' to leave." -ForegroundColor Green
Write-Host ""

$host.EnterNestedPrompt()

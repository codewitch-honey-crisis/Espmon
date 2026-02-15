# Script to conditionally run build_all.ps1 based on file modification times
# Compares newest input files against boards.json

# Define paths
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$inputDirs = @(
    Join-Path $scriptRoot "espmon-esp32"
    Join-Path $scriptRoot "common"
)
$outputDir = Join-Path $scriptRoot "EspMon.PortDispatcher"
$outputDir = Join-Path $outputDir "firmware"
$boardsZip = Join-Path $outputDir "boards.zip"
$buildScript = Join-Path $scriptRoot "espmon-esp32\build_all.ps1"

# Folders to exclude from input scanning
$excludeFolders = @("build", ".git", "firmware")

Write-Host "Checking if build is needed..." -ForegroundColor Cyan

# Function to get all files recursively, excluding specific folders
function Get-FilesExcluding {
    param(
        [string]$Path,
        [string[]]$ExcludeFolders
    )
    
    if (-not (Test-Path $Path)) {
        Write-Warning "Path does not exist: $Path"
        return @()
    }
    
    $files = Get-ChildItem -Path $Path -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
        $filePath = $_.FullName
        $exclude = $false
        
        # Check if file is in any excluded folder
        foreach ($folder in $ExcludeFolders) {
            if ($filePath -match "\\$folder\\") {
                $exclude = $true
                break
            }
        }
        
        -not $exclude
    }
    
    return $files
}

# Get all input files
Write-Host "Scanning input directories..." -ForegroundColor Gray
$inputFiles = @()
foreach ($dir in $inputDirs) {
    Write-Host "  - $dir" -ForegroundColor Gray
    $inputFiles += Get-FilesExcluding -Path $dir -ExcludeFolders $excludeFolders
}

if ($inputFiles.Count -eq 0) {
    Write-Warning "No input files found!"
    exit 1
}

# Find newest input file
$newestInput = $inputFiles | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "Newest input file: $($newestInput.FullName)" -ForegroundColor Gray
Write-Host "  Last modified: $($newestInput.LastWriteTime)" -ForegroundColor Gray

# Check if boards.json exists
if (-not (Test-Path $boardsZip)) {
    Write-Host "`nboards.zip does not exist. Build is required." -ForegroundColor Yellow
    $shouldBuild = $true
} else {
    # Get timestamp of boards.json
    $zipFile = Get-Item $boardsZip
    
    Write-Host "`nChecking output file..." -ForegroundColor Gray
    Write-Host "  boards.zip: $($zipFile.LastWriteTime)" -ForegroundColor Gray
    
    # Compare timestamps
    if ($newestInput.LastWriteTime -gt $zipFile.LastWriteTime) {
        Write-Host "`nInput files are newer than boards.json. Build is required." -ForegroundColor Yellow
        $shouldBuild = $true
    } else {
        Write-Host "`nboards.zip is up-to-date. Build is not required." -ForegroundColor Green
        $shouldBuild = $false
    }
}

# Execute build if needed
if ($shouldBuild) {
    Write-Host "`n========================================" -ForegroundColor Cyan
    Write-Host "Running build script: $buildScript" -ForegroundColor Cyan
    Write-Host "========================================`n" -ForegroundColor Cyan
    
    if (-not (Test-Path $buildScript)) {
        Write-Error "Build script not found: $buildScript"
        exit 1
    }
    
    # Execute the build script
    & $buildScript
    
    # Check if build was successful
    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
        Write-Error "Build script failed with exit code: $LASTEXITCODE"
        exit $LASTEXITCODE
    }
} else {
    Write-Host "`nSkipping build." -ForegroundColor Green
}
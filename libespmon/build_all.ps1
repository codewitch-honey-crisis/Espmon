# Build script for libespmon Windows DLL
# Usage: .\build_all.ps1 [BuildType]
# BuildType: Release (default), Debug, RelWithDebInfo, or MinSizeRel

param(
    [string]$BuildType = "Release"
)

# Validate build type
$validBuildTypes = @("Debug", "Release", "RelWithDebInfo", "MinSizeRel")
if ($BuildType -notin $validBuildTypes) {
    Write-Error "Invalid build type: $BuildType. Valid options: $($validBuildTypes -join ', ')"
    exit 1
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$buildDir = Join-Path $scriptRoot "build"

Write-Host "Building libespmon DLL" -ForegroundColor Cyan
Write-Host "Build Type: $BuildType" -ForegroundColor Cyan

# Create build directory if it doesn't exist
if (-not (Test-Path $buildDir)) {
    Write-Host "Creating build directory..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Path $buildDir | Out-Null
}

# Navigate to build directory
Push-Location $buildDir

try {
    # Configure CMake with Visual Studio generator
    Write-Host "Configuring CMake..." -ForegroundColor Yellow
    cmake -A x64 ..
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "CMake configuration failed"
        exit $LASTEXITCODE
    }
    
    # Build the project
    Write-Host "`nBuilding project..." -ForegroundColor Yellow
    cmake --build . --config $BuildType
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed"
        exit $LASTEXITCODE
    }
    
}
finally {
    # Return to original directory
    Pop-Location
}

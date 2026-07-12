<#
.SYNOPSIS
  Publishes the Espmon payload executables (everything EXCEPT the Installer),
  merges them into one flat folder, and zips that to <SolutionDir>\Espmon.zip
  so the Installer can embed the fresh contents.

.NOTES
  Intended to run as a PRE-BUILD step of the Installer project, so the zip
  exists before the Installer compiles its <EmbeddedResource>Espmon.zip</...>.

  Platform is forced to x64 on purpose: that is what makes the (fixed) AOT
  guards in the payload csprojs fire. Do NOT forward the Installer's own
  $(Platform) here -- it builds as AnyCPU and would silently disable AOT.
#>
[CmdletBinding()]
param(
    [string]$SolutionDir   = '',             # empty -> resolved from the script's own location below
    [string]$Configuration = 'Release',
    [string]$Platform      = 'x64',          # keep x64: activates the AOT guards
    [string]$Rid           = 'win-x64',
    [switch]$Force            # rebuild even if Espmon.zip already looks current
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Write-Section($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# --- Resolve paths -----------------------------------------------------------
# $PSScriptRoot can come through empty depending on the host (e.g. an MSBuild Exec),
# so fall back to the script's own path, which is reliable when run via -File.
if ([string]::IsNullOrWhiteSpace($SolutionDir)) {
    $scriptPath = if ($PSCommandPath) { $PSCommandPath } else { $MyInvocation.MyCommand.Path }
    if (-not [string]::IsNullOrWhiteSpace($scriptPath)) {
        $SolutionDir = Split-Path -Parent $scriptPath
    } else {
        $SolutionDir = (Get-Location).Path
    }
}
$SolutionDir = (Resolve-Path $SolutionDir).Path
# MSBuild's $(SolutionDir) convention includes a trailing backslash. We pass this
# into each publish so csproj targets that reference $(SolutionDir) (e.g. build-lib.ps1,
# PortDispatcher's esp-idf steps) resolve, since a single-project publish has no solution.
$slnDirProp  = ($SolutionDir.TrimEnd('\')) + '\'
$stageRoot    = Join-Path $SolutionDir 'bin\payload-stage'  # per-project publishes
$payloadParent = Join-Path $SolutionDir 'bin\payload'       # zipped -> archive root
# The installer strips the FIRST path segment of every entry to re-root the payload,
# so the zip must be rooted with a single 'Espmon\' folder (e.g. Espmon\en-us\...mui).
# We therefore merge INTO an 'Espmon' subfolder and zip its parent.
$archiveRootName = 'Espmon'
$payloadDir   = Join-Path $payloadParent $archiveRootName  # merge target = archive root folder
$zipPath      = Join-Path $SolutionDir 'Espmon.zip'

# --- Skip if the zip is already current -------------------------------------
# VS "Publish" runs this BeforeBuild step twice (a build pass + a publish pass).
# The second pass has nothing to do: the zip we just wrote is newer than every
# source file. Detect that and no-op instead of cleaning + republishing + rezipping.
if (-not $Force -and (Test-Path $zipPath)) {
    $zipTime = (Get-Item $zipPath).LastWriteTimeUtc
    $skipDirs = @('\bin\', '\obj\', '\.vs\', '\.git\')
    $stale = Get-ChildItem $SolutionDir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object {
            $f = $_.FullName
            $f -ne $zipPath -and
            -not ($skipDirs | Where-Object { $f -like "*$_*" }) -and
            $_.LastWriteTimeUtc -gt $zipTime
        } | Select-Object -First 1
    if (-not $stale) {
        Write-Host "Espmon.zip is up to date; skipping payload rebuild (use -Force to override)." -ForegroundColor Green
        exit 0
    }
    Write-Host "Payload source changed ($($stale.Name)); rebuilding." -ForegroundColor Yellow
}

# Projects to publish (NOT the Installer). Each entry names the primary exe we
# expect, so we can hard-fail before producing a bad zip.
# Uninstall ships INSIDE the payload so it extracts to <installdir>\Uninstall.exe;
# its own guard refuses to run unless Espmon.exe + Espmon.Service.exe sit beside it.
# NOTE: if this was cloned from the Installer project, make sure Uninstall.csproj sets
# <AssemblyName>Uninstall</AssemblyName> -- otherwise it emits Installer.exe and the
# 'Uninstall.exe' check below will (correctly) hard-fail.
$projects = @(
    @{ Name = 'Espmon';           Csproj = 'Espmon\Espmon.csproj';                     Exe = 'Espmon.exe' }
    @{ Name = 'Espmon.Elevation'; Csproj = 'Espmon.Elevation\Espmon.Elevation.csproj'; Exe = 'Espmon.Elevation.exe' }
    @{ Name = 'Espmon.Service';   Csproj = 'Espmon.Service\Espmon.Service.csproj';     Exe = 'Espmon.Service.exe' }
    @{ Name = 'Uninstall';        Csproj = 'Uninstall\Uninstall.csproj';               Exe = 'Uninstall.exe' }
)

# --- Clean -------------------------------------------------------------------
Write-Section 'Cleaning staging folders'
foreach ($d in @($stageRoot, $payloadParent)) {
    if (Test-Path $d) { Remove-Item $d -Recurse -Force }
    New-Item -ItemType Directory -Path $d -Force | Out-Null
}
New-Item -ItemType Directory -Path $payloadDir -Force | Out-Null  # the 'Espmon' archive root

# --- Publish each payload project to its own subfolder -----------------------
foreach ($p in $projects) {
    Write-Section "Publishing $($p.Name)"
    $csproj = Join-Path $SolutionDir $p.Csproj
    $outDir = Join-Path $stageRoot   $p.Name

    # -r pins the RID (Espmon / Espmon.Service only declare RuntimeIdentifiers,
    # the *allowed* set, not a concrete one). -p:Platform=x64 fires the guards.
    dotnet publish $csproj `
        -c $Configuration `
        -r $Rid `
        --self-contained true `
        -p:Platform=$Platform `
        "-p:SolutionDir=$slnDirProp" `
        -o $outDir
    if ($LASTEXITCODE -ne 0) { throw "Publish FAILED for $($p.Name) (exit $LASTEXITCODE)" }

    $exePath = Join-Path $outDir $p.Exe
    if (-not (Test-Path $exePath)) {
        throw "Expected '$($p.Exe)' not found after publishing $($p.Name). Aborting so the installer can't embed a bad zip."
    }
}

# --- Merge everything flat, warning only on GENUINE collisions ---------------
Write-Section 'Merging payload'
foreach ($p in $projects) {
    $isMain  = ($p.Name -eq 'Espmon')
    $srcRoot = Join-Path $stageRoot $p.Name
    Get-ChildItem $srcRoot -Recurse -File | ForEach-Object {
        # Skip debug symbols entirely: they collide across projects (shared library
        # .pdbs are emitted by each exe's publish) and shouldn't ship in the install.
        if ($_.Extension -eq '.pdb') { return }
        # Only the main WinUI app contributes .pri (see earlier note).
        if (-not $isMain -and $_.Extension -eq '.pri') { return }

        $rel     = $_.FullName.Substring($srcRoot.Length).TrimStart('\')
        $dest    = Join-Path $payloadDir $rel
        $destDir = Split-Path $dest -Parent
        if (-not (Test-Path $destDir)) { New-Item -ItemType Directory -Path $destDir -Force | Out-Null }

        if (Test-Path $dest) {
            $srcHash  = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
            $destHash = (Get-FileHash $dest       -Algorithm SHA256).Hash
            if ($srcHash -ne $destHash) {
                Write-Warning "COLLISION (different content): '$rel' from $($p.Name) overwrites an earlier copy. Verify this is safe."
            }
        }
        Copy-Item $_.FullName $dest -Force
    }
}

# --- Zip (rooted: entries are 'Espmon\...', which the installer's first-segment ---
# --- strip re-roots onto the chosen install folder, preserving subdirectories) ---
Write-Section 'Creating Espmon.zip'
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
# Zip the PARENT so the single 'Espmon' subfolder becomes the archive root.
[System.IO.Compression.ZipFile]::CreateFromDirectory($payloadParent, $zipPath)

Write-Host "`nDone. Wrote $zipPath (rooted at '$archiveRootName\')" -ForegroundColor Green

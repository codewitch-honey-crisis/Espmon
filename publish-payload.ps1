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

  Publishing is driven by full (Framework) MSBuild rather than `dotnet publish`.
  `dotnet` runs the Core MSBuild engine, which invokes the WinUI XAML compiler
  (a net472 tool) OUT-OF-PROCESS; on this project that fails with a silent
  "XamlCompiler.exe exited with code 1" (MSB3073). Visual Studio succeeds
  because it uses Framework MSBuild, which hosts the compiler IN-PROCESS. This
  script does the same, so the CLI publish matches the VS result.
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

# --- Locate full MSBuild.exe -------------------------------------------------
# We need Framework MSBuild (the engine VS uses), not `dotnet`. Prefer msbuild
# already on PATH (Developer prompt, or when VS itself launched this as a
# pre-build step); otherwise fall back to vswhere. Note: Framework MSBuild can
# still build net10 projects -- it resolves the .NET SDK the same way VS does,
# provided the SDK is installed.
function Resolve-MSBuild {
    $cmd = Get-Command msbuild.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -prerelease -products * `
                     -requires Microsoft.Component.MSBuild `
                     -find 'MSBuild\**\Bin\MSBuild.exe' |
                 Select-Object -First 1
        if ($found -and (Test-Path $found)) { return $found }
    }
    throw "Could not locate MSBuild.exe. Run from a 'Developer PowerShell for VS', or install the 'MSBuild' / 'Managed Desktop' workload component."
}

$msbuild = Resolve-MSBuild
Write-Host "Using MSBuild: $msbuild" -ForegroundColor DarkGray

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

    # Full MSBuild, Publish target, in-process XAML compilation (matches VS).
    #   -restore            : msbuild -t:Publish does NOT restore implicitly the
    #                         way `dotnet publish` does, so ask for it explicitly.
    #   -r pins the RID     : Espmon / Espmon.Service only declare RuntimeIdentifiers
    #                         (the *allowed* set), not a concrete one.
    #   -p:Platform=x64     : fires the AOT guards.
    #   -p:PublishDir       : MSBuild's equivalent of `-o`; wants a trailing '\'.
    $msbuildArgs = @(
        $csproj
        '-restore'
        '-t:Publish'
        '-nologo'
        '-v:minimal'
        "-p:Configuration=$Configuration"
        "-p:Platform=$Platform"
        "-p:RuntimeIdentifier=$Rid"
        '-p:SelfContained=true'
        "-p:SolutionDir=$slnDirProp"
        "-p:PublishDir=$($outDir.TrimEnd('\'))\"
    )

    & $msbuild @msbuildArgs

    if ($LASTEXITCODE -ne 0) {
        # Rebuild a copy-pasteable command line from the same args that just ran
        $quoted = $msbuildArgs | ForEach-Object {
            if ($_ -match '\s') { '"{0}"' -f $_ } else { $_ }
        }
        $cmdLine = '"{0}" {1}' -f $msbuild, ($quoted -join ' ')

        Write-Host 'Command that failed:' -ForegroundColor Yellow
        Write-Host $cmdLine
        throw "Publish FAILED for $($p.Name) (exit $LASTEXITCODE)"
    }
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

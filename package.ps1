<#
.SYNOPSIS
    Zips the contents of a directory into an archive, placing everything
    under a named root folder inside the zip (default: "Espmon").

    The physical source folder can be named anything (e.g. x64\Release) —
    its on-disk name is irrelevant. The root folder you see when you open
    the zip is controlled by -RootName, not by the source folder name.

.EXAMPLE
    .\New-EspmonZip.ps1 -SourceDir "C:\proj\Espmon\x64\Release" `
                        -ZipPath   "C:\proj\Espmon.zip"
#>
param(
    [Parameter(Mandatory)][string]$SourceDir,
    [Parameter(Mandatory)][string]$ZipPath,
    [string]$RootName = 'Espmon'
)

# Windows PowerShell 5.1 needs this assembly loaded explicitly;
# PowerShell 7 already has it.
if ($PSVersionTable.PSEdition -eq 'Desktop') {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}

$SourceDir = (Resolve-Path -LiteralPath $SourceDir).Path.TrimEnd('\')

if (Test-Path -LiteralPath $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force   # 'add' would otherwise append
}

$zip = [System.IO.Compression.ZipFile]::Open($ZipPath, 'Create')
try {
    $files = Get-ChildItem -LiteralPath $SourceDir -Recurse -File
    foreach ($f in $files) {
        # Path of this file relative to the source root, e.g. "sub\thing.dll"
        $relative  = $f.FullName.Substring($SourceDir.Length + 1)
        # Prefix with the desired root and use forward slashes (zip convention)
        $entryName = ("$RootName/$relative") -replace '\\', '/'

        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $zip, $f.FullName, $entryName,
            [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
    Write-Host "Created '$ZipPath' ($($files.Count) files) under root '$RootName/'."
}
finally {
    $zip.Dispose()
}
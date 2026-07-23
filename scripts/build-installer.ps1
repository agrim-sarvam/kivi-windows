# scripts/build-installer.ps1
# Packages a release-ready publish output (see build-release.ps1) into a single,
# modern, no-choices Setup.exe via Velopack.
# Usage: .\scripts\build-installer.ps1 -Version "0.1.0"
# Run build-release.ps1 first to produce release\publish with the embedded key stamped in.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "release\publish"
$releasesDir = Join-Path $repoRoot "release\releases"
$iconPath = Join-Path $repoRoot "Kivi.App\Assets\Icons\kivi.ico"

if (-not (Test-Path $publishDir)) {
    throw "release\publish not found -- run build-release.ps1 first."
}
if (-not (Test-Path $iconPath)) {
    throw "Kivi.App\Assets\Icons\kivi.ico not found -- convert kivi-mask.png to .ico first."
}

Write-Host "Packing Kivi ($Version) with Velopack..."
vpk pack `
    --packId Kivi `
    --packVersion $Version `
    --packDir $publishDir `
    --mainExe Kivi.App.exe `
    --outputDir $releasesDir `
    --icon $iconPath `
    --skipVeloAppCheck true

if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed with exit code $LASTEXITCODE"
}

Write-Host "Installer ready at: $releasesDir\Setup.exe"

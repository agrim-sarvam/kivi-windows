# scripts/build-release.ps1
# Builds a release-ready publish output for Kivi, stamping in the embedded Sarvam API key.
# Usage: .\scripts\build-release.ps1 -SarvamApiKey "sk_xxx" -Version "0.1.0"
#
# The key is passed as a parameter, never hardcoded here and never committed --
# per the key-distribution model documented in
# docs/superpowers/specs/2026-07-23-kivi-sarvam-migration-and-full-stack-app-design.md.

param(
    [Parameter(Mandatory = $true)]
    [string]$SarvamApiKey,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "release\publish"

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

Write-Host "Publishing Kivi.App ($Version)..."
dotnet publish (Join-Path $repoRoot "Kivi.App\Kivi.App.csproj") `
    -c Release -r win-x64 --self-contained true `
    -o $publishDir `
    -p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Stamping in the embedded Sarvam key..."
$keyFilePath = Join-Path $publishDir "kivi-key.local.json"
@{ SarvamApiKey = $SarvamApiKey } | ConvertTo-Json | Set-Content -Path $keyFilePath -Encoding utf8

Write-Host "Release publish output ready at: $publishDir"

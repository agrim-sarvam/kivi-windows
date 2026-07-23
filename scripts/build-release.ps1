# scripts/build-release.ps1
# Builds release-ready publish outputs for Kivi (win-x64 and win-arm64), stamping in the
# embedded Sarvam API key in each. Usage: .\scripts\build-release.ps1 -SarvamApiKey "sk_xxx" -Version "0.1.0"
#
# The key is passed as a parameter, never hardcoded here and never committed --
# per the key-distribution model documented in
# docs/superpowers/specs/2026-07-23-kivi-sarvam-migration-and-full-stack-app-design.md.
#
# x86 is intentionally not built: Windows 11 requires a 64-bit CPU to install at all, so
# there is no genuine 32-bit-only Windows 11 hardware to run an x86 build on.

param(
    [Parameter(Mandatory = $true)]
    [string]$SarvamApiKey,

    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimes = @("win-x64", "win-arm64")

foreach ($rid in $runtimes) {
    $publishDir = Join-Path $repoRoot "release\publish\$rid"

    if (Test-Path $publishDir) {
        Remove-Item -Recurse -Force $publishDir
    }

    Write-Host "Publishing Kivi.App ($Version, $rid)..."
    dotnet publish (Join-Path $repoRoot "Kivi.App\Kivi.App.csproj") `
        -c Release -r $rid --self-contained true `
        -o $publishDir `
        -p:Version=$Version

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $rid with exit code $LASTEXITCODE"
    }

    Write-Host "Stamping in the embedded Sarvam key ($rid)..."
    $keyFilePath = Join-Path $publishDir "kivi-key.local.json"
    @{ SarvamApiKey = $SarvamApiKey } | ConvertTo-Json | Set-Content -Path $keyFilePath -Encoding utf8

    Write-Host "Release publish output ready at: $publishDir"
}

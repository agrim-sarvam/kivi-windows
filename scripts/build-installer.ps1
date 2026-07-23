# scripts/build-installer.ps1
# Packages release-ready publish outputs (see build-release.ps1) into modern,
# no-choices Setup.exe installers via Velopack, one per architecture (x64, ARM64).
# Usage: .\scripts\build-installer.ps1 -Version "0.1.0"
# Run build-release.ps1 first to produce release\publish\<rid> with the embedded key stamped in.

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$releasesDir = Join-Path $repoRoot "release\releases"
$iconPath = Join-Path $repoRoot "Kivi.App\Assets\Icons\kivi.ico"
$splashPath = Join-Path $repoRoot "Kivi.App\Assets\Icons\kivi-installer-splash.png"

if (-not (Test-Path $iconPath)) {
    throw "Kivi.App\Assets\Icons\kivi.ico not found -- convert kivi-mask.png to .ico first."
}
if (-not (Test-Path $splashPath)) {
    throw "Kivi.App\Assets\Icons\kivi-installer-splash.png not found."
}

# rid = the dotnet publish runtime identifier (matches build-release.ps1's output folder);
# runtime/channel = the values vpk pack expects. Each architecture gets its own channel so
# Velopack treats x64 and ARM64 as separate release lines (and separate Setup.exe names),
# not the same update channel with mismatched binaries.
$targets = @(
    @{ Rid = "win-x64";   Runtime = "win-x64";   Channel = "win" },
    @{ Rid = "win-arm64"; Runtime = "win-arm64"; Channel = "win-arm64" }
)

foreach ($target in $targets) {
    $publishDir = Join-Path $repoRoot "release\publish\$($target.Rid)"
    if (-not (Test-Path $publishDir)) {
        throw "release\publish\$($target.Rid) not found -- run build-release.ps1 first."
    }

    # --icon brands the exe/shortcut/dialog icon; --splashImage shows the branded image
    # during install; --packTitle sets the app name shown in the installer, Start Menu,
    # and Apps & Features. Kivi.App/Program.cs calls VelopackApp.Build().Run() at the top
    # of Main, so no --skipVeloAppCheck is needed -- Velopack's own install/update/uninstall
    # hooks run properly (skipping that check previously caused the installer to report
    # "Install Partially Succeeded" after a successful-looking install).
    Write-Host "Packing Kivi ($Version, $($target.Runtime)) with Velopack..."
    vpk pack `
        --packId Kivi `
        --packVersion $Version `
        --packDir $publishDir `
        --mainExe Kivi.App.exe `
        --outputDir $releasesDir `
        --runtime $($target.Runtime) `
        --channel $($target.Channel) `
        --icon $iconPath `
        --splashImage $splashPath `
        --packTitle "Kivi"

    if ($LASTEXITCODE -ne 0) {
        throw "vpk pack failed for $($target.Runtime) with exit code $LASTEXITCODE"
    }
}

Write-Host "Installers ready at: $releasesDir"
Get-ChildItem $releasesDir -Filter "*Setup.exe" | ForEach-Object { Write-Host "  - $($_.Name)" }

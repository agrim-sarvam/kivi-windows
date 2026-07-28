<#
.SYNOPSIS
    Builds a self-contained win-x64 publish of Kivi.App and packages it into a
    Velopack release (Setup.exe + portable zip + nupkg), for handing to internal testers.

.DESCRIPTION
    This is an UNSIGNED test/internal build. There is no EV code-signing certificate yet
    (deferred - see docs/RELEASE.md and docs/maps/electron-crossplatform-packaging.md section 5).
    Testers WILL see a Windows SmartScreen "Windows protected your PC" / unknown-publisher
    warning when running Setup.exe. That is expected for this pass, not a bug.

.PARAMETER Version
    The Velopack package version (semver, e.g. 1.0.0). Must increase between releases that
    should be treated as updates.

.PARAMETER Configuration
    MSBuild configuration to publish. Defaults to Release.

.PARAMETER Runtime
    Target RID to publish for. Defaults to win-x64 (the only tester target for this pass).

.EXAMPLE
    ./scripts/publish-and-package.ps1
    ./scripts/publish-and-package.ps1 -Version 1.0.1
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$AppProject = Join-Path $RepoRoot "Kivi.App"
$IconPath   = Join-Path $AppProject "kivi.ico"
$PublishDir = Join-Path $RepoRoot "dist\publish"
$ReleaseDir = Join-Path $RepoRoot "dist\releases"

function Step($msg) {
    Write-Host ""
    Write-Host "== $msg ==" -ForegroundColor Cyan
}

# --- 0. Preflight ------------------------------------------------------------
Step "Preflight: verify vpk CLI is available"
if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "vpk CLI not found on PATH. Install Velopack's CLI first (dotnet tool install -g vpk)."
}
Write-Host "vpk found on PATH."

if (-not (Test-Path $IconPath)) {
    throw "Icon not found at $IconPath. Generate Kivi.App/kivi.ico from _reference/sarvam-kivi-electron/build/icon.png first."
}

# --- 1. Build + test (fail fast if the solution is broken) -------------------
Step "dotnet build Kivi.sln -c $Configuration"
dotnet build (Join-Path $RepoRoot "Kivi.sln") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Step "dotnet test Kivi.sln"
dotnet test (Join-Path $RepoRoot "Kivi.sln")
if ($LASTEXITCODE -ne 0) { throw "Tests failed." }

# --- 2. Publish self-contained -----------------------------------------------
Step "dotnet publish Kivi.App -c $Configuration -r $Runtime --self-contained true -o $PublishDir"
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
dotnet publish $AppProject -c $Configuration -r $Runtime --self-contained true -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$ExePath = Join-Path $PublishDir "Kivi.App.exe"
if (-not (Test-Path $ExePath)) { throw "Expected $ExePath after publish, but it does not exist." }

# --- 3. Pack with Velopack -----------------------------------------------------
Step "vpk pack -> Setup.exe (UNSIGNED - no EV cert configured for this pass)"
if (Test-Path $ReleaseDir) { Remove-Item -Recurse -Force $ReleaseDir }

vpk pack `
    -u Kivi `
    -v $Version `
    -p $PublishDir `
    -e Kivi.App.exe `
    -o $ReleaseDir `
    --packTitle "Kivi" `
    --packAuthors "Sarvam AI" `
    -i $IconPath `
    -y
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed." }

$SetupExe = Join-Path $ReleaseDir "Kivi-win-Setup.exe"
if (-not (Test-Path $SetupExe)) { throw "Expected $SetupExe after packaging, but it does not exist." }

Step "Done"
Write-Host "Installer:  $SetupExe" -ForegroundColor Green
Write-Host ""
Write-Host "CAVEAT: This installer is UNSIGNED (no EV code-signing cert exists yet)." -ForegroundColor Yellow
Write-Host "Testers will see a SmartScreen 'unknown publisher' warning -- this is" -ForegroundColor Yellow
Write-Host "expected for an internal test build, not a bug. See docs/RELEASE.md." -ForegroundColor Yellow

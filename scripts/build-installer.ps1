<#
.SYNOPSIS
    Builds the custom, branded, PER-USER Kivi installer(s): a single self-contained
    Kivi-Setup-<rid>.exe per target architecture that opens a Canon-styled window,
    installs Kivi to %LocalAppData%\Kivi with NO admin prompt, and can uninstall itself.

.DESCRIPTION
    This is a NEW, standalone installer, completely separate from the Velopack path
    in publish-and-package.ps1 (which is left untouched). No auto-update engine, no Velopack.

    Multi-arch: by default builds BOTH win-x64 and win-arm64. Each installer is
    self-contained FOR ITS OWN ARCH and embeds the matching-arch app payload, so the
    installer runs natively on that machine (an x64 installer would only run emulated on
    an ARM64 PC, and would install the emulated x64 app -- so we ship one per arch, the way
    VS Code / Brave do). ARM64 Windows uses the same LLP64 data model as x64, so every
    P/Invoke in the app (incl. the SendInput INPUT struct) is layout-identical across both.

    Pipeline, per requested RID:
      1. Publish Kivi.App self-contained (RID) -> the app payload.
      2. Zip the payload -> Kivi.Installer\payload\payload.zip (embedded resource; swapped per arch).
      3. Publish Kivi.Installer self-contained single-file (RID) -> Kivi-Setup-<rid>.exe.

    NOTE: each Kivi-Setup exe is LARGE (~200-300 MB) -- it bundles two self-contained .NET
    runtimes (the app payload + the installer itself), the price of a no-prerequisite install.
    (To get under ~100 MB, switch to a framework-dependent app payload + a runtime check --
    see the packaging notes; not done here.)

    UNSIGNED: no EV code-signing cert yet, so Windows SmartScreen shows a "Windows protected
    your PC" / unknown-publisher warning. Expected for this pass (mirrors publish-and-package.ps1).

.PARAMETER Version
    Installer version string for the banner. The compiled default lives in Installer.Version.

.PARAMETER Configuration
    MSBuild configuration. Defaults to Release.

.PARAMETER Runtimes
    One or more target RIDs. Defaults to BOTH win-x64 and win-arm64. Pass a subset to build
    just one, e.g. -Runtimes win-arm64.

.EXAMPLE
    ./scripts/build-installer.ps1                      # both x64 + arm64
    ./scripts/build-installer.ps1 -Runtimes win-x64    # x64 only
    ./scripts/build-installer.ps1 -Runtimes win-arm64  # arm64 only
    ./scripts/build-installer.ps1 -Version 1.0.1
#>
[CmdletBinding()]
param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string[]]$Runtimes = @("win-x64", "win-arm64")
)

$ErrorActionPreference = "Stop"

$RepoRoot        = Split-Path -Parent $PSScriptRoot
$AppProject      = Join-Path $RepoRoot "Kivi.App"
$InstallerProj   = Join-Path $RepoRoot "Kivi.Installer"
$PayloadDir      = Join-Path $InstallerProj "payload"
$PayloadZip      = Join-Path $PayloadDir "payload.zip"
$InstallerOutDir = Join-Path $RepoRoot "dist\installer"

function Step($msg) {
    Write-Host ""
    Write-Host "== $msg ==" -ForegroundColor Cyan
}

if (-not (Test-Path $InstallerOutDir)) {
    New-Item -ItemType Directory -Force -Path $InstallerOutDir | Out-Null
}

$results = @()

foreach ($Runtime in $Runtimes) {

    $AppPublishDir   = Join-Path $RepoRoot "dist\app-$Runtime"
    $StageOutDir     = Join-Path $InstallerOutDir "_stage-$Runtime"
    $FinalExe        = Join-Path $InstallerOutDir "Kivi-Setup-$Runtime.exe"

    Write-Host ""
    Write-Host "############################################################" -ForegroundColor Magenta
    Write-Host "#  Building installer for $Runtime" -ForegroundColor Magenta
    Write-Host "############################################################" -ForegroundColor Magenta

    # --- 1. Publish the app payload (this RID) ------------------------------
    Step "[$Runtime] 1/4  Publish Kivi.App (self-contained) -> $AppPublishDir"
    if (Test-Path $AppPublishDir) { Remove-Item -Recurse -Force $AppPublishDir }
    dotnet publish $AppProject -c $Configuration -r $Runtime --self-contained true -o $AppPublishDir
    if ($LASTEXITCODE -ne 0) { throw "[$Runtime] App publish failed." }
    $AppExe = Join-Path $AppPublishDir "Kivi.App.exe"
    if (-not (Test-Path $AppExe)) { throw "[$Runtime] Expected $AppExe after publish, but it does not exist." }

    # --- 2. Zip the (arch-matched) payload into the installer project -------
    Step "[$Runtime] 2/4  Zip payload -> $PayloadZip"
    if (-not (Test-Path $PayloadDir)) { New-Item -ItemType Directory -Force -Path $PayloadDir | Out-Null }
    if (Test-Path $PayloadZip) { Remove-Item -Force $PayloadZip }
    Compress-Archive -Path (Join-Path $AppPublishDir "*") -DestinationPath $PayloadZip -Force
    if (-not (Test-Path $PayloadZip)) { throw "[$Runtime] Payload zip was not produced." }
    $ZipMb = [math]::Round((Get-Item $PayloadZip).Length / 1MB, 1)
    Write-Host "payload.zip = $ZipMb MB"

    # --- 3. Publish the installer (single-file, self-contained, this RID) ---
    # NOTE: an obj/ from a different RID can poison this build -- clean the installer's
    # intermediate + bin so each arch links against its own runtime pack.
    Step "[$Runtime] 3/4  Publish Kivi.Installer (single-file self-contained) -> Kivi-Setup-$Runtime.exe"
    Remove-Item -Recurse -Force (Join-Path $InstallerProj "obj") -ErrorAction SilentlyContinue
    Remove-Item -Recurse -Force (Join-Path $InstallerProj "bin") -ErrorAction SilentlyContinue
    if (Test-Path $StageOutDir) { Remove-Item -Recurse -Force $StageOutDir }
    dotnet publish $InstallerProj -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $StageOutDir
    if ($LASTEXITCODE -ne 0) { throw "[$Runtime] Installer publish failed." }

    $StageExe = Join-Path $StageOutDir "Kivi-Setup.exe"
    if (-not (Test-Path $StageExe)) { throw "[$Runtime] Expected $StageExe after publish, but it does not exist." }

    if (Test-Path $FinalExe) { Remove-Item -Force $FinalExe }
    Move-Item -Force $StageExe $FinalExe
    Remove-Item -Recurse -Force $StageOutDir -ErrorAction SilentlyContinue

    $SetupMb = [math]::Round((Get-Item $FinalExe).Length / 1MB, 1)
    $results += [pscustomobject]@{ Runtime = $Runtime; Path = $FinalExe; SizeMb = $SetupMb }
}

# --- 4. Report ---------------------------------------------------------------
Step "Done -- $($results.Count) installer(s)"
foreach ($r in $results) {
    $line = "$($r.Runtime)  ->  $($r.Path)  ($($r.SizeMb) MB)"
    Write-Host $line -ForegroundColor Green
}
Write-Host "Version:    $Version" -ForegroundColor Green
Write-Host ""
Write-Host "CAVEAT: These installers are UNSIGNED (no EV code-signing cert exists yet)." -ForegroundColor Yellow
Write-Host "Users will see a SmartScreen 'unknown publisher' warning -- expected for an" -ForegroundColor Yellow
Write-Host "internal build, not a bug. Install is per-user to %LocalAppData%\Kivi with no" -ForegroundColor Yellow
Write-Host "admin prompt; uninstall via Settings > Apps. Ship the arch that matches the" -ForegroundColor Yellow
Write-Host "target PC (win-x64 for Intel/AMD, win-arm64 for Windows-on-ARM)." -ForegroundColor Yellow

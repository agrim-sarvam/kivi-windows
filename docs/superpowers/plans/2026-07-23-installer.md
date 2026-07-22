# Installer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Package Kivi as a single, modern, no-choices setup exe (Chrome/Brave/Wisprflow-style: branded progress window, auto-installs, auto-launches) using Velopack, distributing the embedded shared Sarvam API key per the migration plan's key-distribution model.

**Architecture:** Velopack builds a self-contained release from `Kivi.App`'s existing `dotnet publish` output (already `WindowsAppSDKSelfContained=true`, `SelfContained=true`, `win-x64`) and wraps it in a `Setup.exe` with Velopack's built-in minimal installer UI. This plan starts with a validation spike (Task 1) before committing further work, since Velopack's compatibility with an unpackaged Windows App SDK app is not confirmed from documentation alone — this is the single highest-risk unknown in the whole five-part spec, and the plan is structured so that risk is resolved first, cheaply, before any packaging polish is invested.

**Tech Stack:** Velopack (`vpk` CLI + `Velopack` NuGet package), `dotnet publish`, PowerShell/batch build scripts.

## Global Constraints

- Single-screen, no-choices install: no license page, no install-location picker, no component checkboxes (per spec Part 5).
- Per-user install (no admin elevation), `%LocalAppData%\Kivi`, Start Menu shortcut, auto-update registration.
- Unsigned for now — SmartScreen warning is expected and explicitly out of scope (per spec Part 5 and the user's explicit instruction not to address signing in this pass).
- The Sarvam API key ships embedded with the build, in a separate local config/appsettings file next to the exe (not a hardcoded C# string literal), per the Sarvam migration plan's key-distribution guardrail — so it can be rotated without a full rebuild.
- This plan assumes Plans A (Sarvam migration), B (onboarding/tray), and C (main app window) are already implemented, since the installer packages the finished app — but Task 1's spike can run against the app's current state at any point, since it only needs *a* buildable WinUI3/WindowsAppSDK app, not the fully-featured one.

---

### Task 1: Spike — validate Velopack packages an unpackaged Windows App SDK app

This is the risk-resolution gate for the entire plan. If this fails, the rest of the plan needs re-scoping around a different installer framework (e.g. a plain Squirrel.Windows fork, or a minimal custom WPF/Win32 bootstrapper) — that decision is deferred until this task's result is known, per the spec's explicit "validate before committing further" instruction.

**Files:**
- Create (temporary, spike-only): `installer-spike/` directory at repo root, deleted at the end of this task regardless of outcome (Step 6).

**Interfaces:** none — this task produces a go/no-go finding, not shipped code.

- [ ] **Step 1: Install the Velopack CLI**

Run: `dotnet tool install -g vpk`
Expected: installs successfully. If `vpk` is already installed, run `dotnet tool update -g vpk` instead to ensure a current version.

- [ ] **Step 2: Publish a self-contained build of `Kivi.App`**

Run:
```bash
dotnet publish Kivi.App/Kivi.App.csproj -c Release -r win-x64 --self-contained true -o installer-spike/publish
```
Expected: succeeds, producing `installer-spike/publish/Kivi.App.exe` and its dependencies. This uses the exact same `WindowsAppSDKSelfContained`/`SelfContained`/`RuntimeIdentifiers=win-x64` settings already declared in `Kivi.App.csproj` (lines 39, 46-47) — no new publish profile is needed.

- [ ] **Step 3: Attempt a Velopack pack**

Run:
```bash
vpk pack --packId Kivi --packVersion 0.1.0 --packDir installer-spike/publish --mainExe Kivi.App.exe --outputDir installer-spike/releases
```
Expected: either succeeds (producing a `Setup.exe` in `installer-spike/releases/`) or fails with a specific error. **Record the exact output either way** — this is the evidence the go/no-go decision rests on.

- [ ] **Step 4: If Step 3 succeeded, run the produced installer**

Run `installer-spike/releases/Setup.exe` manually (this is a GUI installer — run it interactively, not via an automated script). Confirm: it installs without error, the installed app launches, and the installed app's WinUI3 windows (orb overlay, onboarding, or whichever launches first given current app state) render correctly — not just "a process starts," since Windows App SDK apps can fail at the XAML-loading stage even when the process technically launches (the framework's own DLLs need to resolve correctly from the self-contained deployment).

- [ ] **Step 5: Record the finding**

Write a short note (a few sentences, not a full report) into this plan's Task 1 section or as a commit message documenting: did `vpk pack` succeed, did the installed app actually run and render its UI correctly, and any error text encountered. This finding determines whether Task 2 onward proceeds as written or whether Velopack needs to be swapped for an alternative — if Step 3 or Step 4 failed, **stop here and do not proceed to Task 2** until a human decides the replacement approach, since every subsequent task assumes Velopack works.

- [ ] **Step 6: Clean up the spike directory**

```bash
rm -rf installer-spike
```

Regardless of outcome, the spike directory is temporary scratch work, not part of the shipped repo.

- [ ] **Step 7: Commit the finding**

```bash
git commit --allow-empty -m "$(cat <<'EOF'
chore(installer): record Velopack + Windows App SDK compatibility spike result

[Fill in: PASS or FAIL, with the key evidence from Step 3/4 -- e.g. "vpk pack
succeeded, installed app launched and rendered the orb/onboarding UI
correctly" or "vpk pack failed with error: <exact error text>, needs a
different installer framework."]
EOF
)"
```

Replace the bracketed placeholder with the actual finding before committing — this is the one place in this plan where the content is genuinely unknown until the step is run, which is why it's structured as a spike rather than assumed.

---

### Task 2: Embedded Sarvam key config file

Per the Sarvam migration plan's key-distribution guardrail: ship the key in a separate local config file next to the exe, not a hardcoded string literal, so it can be rotated without a rebuild.

**Files:**
- Create: `Kivi.App/kivi-key.local.json.template` (committed — a template with an empty key, documenting the expected shape; the real, filled-in file is generated at release-build time and never committed)
- Modify: `Kivi.App/App.xaml.cs` (read this file, if present, as an additional key source alongside the existing env-var/user-secrets sources)
- Modify: `.gitignore` (ensure the real `kivi-key.local.json` — without `.template` — is git-ignored)

**Interfaces:**
- Produces: `App.xaml.cs`'s secret-resolution logic gains a third source (env var → this file → nothing), read before the `DpapiSecretStore` caching step.

- [ ] **Step 1: Create the template file**

Create `Kivi.App/kivi-key.local.json.template`:

```json
{
  "SarvamApiKey": ""
}
```

- [ ] **Step 2: Confirm `.gitignore` excludes the real file**

Read `.gitignore` in full. If it does not already have a pattern covering `kivi-key.local.json` (distinct from the `.template` suffix version, which must stay tracked), add:

```
kivi-key.local.json
```

- [ ] **Step 3: Update `App.xaml.cs`'s secret resolution**

In `Kivi.App/App.xaml.cs`, modify the `ISecretStore` registration (currently lines 76-82):

```csharp
        services.AddSingleton<ISecretStore>(_ =>
        {
            var envKey = configuration["SARVAM_API_KEY"];
            var dpapi = new DpapiSecretStore();
            if (!string.IsNullOrEmpty(envKey))
            {
                dpapi.SetApiKey(envKey);
            }
            else
            {
                var embeddedKeyPath = Path.Combine(AppContext.BaseDirectory, "kivi-key.local.json");
                if (File.Exists(embeddedKeyPath))
                {
                    try
                    {
                        var json = File.ReadAllText(embeddedKeyPath);
                        using var doc = System.Text.Json.JsonDocument.Parse(json);
                        if (doc.RootElement.TryGetProperty("SarvamApiKey", out var keyProp))
                        {
                            var embeddedKey = keyProp.GetString();
                            if (!string.IsNullOrEmpty(embeddedKey)) dpapi.SetApiKey(embeddedKey);
                        }
                    }
                    catch { /* malformed key file -- fall through with no key, same as today's missing-key behavior */ }
                }
            }
            return dpapi;
        });
```

This tries the env var first (unchanged dev-time behavior — a developer's own `.env` still works exactly as before), then falls back to the installed-next-to-the-exe `kivi-key.local.json` (the file end users' installed builds will actually have, populated by the release-build step in Task 3), then falls back to whatever's already cached in DPAPI from a previous run (the existing `DpapiSecretStore.GetApiKey()` behavior, unchanged).

- [ ] **Step 4: Build**

Run: `dotnet build Kivi.App`
Expected: builds clean.

- [ ] **Step 5: Manual smoke test**

Create a throwaway `kivi-key.local.json` next to the built exe (in `Kivi.App/bin/Debug/net8.0-windows.../`) with a real Sarvam key, temporarily remove/rename any local `.env`, run the built exe directly (not `dotnet run`, since that's a different working-directory/base-directory context), and confirm dictation works — proving the embedded-file path is actually reached, not just the env-var path. Delete the throwaway file afterward so it doesn't get accidentally committed.

- [ ] **Step 6: Commit**

```bash
git add Kivi.App/kivi-key.local.json.template Kivi.App/App.xaml.cs .gitignore
git commit -m "feat(app): support an embedded kivi-key.local.json as a release-build key source"
```

---

### Task 3: Release build script that stamps in the real key

**Files:**
- Create: `scripts/build-release.ps1`

**Interfaces:**
- Produces: a PowerShell script that takes the real Sarvam API key as a parameter (never hardcoded in the script itself, never committed), publishes `Kivi.App`, writes the real `kivi-key.local.json` into the publish output, and leaves the output ready for Task 4's `vpk pack` step.

- [ ] **Step 1: Write the build script**

Create `scripts/build-release.ps1`:

```powershell
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
```

- [ ] **Step 2: Manual test**

Run: `powershell -File scripts/build-release.ps1 -SarvamApiKey "test-key-value" -Version "0.1.0"`
Expected: completes without error, produces `release/publish/Kivi.App.exe` and `release/publish/kivi-key.local.json` containing `{"SarvamApiKey": "test-key-value"}`.

- [ ] **Step 3: Add `release/` to `.gitignore`**

Confirm `.gitignore` excludes the `release/` output directory (build artifacts, never committed). Add `release/` if not already covered by an existing broader pattern (e.g. `bin/`/`obj/` patterns won't catch this custom directory name).

- [ ] **Step 4: Commit**

```bash
git add scripts/build-release.ps1 .gitignore
git commit -m "feat(installer): add release build script that stamps in the embedded Sarvam key"
```

---

### Task 4: Velopack packaging with branded, single-screen installer UI

Only proceed with this task if Task 1's spike passed. This task assumes `vpk pack` successfully produces a working `Setup.exe` from a Windows App SDK self-contained publish output.

**Files:**
- Create: `scripts/build-installer.ps1`
- Create: `Kivi.App/Assets/Icons/kivi.ico` (Velopack needs a `.ico` for the installer/shortcut icon; the repo currently only has `kivi-mask.png` — convert it)

**Interfaces:**
- Produces: a script that runs Task 3's release build, then `vpk pack` with Velopack's icon/branding options, producing a distributable `Setup.exe`.

- [ ] **Step 1: Convert the existing PNG icon to `.ico`**

`Kivi.App/Assets/Icons/kivi-mask.png` exists but Velopack (like most Windows installer/shortcut tooling) needs a `.ico` file, which can contain multiple resolutions. Use any standard PNG-to-ICO conversion approach (e.g. `magick kivi-mask.png -define icon:auto-resize=256,128,64,48,32,16 Kivi.App/Assets/Icons/kivi.ico` if ImageMagick is available, or an online/offline converter) to produce `Kivi.App/Assets/Icons/kivi.ico`. If no conversion tool is available in the environment, flag this as a manual step for the user to complete before this task can finish, rather than shipping a missing/placeholder icon file.

- [ ] **Step 2: Write the installer packaging script**

Create `scripts/build-installer.ps1`:

```powershell
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
    --icon $iconPath

if ($LASTEXITCODE -ne 0) {
    throw "vpk pack failed with exit code $LASTEXITCODE"
}

Write-Host "Installer ready at: $releasesDir\Setup.exe"
```

- [ ] **Step 3: Confirm Velopack's default installer UI matches the no-choices requirement**

Velopack's default `Setup.exe` UI is already a minimal single-screen progress window with no license/location/component pages by default (this is Velopack's baseline design, distinct from older MSI-wizard-style installers) — confirm this against the actual produced `Setup.exe` from Step 4 below rather than assuming; if Velopack's default shows any extra page or choice, check Velopack's CLI options (`vpk pack --help`) for a flag to suppress it before concluding customization work is needed here.

- [ ] **Step 4: Run the full build → package pipeline**

```bash
powershell -File scripts/build-release.ps1 -SarvamApiKey "<a real Sarvam key for this test build>" -Version "0.1.0"
powershell -File scripts/build-installer.ps1 -Version "0.1.0"
```
Expected: both scripts complete, producing `release/releases/Setup.exe`.

- [ ] **Step 5: Run the produced installer end-to-end**

Run `release/releases/Setup.exe` on a clean or test Windows account. Confirm: single branded progress window appears (Kivi icon/branding, progress bar, no license/location/component pages), installs to a per-user location without requesting admin elevation, adds a Start Menu shortcut, and auto-launches Kivi on completion. Confirm the launched app goes into onboarding (first run) and that dictation works using the embedded key (proving Task 2/3's key-stamping actually reaches the installed app).

- [ ] **Step 6: Commit**

```bash
git add scripts/build-installer.ps1 Kivi.App/Assets/Icons/kivi.ico
git commit -m "feat(installer): add Velopack packaging script producing a single-screen Setup.exe"
```

---

### Task 5: Document the release process

**Files:**
- Modify: `README.MD` (add a short "Building a release" section)

**Interfaces:** none — documentation only.

- [ ] **Step 1: Read the current `README.MD` in full**

Confirm its existing structure/section conventions before adding to it (this file was already flagged as having some stale content in the Sarvam migration plan's Task 6 — if that plan's README fixes haven't been applied yet, apply them now as part of reading it fully, or note that they're out of scope for this specific plan if Plan A already handled it).

- [ ] **Step 2: Add a "Building a release" section**

Add a section documenting the two-script pipeline:

```markdown
## Building a release

1. `powershell -File scripts/build-release.ps1 -SarvamApiKey "<key>" -Version "<version>"` —
   publishes a self-contained build and stamps in the embedded Sarvam API key
   (never commit the real key; it's read from the command-line parameter only).
2. `powershell -File scripts/build-installer.ps1 -Version "<version>"` — packages the
   publish output into `release/releases/Setup.exe` via Velopack.

The resulting `Setup.exe` is unsigned (no code-signing certificate yet) — Windows
SmartScreen will show a warning on first run; this is expected until a certificate
is obtained.

The embedded Sarvam key means every install of this Setup.exe draws on the same
Sarvam account's credits and rate limits — see the key-distribution note in
`docs/superpowers/specs/2026-07-23-kivi-sarvam-migration-and-full-stack-app-design.md`
before distributing this build beyond a small, trusted circle.
```

- [ ] **Step 3: Commit**

```bash
git add README.MD
git commit -m "docs: document the two-script release/installer build pipeline"
```

---

## Self-Review Notes

- **Spec coverage:** Part 5 of the spec (Velopack, single-screen no-choices UI, per-user install, unsigned, auto-launch into onboarding) is covered by Tasks 1 and 4. The spec's explicit "validate Velopack compatibility first" open item is Task 1, structured as a true go/no-go gate rather than an assumed-safe step — Task 1 Step 5 explicitly instructs stopping before Task 2 if the spike fails. The "Key distribution" addendum from the Sarvam migration plan (embedded key in a separate rotatable file, not a hardcoded literal) is covered by Tasks 2-3.
- **Placeholder scan:** the one deliberately unresolved value is Task 1 Step 7's bracketed finding placeholder — flagged explicitly as "fill in before committing," not left as a silent TODO, since the actual spike result cannot be known until the step runs.
- **Ambiguity resolved:** Task 4 Step 1 (PNG→ICO conversion) is flagged as possibly requiring a manual step if no conversion tool is available in the execution environment, rather than assuming a tool exists — this is an honest environment-dependency callout, not a placeholder for missing design decisions.
- **Type/naming consistency check:** `kivi-key.local.json`'s shape (`{"SarvamApiKey": "..."}`) is identical across Task 2's template, Task 2's `App.xaml.cs` reader, and Task 3's build script writer. `scripts/build-release.ps1`'s output path (`release/publish`) matches exactly what Task 4's `scripts/build-installer.ps1` expects as input.
- **Dependency ordering, explicit:** this plan assumes Plans A/B/C are already implemented (the installer packages the finished app), but Task 1's spike is deliberately independent of that — it validates the packaging mechanism itself against whatever buildable state the app is in at the time the spike is run, so the highest-risk unknown can be resolved early rather than blocking on the other three plans first.

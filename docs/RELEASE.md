# Kivi .NET/Windows — Release / Packaging (M8)

Packaging is the **.NET publish → Windows installer** path. The reference used
electron-builder + electron-updater over an electron-vite `out/` bundle; the .NET port
replaces that whole toolchain with `dotnet publish` + a Windows installer (**MSIX**, or
**WiX/MSI** if a classic installer is preferred) + a Windows auto-updater.

**Unlike the Electron reference, this app ships native code** — the `WH_KEYBOARD_LL` keyboard
hook and the `SendInput` paste path — so signing and SmartScreen reputation matter from the
first public build (see "Windows — EV code-signing" below). There is **one target: Windows**
(x64 + arm64). No macOS/Linux packaging exists in this repo.

## Build commands

```powershell
dotnet build Kivi.sln -c Release                 # compile
dotnet test  Kivi.Core.Tests                     # unit + golden-frame + wire-parity gate
dotnet publish Kivi.App -c Release -r win-x64   --self-contained  # framework-dependent also fine
dotnet publish Kivi.App -c Release -r win-arm64 --self-contained
# then package (see installer below):
#   MSIX:  msbuild /t:Publish  (Windows Application Packaging project)  OR  MakeAppx
#   MSI:   the WiX toolset (candle/light or the WiX SDK targets)
```

Each publish produces the app payload under `Kivi.App/bin/Release/<rid>/publish/`; the
installer step packages that payload plus the compiled native helpers and the one image
asset. Artifacts land in `dist/` (gitignored).

### What builds where
- **Windows**: builds and packages natively on a Windows runner. x64 + arm64.
- There is **no cross-compilation target** — no dmg, no AppImage/deb, no Wine step. (The
  Electron plan's "config-only on Apple Silicon / native CI matrix" concern does not apply;
  the CI matrix is Windows-only.)

## App icon

The app ships **one real image asset** (the brand mark). Generate the Windows `.ico` from it
for the taskbar/installer, and the MSIX logo assets (Square44x44, Square150x150, etc.) from
the same source. **Swap the placeholder for final brand art before a public release.** All
other UI is code-drawn (see `docs/maps/orb-visual-and-box.md`, `main-window-shell-pages.md`).

---

## Signing & notarization

> There is no macOS notarization here (no mac target). The Windows story below replaces the
> reference's Sparkle/hardened-runtime/entitlements section entirely.

### Windows — EV code-signing (do it EARLY)

The app bundles a **low-level keyboard hook** and a **`SendInput` paste path**. These trip
AV/SmartScreen keylogger heuristics, so the app (and any separately-compiled native helper
binaries) should be **EV code-signed from the first public build** to start accruing
SmartScreen reputation (`MASTER-PLAN` R11).

- Sign the published `Kivi.App` executable and every native helper binary with an **EV cert**
  (hardware token or a cloud/HSM signing service). For MSIX, sign the `.msix` package; for
  MSI, sign both the `.exe`/DLLs and the `.msi`.
- Set the signing cert via the CI secret store (an HSM/cloud-HSM signing hook is preferred to
  a raw `.pfx`); use `signtool sign /fd sha256 /tr <RFC3161 timestamp> /td sha256`.
- Timestamp every signature (so signatures stay valid after the cert expires).

**Dev builds** are unsigned (or self-signed for MSIX sideloading during development); the
release pipeline is kept **sign-ready** and the real EV signing runs only in the M8 CI job.

---

## Installer

Two viable Windows installer paths — decide per M8:

- **MSIX** (preferred for a modern Windows app): clean install/uninstall, per-user, built-in
  auto-update via the App Installer / a `.appinstaller` feed. Caveat: the low-level keyboard
  hook and global `SendInput` must work under the MSIX runtime (verify the hook installs and
  paste reaches other apps from a packaged process; full-trust MSIX is required — declare the
  `runFullTrust` capability). Launch-at-login via the MSIX `StartupTask` extension.
- **WiX / MSI** (classic, maximum control): a plain per-user or per-machine installer with a
  Startup shortcut / registry `Run` key for launch-at-login, and a bundled updater. Choose
  this if MSIX's sandbox interferes with the hook/paste seams.

The installer registers the `kivi` URL scheme (for OAuth deep-link fallback; the primary
callback is the loopback `HttpListener`) and, if enabled, the launch-at-login entry.

---

## Auto-update

- **MSIX path:** the App Installer auto-update mechanism against a hosted `.appinstaller`
  manifest (the analog of electron-updater's `latest.yml`). No in-app updater code needed;
  Windows checks and applies updates on the configured cadence.
- **MSI path:** a Squirrel.Windows- or Velopack-style updater with a hosted release feed
  (delta packages + a versioned manifest), invoked from an updater seam in `Kivi.App` that is
  **gated** — never runs in dev, only when packaged and explicitly enabled (mirror the
  reference's `KIVI_ENABLE_UPDATER` gate; default launches make no network call, downloads are
  opt-in).
- **`TODO(release)`**: the hosted update feed + the "restart to update" UX + staged rollout are
  wired in the M8 CI job. The user-facing update prompt is not built yet.

---

## Launch-at-login

Registry `Run` key (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) or a Startup-folder
shortcut (MSI path), or the MSIX `StartupTask` extension (MSIX path) — the `SMAppService` /
`setLoginItemSettings` analog. Toggled from Settings ▸ Advanced.

---

## P7: building an internal-test installer (Velopack)

This is the current, working packaging path for handing testers a `Setup.exe` on other Windows
machines. It supersedes the MSIX/WiX discussion above **for this pass only** — see the
"Installer-tooling decision" callout in `docs/maps/electron-crossplatform-packaging.md` §5 for
the full rationale (already-installed `vpk` CLI, single self-contained `Setup.exe`, no admin
rights required to install, and it doesn't run the app inside an MSIX app-container/sandbox —
which matters here because Kivi installs a low-level `WH_KEYBOARD_LL` hook and does global
`SendInput` paste).

### One-shot build

```powershell
./scripts/publish-and-package.ps1
# or pin a version:
./scripts/publish-and-package.ps1 -Version 1.0.1
```

This runs, in order: `dotnet build` (fails fast if the solution is broken) → `dotnet test` →
`dotnet publish Kivi.App -c Release -r win-x64 --self-contained true` → `vpk pack` (Velopack).

### Manual steps (what the script automates)

```powershell
dotnet build Kivi.sln -c Release
dotnet test Kivi.sln
dotnet publish Kivi.App -c Release -r win-x64 --self-contained true -o dist/publish

vpk pack `
    -u Kivi `
    -v 1.0.0 `
    -p dist/publish `
    -e Kivi.App.exe `
    -o dist/releases `
    --packTitle "Kivi" `
    --packAuthors "Sarvam AI" `
    -i Kivi.App/kivi.ico `
    -y
```

Run `vpk --help` / `vpk pack --help` before changing flags — Velopack's CLI surface has
changed across versions; don't assume flags from an older doc or memory.

### Where the installer lands

`dist/releases/Kivi-win-Setup.exe` — this is the file to hand to testers. (`dist/` also
contains `Kivi-win-Portable.zip` and the Velopack `.nupkg`/release-feed JSON used for
self-updating; testers only need `Setup.exe`.) `dist/` is gitignored — these are build
artifacts, not checked in.

### Velopack + WPF wiring

Velopack requires `VelopackApp.Build().Run()` to execute at the very start of the process
(before any other startup code) so it can intercept its own install/update/uninstall hook
invocations against the installed exe. `Kivi.App` therefore has an explicit
`Kivi.App/Program.cs` with a hand-written `Main` (`<StartupObject>Kivi.App.Program</StartupObject>`
in `Kivi.App.csproj`, replacing the WPF-SDK's normally auto-generated one from `App.xaml`) that
calls `VelopackApp.Build().Run()` before constructing and running the WPF `App`.

### CAVEAT: this installer is UNSIGNED

**There is no EV code-signing certificate yet** — that is a known, accepted gap for this
internal-test pass, not an oversight. `vpk pack` reports this explicitly
(`No signing parameters provided, N file(s) will not be signed.`).

Practical effect: when a tester runs `Kivi-win-Setup.exe` on another machine, **Windows
SmartScreen will show a "Windows protected your PC" / unknown-publisher warning.** This is
expected — testers need to click "More info" → "Run anyway." Do not mistake this for a bug
report. This must be fixed (EV cert + `signtool`, or `vpk pack --signParams`/
`--azureTrustedSignFile`) before any real/public release — see the EV code-signing section
above, which still applies unchanged; only its timing (deferred vs. day-one) has changed for
this internal pass.

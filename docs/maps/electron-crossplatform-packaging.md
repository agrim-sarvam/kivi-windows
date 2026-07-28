# MAP: electron-crossplatform-packaging

> **Filename retained to mirror the Electron reference `docs/` structure 1:1. Content is the
> Windows/.NET packaging + app architecture story** — the .NET analog of
> `_reference/sarvam-kivi-electron/docs/maps/electron-crossplatform-packaging.md`. There is no
> Electron and no cross-platform target here; this is a single Windows/.NET build.

# .NET/Windows Architecture, Build & Packaging — Kivi Clone Digest

Scope: how to structure the .NET app (orb overlay + main window + tray), the in-process seams (no
IPC), the Windows dev/build/test loop, packaging/signing/auto-update, and the transparent/
always-on-top/click-through overlay gotchas. Native macOS primitives from the reference are mapped to
their Windows/.NET equivalents.

---

## 0. What the app actually does (so the port matches)

The behaviors the .NET windows must reproduce (from the reference orb + macOS source):

**Orb overlay** — a transparent, borderless, **non-activating, always-on-top** window:
- `WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW` (+ `WS_POPUP`) — frameless, never-activating, always-on-top, no taskbar button.
- Drawn via `UpdateLayeredWindow` (premultiplied ARGB) → true per-pixel transparency, no window shadow (a native Win32 layered window with an invisible WPF host for lifetime).
- Click-through by default (`WS_EX_TRANSPARENT`); the window **only accepts keyboard focus** while editing the box (M4), otherwise the frontmost app keeps focus so dictated text lands *there* (the `.nonactivatingPanel` contract).
- Canvas is a fixed logical `1480×720` (`.base` envelope; `.maxi` = 1880×1760); orb centre inset `24` from the screen edge; the window is bigger than the visible orb.

**Dynamic click-through** — a per-tick poll reads `GetCursorPos`, runs the **geometric hit-test**
(`FlowFrame.InteractiveTarget`, not any `.OnHover`), and toggles `WS_EX_TRANSPARENT` every frame. The
single most important overlay trick to replicate (the port of macOS `syncCursorState`).

**Tray** — a Windows notification-area icon + a frameless popover window (see
`menubar-onboarding-auth.md §1`).

**App type** — a resident agent: no taskbar button for the orb, process stays alive with windows
hidden.

**Hotkey** — default **Right-Ctrl hold** (no `fn` on Windows). Push-to-talk needs a low-level
keyboard hook — the hardest thing to port (see §6).

---

## 1. Recommended project structure

**Build harness: the .NET SDK** (`dotnet build`/`publish`) with a **WPF app on .NET 8
(`net8.0-windows`, `<UseWPF>true</UseWPF>`)** — no Windows App SDK dependency. There is no bundler
like electron-vite — MSBuild + XAML compilation is the pipeline. Renderer stack: **WPF XAML + MVVM
(CommunityToolkit.Mvvm)** with the code-drawn orb on a WPF-hosted 2D surface (Win2D or
`WriteableBitmap`/`DrawingContext`) and a `CompositionTarget.Rendering` per-frame tick.

```
Kivi.sln
├─ Kivi.Core/                    # pure, UI/OS-free (the packages/* equivalent)
│  ├─ Orb/  KiwiMark/  DesignTokens/  Planner/
│  ├─ Wire/ (KiviServiceClient, WireModels, Endpoints, DictationBudgets)
│  ├─ Rest/ (KiviRestClient)
│  └─ Contracts/ (IHotkeyService, IPasteService, IOverlayHost, IAudioCapture, ISecretStore, ITrayHost, DTOs)
├─ Kivi.Platform/                # Windows-native seams (implement Kivi.Core.Contracts)
│  ├─ Hotkey/  Paste/  Frontmost/  Overlay/  Audio/  Secrets/  Tray/  Auth/
├─ Kivi.App/                     # WPF host + composition root + views
│  ├─ App.xaml(.cs)              # DI container, app lifetime, window orchestration
│  ├─ DictationOrchestrator.cs   # hotkey→connect→capture→final→paste (the OrbHost analog)
│  ├─ Views/  ViewModels/  Drawing/ (Win2D or WriteableBitmap/DrawingContext)  Themes/ (WPF XAML from DesignTokens)
├─ Kivi.Core.Tests/             # xUnit: golden-frame, wire parity, classifier, planner
├─ build/                        # icon assets, installer inputs (entitlements have no analog)
└─ resources/                    # the one image asset → .ico + MSIX logo assets
```

Key structural rules:
- **One window per surface** (orb layered window / main WPF window / tray popover) — each has different constructor-time flags (the orb needs the layered/no-activate `WS_EX_*`; the main window is normal chrome). Don't reuse one window.
- **The app is the single source of truth** for window lifecycle, tray, the global hook, and the STT socket. Views are pure layers over injected state.
- **No IPC, no preload, no `contextBridge`** — the Electron main↔renderer boundary collapses to in-process `async`/`await` + events + DI-injected interfaces (T2). This is a strict simplification.

---

## 2. The three windows — concrete configs

### Orb overlay (transparent, always-on-top, click-through) — `Kivi.Platform.Overlay`
A **native Win32 layered window** (NOT a WPF surface — a WPF transparent window cannot host a truly
per-pixel-alpha, non-activating overlay; see the `orb-is-a-chip` memo). An invisible WPF anchor window
holds app/lifetime; the orb itself is drawn with `UpdateLayeredWindow` (premultiplied ARGB), and
WPF↔Win32 interop is seamless.
```
extended styles: WS_EX_LAYERED | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT
style:           WS_POPUP (frameless)
size:            1480×720 logical (.base) / 1880×1760 (.maxi) — swap live; size to the display at runtime
always-on-top:   SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)
```
- **`WS_EX_NOACTIVATE`** is the non-activating-panel analog: the window never steals keyboard focus, so dictated text lands in the active app.
- **Dynamic click-through:** a `~16–33 ms` timer in the app reads `GetCursorPos`, compares to the orb bounds + the current hit-region (`FlowFrame.InteractiveTarget`), and toggles `WS_EX_TRANSPARENT` via `SetWindowLong`. This mirrors the macOS `NSEvent.mouseLocation` poll. (No `forward`-mouse-events dependency — the poll is authoritative and load-bearing.)
- For the editable-box case (M4), temporarily clear `WS_EX_NOACTIVATE` + focus the window, then revert on blur.

### Main window (normal) — WPF
A standard WPF `Window`, 1180×760 default, min 980×640, hidden until ready. Custom title bar via
`WindowChrome` (`WindowStyle=None` + a custom caption/drag region) and your own window controls; the
drag strip replaces `-webkit-app-region: drag`.

### Tray — `Kivi.Platform.Tray`
A Windows notification-area icon (Shell_NotifyIcon interop / a tray helper) + a frameless popover
window positioned near the tray bounds, hide-on-deactivate. Pre-render discrete per-state icons;
avoid high-frequency updates (`menubar-onboarding-auth.md §1`).

---

## 3. In-process wiring (replaces main↔renderer IPC)

There is **no IPC**. The Electron pattern (`ipcRenderer.invoke`/`ipcMain.handle` +
`webContents.send`) becomes:
- **Request→response**: a direct `await service.MethodAsync(...)` on a DI-injected interface.
- **Pushes** (STT partials, state changes): C# `event` / `IObservable` the view subscribes to.
- **PCM path**: a bounded in-process `Channel<byte[]>` from the WASAPI capture thread to the socket pump — zero serialization (replaces the `MessagePort` transferable path).

```csharp
// Kivi.Core/Contracts
public interface IDictationService {
    Task StartAsync();
    Task StopAsync();
    event Action<DictationEvent> Event;   // interim/final/error pushes
}
// Kivi.App wiring
sttClient.Event += e => orbViewModel.OnDictationEvent(e);   // no ipc, just an event
```
Rules: keep the STT socket in the app (one process) so orb + main + tray all observe one session;
validate inputs at trust boundaries (loopback, clipboard); for high-frequency streams use events, and
for raw PCM use a `Channel`, never JSON. The orb↔service data path that Electron split across
main/renderer is simply in-process here.

---

## 4. Windows dev/build/test loop

```powershell
dotnet build Kivi.sln -c Debug          # compile
dotnet run --project Kivi.App           # launch (WPF app)
dotnet test Kivi.Core.Tests             # unit + golden-frame + wire-parity gate
dotnet build -t:Publish Kivi.App -c Release   # publish payload for packaging
```
- **Run/iterate:** `dotnet run` / F5 in Visual Studio; XAML Hot Reload for the view layer.
- **Testing:** unit/logic with **xUnit** (the engine + wire + planner are pure — fast, headless). UI e2e via a WPF UI-automation driver (**WinAppDriver**/Appium/FlaUI over UI Automation) — it can drive the WPF window, assert on the visual tree, and screenshot for the side-by-side visual-parity gate against the running Electron app. The **geometry classifier** is tested in isolation (a pure function) plus a view-level interactive-region test (OS-level click-through is validated by the integration harness, not the UI driver).
- **Local backend:** point the app's STT client at the same local `kivi-service` (`ws://127.0.0.1:8788`). No change to the service.
- **Signing:** see §5. (There is no macOS notarization — no mac target.)

---

## 5. Packaging (Windows only)

Full detail in `RELEASE.md`. Summary:
- `dotnet publish Kivi.App -c Release -r win-x64` (+ `win-arm64`) → the app payload.
- **Installer (P7 decision): Velopack, not MSIX or WiX/MSI.**

> **Installer-tooling decision (MSIX/WiX → Velopack).** For the P7 internal-test packaging
> pass, the installer is built with **Velopack** (`vpk` CLI) rather than MSIX or WiX/MSI as
> originally recommended above. Rationale, decided with the user: `vpk` was **already
> installed** on the build machine (no new toolchain to stand up); it produces a **single
> self-contained `Setup.exe`** that testers can just double-click; it needs **no admin rights**
> to install (per-user install — good for handing an exe to testers on machines you don't
> control); and unlike MSIX it **does not run the app in an app-container/sandbox**, so it
> sidesteps the exact risk this doc flags in the MSIX bullet above — verifying the low-level
> `WH_KEYBOARD_LL` hook and global `SendInput` paste path actually work under a packaged
> process. This is a deliberate **T1 (platform-native seam) divergence** from the ported docs'
> default, following the same "mirror Electron's *intent* (a working installer), diverge on
> the *tool* for a Windows/.NET-native reason" pattern as the WinUI→WPF decision recorded in
> `MASTER-PLAN.md`. **This is scoped to the current internal-test pass** — if a future
> milestone needs MSIX's Store distribution or auto-update-via-`.appinstaller`, or WiX's
> machine-wide/MSI-specific installs, revisit then; Velopack is not declared the permanent
> final answer, just the pragmatic choice for getting a `Setup.exe` into testers' hands now.
>
> Velopack also ships its own updater (a hosted release feed + delta packages), which is a
> straight swap for the "MSI path: a Squirrel.Windows- or Velopack-style updater" line in
> `RELEASE.md`'s auto-update section — that becomes the concrete choice, not just an example,
> whenever the update feed is actually wired up (still `TODO(release)`, unchanged by this
> decision).
- **EV code-sign** the executable + any native helper (and the package): a low-level keyboard hook trips keylogger AV/SmartScreen heuristics, so sign **from the first public build** to accrue reputation (R11). `signtool sign /fd sha256 /tr <RFC3161> /td sha256`. **Status: deferred for this pass** — no EV cert exists yet, so the P7 `Setup.exe` is **unsigned**; testers will see a SmartScreen "unknown publisher" warning (expected, not a bug — see `RELEASE.md`). `vpk pack` accepts `--signParams`/`--azureTrustedSignFile`/`--signTemplate` for whenever a cert is available — the pipeline stays sign-ready, it's just not signing today.
- **App icon**: generate the Windows `.ico` from the one real image asset (`_reference/sarvam-kivi-electron/build/icon.png`) — done for P7: `Kivi.App/kivi.ico` (16/32/48/256 multi-size), referenced via `<ApplicationIcon>` in `Kivi.App.csproj` and passed to `vpk pack -i`. MSIX logo assets (Square44x44 etc.) are N/A under the Velopack path and not generated.
- **CI matrix**: **Windows runners only** — `build` + `xunit` + golden-frame gate + WinAppDriver e2e + visual-diff + the OS-integration harness on every PR; nightly real-STT parity. (No macos/ubuntu runners; no Wine, no cross-compilation.)

> **Removed (not applicable):** dmg/notarization, AppImage/deb/rpm, the Docker/Wine cross-compile
> path, and the three-native-runner cross-OS matrix. There is one target: Windows.

---

## 6. Global hotkey — the big risk

The default trigger is **Right-Ctrl hold** (push-to-talk). The built-in shortcut APIs
(`globalShortcut` in Electron, `RegisterHotKey` in Win32):
- are **accelerator/combo-based** (need a real key), fire on **keydown only — no key-up**, so they can't express hold-to-talk, and **cannot bind bare modifiers**. `fn` is essentially unreachable and doesn't exist off Apple hardware.

Implication: ship a **low-level keyboard hook** — `SetWindowsHookEx(WH_KEYBOARD_LL)` on a dedicated
native thread with its own message pump — to get keydown+keyup and synthesize hold-to-talk. On
Windows this needs **no permission gate** (unlike macOS Accessibility), but it does trip AV/SmartScreen
(EV-sign early, R11) and the hook must not live on a busy thread (R5). Budget real time here; it is
not a drop-in. Text insertion (paste into the active app) is also native — `clipboard` + `SendInput`
Ctrl+V (see `platform-coupling-audit.md §3`, `dictation-audio-pipeline.md §8`).

---

## 7. Transparent / frameless / always-on-top gotchas (Windows)

**View stack decision:** WPF (.NET 8) + MVVM, with the orb drawn on a WPF-hosted 2D surface (Win2D or
`WriteableBitmap`/`DrawingContext`) — the UI is a XAML tree (orb states, flow, settings pages
mirroring the reference views), design tokens port to WPF `ResourceDictionary` theme dictionaries +
generated C# constants, XAML Hot Reload makes the visual-parity loop fast, and a UI-automation driver
gives the screenshot gate. **The orb itself is a native Win32 layered window, not a WPF surface** — a
WPF transparent window can't give a truly non-activating, per-pixel-alpha overlay (and WPF↔Win32
interop is seamless). WPF was chosen over WinUI 3 for lower latency on this custom-drawn,
per-frame-animated app and better transparent/layered-window handling.

Platform behavior:
- **Transparency** — a **layered window** (`WS_EX_LAYERED` + `UpdateLayeredWindow`) gives true per-pixel alpha; DWM composition is always on for modern Windows (no compositor detection needed). WPF's own `AllowsTransparency` window is a per-pixel-alpha surface but cannot be truly non-activating — hence the native Win32 layered window for the orb.
- **No window shadow / no rounded corners** — a layered window draws exactly what you composite; control corners in your own drawing (the 2D surface).
- **Always-on-top** — `WS_EX_TOPMOST` + `SetWindowPos(HWND_TOPMOST, …, SWP_NOACTIVATE)`. It can still sit under a truly exclusive-fullscreen app; acceptable for the orb.
- **Non-activating** — `WS_EX_NOACTIVATE` keeps host keyboard focus (the crux; R20). Verify with the integration harness (type into a target while the orb is visible → keystrokes land in the target).
- **Click-through** — toggle `WS_EX_TRANSPARENT` per-tick from the geometric hit-test (`GetCursorPos` poll). No `forward`-mouse dependency.
- **No taskbar button** — `WS_EX_TOOLWINDOW` (+ hidden from Alt-Tab).

---

## 8. Native-primitive → Windows/.NET mapping (quick reference)

| macOS (reference source) | Windows/.NET |
|---|---|
| `NSPanel .borderless/.nonactivatingPanel` | native layered window `WS_POPUP` + `WS_EX_NOACTIVATE` |
| `isFloatingPanel`, `level=.statusBar` | `WS_EX_TOPMOST` + `SetWindowPos(HWND_TOPMOST, SWP_NOACTIVATE)` |
| `collectionBehavior canJoinAllSpaces/fullScreenAuxiliary` | *(no analog — single desktop; always-on-top only)* |
| `isOpaque=false`+`backgroundColor=.clear` | `WS_EX_LAYERED` + `UpdateLayeredWindow` (premultiplied ARGB) |
| `hasShadow=false` | inherent to a layered window |
| `ignoresMouseEvents=true` default | `WS_EX_TRANSPARENT` |
| `syncCursorState` poll flipping `ignoresMouseEvents` | `GetCursorPos` poll → toggle `WS_EX_TRANSPARENT` via `SetWindowLong` |
| `canBecomeKey` armed only over box | temporarily clear `WS_EX_NOACTIVATE` + focus while editing (M4) |
| `NSStatusItem`+popover | notification-area icon + frameless popover window |
| `LSUIElement` agent app | `WS_EX_TOOLWINDOW` orb (no taskbar button) + process stays resident |
| Carbon `RegisterEventHotKey` / solo-`fn` | `WH_KEYBOARD_LL` hook (bare-modifier hold-to-talk); `RegisterHotKey` only for full chords |
| `HostTextInserter` paste into active app | `clipboard` + `SendInput` Ctrl+V (no Accessibility gate) |
| Electron IPC / preload / contextBridge | in-process `async`/`await` + events + DI interfaces (no IPC bus) |
| React component / JSX | WPF XAML + MVVM |
| Canvas 2D | WPF-hosted 2D surface — Win2D or WriteableBitmap/DrawingContext (port the algorithm) |
| electron-vite / electron-builder / electron-updater | `dotnet publish` + MSIX/MSI installer + MSIX/Squirrel auto-update |
| Sparkle / notarization / entitlements | EV code-signing; no notarization/entitlement analog |

---

## 9. Top risks for the architects

1. **Global hotkey / hold-to-talk** — the built-in shortcut API can't do it; `WH_KEYBOARD_LL` on a dedicated thread required; `fn` doesn't exist. Default = Right-Ctrl hold (§6, R5/R8).
2. **The orb window** — a WPF transparent window can't be truly non-activating; use a native Win32 layered window (`UpdateLayeredWindow`) with `WS_EX_NOACTIVATE` and an invisible WPF host (§2, §7, R20; `orb-is-a-chip` memo).
3. **Dynamic click-through** — a `GetCursorPos` poll toggling `WS_EX_TRANSPARENT` (the `syncCursorState` port); no mouse-forward dependency (§2, §7).
4. **Hook/inject binaries + AV/SmartScreen** — EV-sign early (§5, R11).
5. **Alpha capture for the pixel gate** — composite the layered orb and the candidate over an identical background before diff; the desktop-behind blur is excluded (R1, R14).

**Deferred / v1 non-goals (this map):** MSIX-vs-MSI decision is M8; the maxi envelope swap +
in-box-editing activatable window is M4; auto-update feed is M8.

> **Not applicable — Windows-only.** The reference's entire cross-compile/Wine/Docker section, the
> macOS notarization loop, Linux AppImage/deb/rpm packaging, `setVisibleOnAllWorkspaces`, and every
> Wayland/X11 overlay caveat are removed. There is one build target: Windows (x64 + arm64).

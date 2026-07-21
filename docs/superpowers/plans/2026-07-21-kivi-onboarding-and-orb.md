# Kivi Onboarding Flow + Persistent Orb Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build Kivi's first-run onboarding flow (Google-login stub → real mic-permission check → config screen) gated by a persist-once flag, followed by a persistent bare dot-matrix kiwi orb that lives on the desktop — matching the `ui/` Figma exports — using the existing `Kivi.Core`/`Kivi.Platform` engine, plus one new engine capability (runtime hotkey rebinding).

**Architecture:** Phase A (`Kivi.Core`/`Kivi.Platform`, TDD where testable) adds `AppConfig` fields (onboarding flag, orb color, screen-context toggle, hotkey VK code), runtime hotkey rebinding on `IHotkeyService`, and a screen-context enable/disable gate in the orchestrator. Phase B (`Kivi.App`, WinUI3, build-gated) recovers the bare-bird orb from reverted commits and fixes its fullscreen-black bug, then builds the three onboarding pages + the startup gate in `App.xaml.cs`. No tray icon. No transcript-history features.

**Tech Stack:** .NET 8, C#, WinUI 3 (Microsoft.WindowsAppSDK 1.8.260710003), CommunityToolkit.Mvvm, CsWin32 (existing hotkey interop), xUnit (Phase A only).

## Global Constraints

- Dependency direction: `Kivi.Core` has zero Windows/UI dependencies; `Kivi.Platform`/`Kivi.App` depend on `Kivi.Core`, never the reverse.
- Never log the API key, transcript text, audio bytes, or captured screen context.
- Hotkey default is Right Ctrl (`VK_RCONTROL = 0xA3`). Never reintroduce `fn`/Mac-specific copy in any UI text.
- WindowsAppSDK version is `1.8.260710003` (already set; do not change it).
- Visual fidelity: Login/Permissions/orb match the `ui/` Figma exports pixel-faithfully with the agreed Windows adjustments (no "Continue with Apple"; hotkey shown as "Right Ctrl"; "Accessibility" → informational "Screen context", auto-granted). Config screen uses the design's card/grid/type system but omits controls with no backend rather than showing them disabled. All colors/spacing/radii/type come from `Kivi.App/Themes/Tokens.xaml` (semantic keys) — never hand-picked literals.
- The orb is the BARE dot-matrix kiwi silhouette — no pill/box/rounded container, no background fill. It scales in size and recolors per state. Transcript (if built) is a separate floating surface above the bird.
- TDD for `Kivi.Core`/`Kivi.Platform` logic that is unit-testable (config, orchestrator gate). Hotkey interop and all `Kivi.App` WinUI3 code are build-gated (compile + manual smoke test), matching the existing `Kivi.Platform` convention.
- Git commits use `git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit`, every message ending with a `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>` trailer.

---

## File Structure

- `Kivi.Core/Config/AppConfig.cs` — add `OnboardingCompleted`, `OrbAccentColor`, `ScreenContextEnabled`, `HotkeyVirtualKeyCode` fields.
- `Kivi.Core/Abstractions/IHotkeyService.cs` — add `SetHotkey(uint virtualKeyCode)`.
- `Kivi.Platform/Hotkey/LowLevelKeyboardHookService.cs` — mutable bound-key state + `SetHotkey` impl.
- `Kivi.Core/Orchestration/DictationOrchestrator.cs` — gate `CaptureContextAsync` on `ScreenContextEnabled`.
- `Kivi.Core.Tests/AppConfigTests.cs`, `Kivi.Core.Tests/OrchestratorTests.cs` — new tests.
- `Kivi.App/Controls/KiviOrbControl.cs`, `Kivi.App/Assets/Icons/kivi-mask.png`, `Kivi.App/Interop/NativeMethods.cs`, `Kivi.App/Views/OverlayWindow.xaml(.cs)` — recovered from reverted commits, fixed for bare-bird + black-window bug.
- `Kivi.App/Views/Onboarding/OnboardingWindow.xaml(.cs)`, `LoginPage.xaml(.cs)`, `PermissionsPage.xaml(.cs)`, `ConfigPage.xaml(.cs)` — new onboarding UI.
- `Kivi.App/ViewModels/ConfigViewModel.cs` — config screen state bound to AppConfig.
- `Kivi.App/Services/StartupLauncher.cs` — launch-at-login registry helper.
- `Kivi.App/App.xaml.cs` — startup gate + orb creation.

---

## Phase A — Kivi.Core / Kivi.Platform (TDD where testable)

### Task 1: AppConfig fields + screen-context orchestrator gate + hotkey rebinding

**Files:**
- Modify: `Kivi.Core/Config/AppConfig.cs`
- Modify: `Kivi.Core/Abstractions/IHotkeyService.cs`
- Modify: `Kivi.Platform/Hotkey/LowLevelKeyboardHookService.cs`
- Modify: `Kivi.Core/Orchestration/DictationOrchestrator.cs`
- Test: `Kivi.Core.Tests/AppConfigTests.cs`, `Kivi.Core.Tests/OrchestratorTests.cs`

**Interfaces:**
- Consumes: existing `AppConfig`, `IHotkeyService`, `DictationOrchestrator`, `IScreenContextProvider`.
- Produces:
  - `AppConfig.OnboardingCompleted` (bool, default false), `AppConfig.OrbAccentColor` (string, default `"#41691E"`), `AppConfig.ScreenContextEnabled` (bool, default true), `AppConfig.HotkeyVirtualKeyCode` (uint, default `0xA3` = Right Ctrl).
  - `IHotkeyService.SetHotkey(uint virtualKeyCode)` — changes the bound key at runtime.
  - `DictationOrchestrator.OnHoldStarted` now only calls `_context.CaptureContextAsync` when `_config.ScreenContextEnabled`; otherwise `_contextTask` is set to `Task.FromResult("")`.
  Task 4 (ConfigViewModel) consumes all four AppConfig fields and `SetHotkey`. Task 6 (App.xaml.cs startup) consumes `OnboardingCompleted` and re-applies `HotkeyVirtualKeyCode` via `SetHotkey` on launch.

- [ ] **Step 1a: Write failing AppConfig test**

Add to `Kivi.Core.Tests/AppConfigTests.cs`:

```csharp
[Fact]
public void Default_HasOnboardingAndOrbAndContextAndHotkeyDefaults()
{
    var c = AppConfig.Default();
    Assert.False(c.OnboardingCompleted);
    Assert.Equal("#41691E", c.OrbAccentColor);
    Assert.True(c.ScreenContextEnabled);
    Assert.Equal(0xA3u, c.HotkeyVirtualKeyCode);
}
```

- [ ] **Step 1b: Run it — expect FAIL**

Run: `dotnet test Kivi.Core.Tests --filter Default_HasOnboardingAndOrbAndContextAndHotkeyDefaults`
Expected: FAIL — compile error, those properties don't exist yet.

- [ ] **Step 1c: Add the AppConfig fields**

In `Kivi.Core/Config/AppConfig.cs`, add these properties alongside the existing ones (before `Default()`):

```csharp
    public bool OnboardingCompleted { get; set; }
    public string OrbAccentColor { get; set; } = "#41691E";
    public bool ScreenContextEnabled { get; set; } = true;
    public uint HotkeyVirtualKeyCode { get; set; } = 0xA3; // VK_RCONTROL (Right Ctrl)
```

(Do not add these to `Validate()` — none is a URL. `OrbAccentColor` is stored as-is; the UI is responsible for only ever writing valid hex to it.)

- [ ] **Step 1d: Run it — expect PASS**

Run: `dotnet test Kivi.Core.Tests --filter Default_HasOnboardingAndOrbAndContextAndHotkeyDefaults`
Expected: PASS.

- [ ] **Step 1e: Add SetHotkey to the interface**

Edit `Kivi.Core/Abstractions/IHotkeyService.cs`:

```csharp
namespace Kivi.Core.Abstractions;

public interface IHotkeyService
{
    event Action HoldStarted;
    event Action HoldEnded;
    void Start();
    void Stop();
    void SetHotkey(uint virtualKeyCode);
}
```

- [ ] **Step 1f: Implement SetHotkey in the hook service**

In `Kivi.Platform/Hotkey/LowLevelKeyboardHookService.cs`, change the hardcoded `VK_RCONTROL` constant usage to mutable state and add `SetHotkey`. Replace the `private const uint VK_RCONTROL = 0xA3;` line with:

```csharp
    private uint _boundVk = 0xA3; // VK_RCONTROL default; changeable via SetHotkey
```

In `HookCallback`, change `if (data.vkCode == VK_RCONTROL)` to `if (data.vkCode == _boundVk)`. Add the method (after `Dispose`):

```csharp
    public void SetHotkey(uint virtualKeyCode)
    {
        _boundVk = virtualKeyCode;
        // If a hold was in progress on the old key, clear it so state doesn't stick.
        if (_held) { _held = false; HoldEnded?.Invoke(); }
    }
```

- [ ] **Step 1g: Write failing orchestrator screen-context-gate test**

Add to `Kivi.Core.Tests/OrchestratorTests.cs` a fake context that records whether it was called, then a test. First add this fake to `Kivi.Core.Tests/Fakes/Fakes.cs` (append):

```csharp
public sealed class SpyContext : Kivi.Core.Abstractions.IScreenContextProvider
{
    public int Calls;
    public Task<string> CaptureContextAsync(CancellationToken ct) { Calls++; return Task.FromResult("App: Notepad"); }
}
```

Then the test in `OrchestratorTests.cs`:

```csharp
[Fact]
public async Task ScreenContextDisabled_SkipsContextCapture()
{
    var cfg = AppConfig.Default();
    cfg.ScreenContextEnabled = false;
    var ctx = new SpyContext();
    var paste = new SpyPaste();
    using var metrics = new KiviMetrics();
    var orch = new DictationOrchestrator(new FakeHotkey(), new FakeAudio(), ctx,
        new StubStt(), new StubPolish(), paste, cfg, metrics);
    orch.Start();

    var hotkey = (FakeHotkey)typeof(DictationOrchestrator)
        .GetField("_hotkey", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
        .GetValue(orch)!;
    hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);

    Assert.Equal(0, ctx.Calls); // context capture skipped entirely
    Assert.Equal("Hello there.", paste.Pasted); // pipeline still completes
}
```

(If reflection-to-get-the-hotkey feels brittle, the simpler form is to keep a reference to the `FakeHotkey` you constructed and pass it in directly — rewrite to construct `var fakeHotkey = new FakeHotkey();` and pass it to the orchestrator, then call `fakeHotkey.FireStart()`. Use that simpler form; the reflection variant above is only shown in case the constructor wiring differs. Prefer: construct the `FakeHotkey` locally and use it directly.)

Simpler canonical version to actually use:

```csharp
[Fact]
public async Task ScreenContextDisabled_SkipsContextCapture()
{
    var cfg = AppConfig.Default();
    cfg.ScreenContextEnabled = false;
    var ctx = new SpyContext();
    var hotkey = new FakeHotkey();
    var paste = new SpyPaste();
    using var metrics = new KiviMetrics();
    var orch = new DictationOrchestrator(hotkey, new FakeAudio(), ctx,
        new StubStt(), new StubPolish(), paste, cfg, metrics);
    orch.Start();

    hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);

    Assert.Equal(0, ctx.Calls);
    Assert.Equal("Hello there.", paste.Pasted);
}
```

- [ ] **Step 1h: Run it — expect FAIL**

Run: `dotnet test Kivi.Core.Tests --filter ScreenContextDisabled_SkipsContextCapture`
Expected: FAIL — `ctx.Calls` is 1, because `OnHoldStarted` currently always calls `CaptureContextAsync`.

- [ ] **Step 1i: Gate the context capture**

In `Kivi.Core/Orchestration/DictationOrchestrator.cs`, change `OnHoldStarted`:

```csharp
    private void OnHoldStarted()
    {
        _cts = new CancellationTokenSource();
        SetState(RecordingState.Listening);
        _contextTask = _config.ScreenContextEnabled
            ? _context.CaptureContextAsync(_cts.Token)
            : Task.FromResult("");
        _ = _audio.StartRecordingAsync(_cts.Token);
    }
```

- [ ] **Step 1j: Run it — expect PASS, then full suite**

Run: `dotnet test Kivi.Core.Tests --filter ScreenContextDisabled_SkipsContextCapture`
Expected: PASS.
Then run the whole suite: `dotnet test Kivi.Core.Tests`
Expected: all tests pass (the existing `FullDictation_...` test uses a `FakeContext` and `ScreenContextEnabled` defaults true, so it still exercises the capture path unchanged).

- [ ] **Step 1k: Confirm whole solution builds**

Run: `dotnet build`
Expected: Build succeeds. `LowLevelKeyboardHookService`'s `SetHotkey` addition compiles; no other `IHotkeyService` implementer exists besides the fake in tests — **update `Kivi.Core.Tests/Fakes/Fakes.cs`'s `FakeHotkey` to implement `SetHotkey`** (add `public void SetHotkey(uint virtualKeyCode) { }` — a no-op is fine for the fake). If the build fails on `FakeHotkey` not implementing the interface, that's the fix.

- [ ] **Step 1l: Commit**

```bash
git add Kivi.Core/Config/AppConfig.cs Kivi.Core/Abstractions/IHotkeyService.cs Kivi.Platform/Hotkey/LowLevelKeyboardHookService.cs Kivi.Core/Orchestration/DictationOrchestrator.cs Kivi.Core.Tests/AppConfigTests.cs Kivi.Core.Tests/OrchestratorTests.cs Kivi.Core.Tests/Fakes/Fakes.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(core): onboarding/orb/context config + runtime hotkey rebinding

Adds AppConfig.OnboardingCompleted/OrbAccentColor/ScreenContextEnabled/
HotkeyVirtualKeyCode. IHotkeyService gains SetHotkey(uint) for runtime
rebinding (LowLevelKeyboardHookService now checks a mutable bound VK
instead of a hardcoded Right-Ctrl constant). DictationOrchestrator
skips screen-context capture entirely when ScreenContextEnabled is off.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase B — Kivi.App WinUI3 (build-gated)

### Task 2: Recover the bare-bird orb (KiviOrbControl + OverlayWindow), fix the black-window bug

**Files:**
- Create: `Kivi.App/Controls/KiviOrbControl.cs` (recover from commit `63f81f7`, then modify for bare-bird)
- Create: `Kivi.App/Assets/Icons/kivi-mask.png` (recover from commit `63f81f7`)
- Create: `Kivi.App/Interop/NativeMethods.cs` (recover from commit `468a5a7`, unchanged)
- Create: `Kivi.App/Views/OverlayWindow.xaml(.cs)` (recover from commit `468a5a7`, then modify)
- Create: `Kivi.App/ViewModels/OverlayViewModel.cs` (recover from commit `e3f774e`, unchanged)
- Modify: `Kivi.App/Kivi.App.csproj` (re-add the Content/Page items the recovered files need)
- Modify: `Kivi.App/App.xaml.cs` (create the overlay window at startup — temporary wiring, finalized in Task 6)

**Interfaces:**
- Consumes: `IDictationOrchestrator` (existing), `RecordingState` (7 values), the `Overlay*Brush` tokens in `Themes/Tokens.xaml`, `AppConfig.OrbAccentColor` (Task 1).
- Produces: a persistent, bottom-center, borderless, click-through `OverlayWindow` hosting a `KiviOrbControl` that renders the bare dot-matrix kiwi (no container), scaling + recoloring per `RecordingState`. Task 6 wires this into the real startup gate.

- [ ] **Step 2a: Recover the four orb files verbatim from git history**

Run these to restore the reverted files exactly as they were (they were sound; we fix them in later steps):

```bash
git checkout 63f81f7 -- Kivi.App/Controls/KiviOrbControl.cs Kivi.App/Assets/Icons/kivi-mask.png
git checkout 468a5a7 -- Kivi.App/Interop/NativeMethods.cs Kivi.App/Views/OverlayWindow.xaml Kivi.App/Views/OverlayWindow.xaml.cs
git checkout e3f774e -- Kivi.App/ViewModels/OverlayViewModel.cs
```

- [ ] **Step 2b: Re-add the csproj Content/Page registrations**

`Kivi.App.csproj` has `EnableDefaultPageItems=false`, so recovered XAML/assets must be explicitly registered. Add to `Kivi.App/Kivi.App.csproj` (inside an `<ItemGroup>`):

```xml
  <ItemGroup>
    <Content Include="Assets\Icons\kivi-mask.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
    <Page Update="Views\OverlayWindow.xaml">
      <Generator>MSBuild:Compile</Generator>
    </Page>
  </ItemGroup>
```

- [ ] **Step 2c: Build to confirm the recovered files compile as-is**

Run: `dotnet build Kivi.App`
Expected: Build succeeds. If `OverlayViewModel` references anything not present, read the file and confirm it only uses `IDictationOrchestrator`/`RecordingState`/`DispatcherQueue` (all present). Do not run the app yet — the black-window bug still needs fixing.

- [ ] **Step 2d: Fix KiviOrbControl to render the BARE bird (no container assumptions) tinted by OrbAccentColor**

Read the recovered `Kivi.App/Controls/KiviOrbControl.cs`. It already draws bare `Ellipse` dots sampled from the mask onto a transparent `Canvas` (no background) — this is correct for bare-bird. The one change: the active-state color for Listening/Speaking/Done must resolve from `AppConfig.OrbAccentColor` rather than the fixed `OverlayListeningBrush`/`OverlaySpeakingBrush`/`OverlayDoneBrush` tokens. Modify `ApplyStateColor()` so those three states use a brush built from `App`-level state (the accent color), while Idle/Processing/Waiting/Error keep their fixed `Overlay*Brush` tokens. Concretely, add a static accent hook the control reads:

```csharp
    // Set once at startup from AppConfig.OrbAccentColor; used for the "your voice" states.
    public static Microsoft.UI.Xaml.Media.Brush? AccentBrush { get; set; }
```

and in `ApplyStateColor()`, for `RecordingState.Listening`, `.Speaking`, `.Done`: use `AccentBrush ?? <the existing token brush>` instead of the token lookup. Keep Idle/Processing/Waiting/Error exactly as the recovered code has them (fixed `Overlay*Brush` tokens).

- [ ] **Step 2e: Fix the OverlayWindow fullscreen-black bug**

Read the recovered `Kivi.App/Views/OverlayWindow.xaml.cs`. The reverted version presented as a fullscreen black window disrupting the taskbar. Root-cause and fix — the fix must ensure:
1. The window's content root is fully transparent (the `Grid`/root has `Background="Transparent"` in `OverlayWindow.xaml` — confirm it's there; the recovered XAML should already have `Background="Transparent"` on `Root`).
2. `AppWindow.Resize(...)` to the bird's small drawn size runs BEFORE `_appWindow.Show(...)`, so the window never briefly presents at a default (large) size — a large transparent-but-not-yet-transparent window flashing black is the likely cause. Ensure the presenter/click-through/resize setup in the constructor completes before any `Show`.
3. A transparent backdrop is actually set. In WinUI3/WinAppSDK 1.8, a `Window` with no explicit backdrop can render an opaque default. Set `this.SystemBackdrop = null` and ensure the root element is `Background="Transparent"`; if the window still renders opaque black, apply a transparent `Microsoft.UI.Composition` backdrop or set the `AppWindow` to layered via the existing `WS_EX_LAYERED` + `SetLayeredWindowAttributes(hwnd, 0, 255, LWA_ALPHA)` call (already in `MakeClickThrough`) — verify that call runs and that the HWND actually becomes layered (the reverted code called it; confirm it executes before first paint).

Replace the reverted `ApplyState()`'s posture-container sizing (rest pill 39×15 / woken 61×61 / satellites 23×23 / box 322×108) with **bare-bird size scaling**: a small size at Idle, a larger size for active states, sizing the window to fit the bird's drawn dimensions. Exact sizes (window px):

```csharp
    private (int w, int h) SizeFor(RecordingState s) => s switch
    {
        RecordingState.Idle => (48, 64),   // small resting bird
        _                    => (96, 130),  // larger active bird (same 24-col aspect ratio)
    };
```

The bird's aspect ratio (from the 120×162 mask trace) is ~0.74; both sizes above preserve it (48/64 ≈ 0.75, 96/130 ≈ 0.74). Update `ApplyState()` to `_appWindow.Resize(new SizeInt32(w, h))` from `SizeFor(state)`, set `Orb.State = _vm.State`, and keep the bottom-center anchoring. Remove any `Posture` property references (the bare bird has no postures — if the recovered `KiviOrbControl` still has a `Posture` DP, either leave it unused or remove it; do not drive it from `OverlayWindow`).

- [ ] **Step 2f: Temporarily wire the overlay into App.xaml.cs for smoke testing**

In `Kivi.App/App.xaml.cs`, at the end of `OnLaunched` (after `orchestrator.Start();`), add temporary wiring (finalized in Task 6):

```csharp
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Controls.KiviOrbControl.AccentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            ColorFromHex(appConfig.OrbAccentColor));
        var overlayVm = new ViewModels.OverlayViewModel(orchestrator, dispatcher);
        _overlayWindow = new Views.OverlayWindow(overlayVm);
```

Add the field `private Views.OverlayWindow? _overlayWindow;` and a small hex helper:

```csharp
    private static Windows.UI.Color ColorFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        return Windows.UI.Color.FromArgb(255, r, g, b);
    }
```

Add `using Microsoft.UI.Dispatching;` if not present.

- [ ] **Step 2g: Build + manual smoke test the orb**

Run: `dotnet build Kivi.App` — expect 0 errors.
Then `dotnet run --project Kivi.App`. **Manual check (report honestly what you can/can't verify):** confirm a small kiwi-shaped cluster of dots appears bottom-center of the screen with NO black box, NO fullscreen black window, NO taskbar disruption. Hold Right Ctrl and speak — the bird should grow and change color, then shrink back. If you have no screenshot capability, at minimum confirm the app starts without the fullscreen-black regression (the whole point of this task's fix); if the black window still appears, that is a BLOCKED condition — report it with what you observed rather than committing a known-broken overlay.

- [ ] **Step 2h: Commit**

```bash
git add Kivi.App/Controls/KiviOrbControl.cs Kivi.App/Assets/Icons/kivi-mask.png Kivi.App/Interop/NativeMethods.cs Kivi.App/Views/OverlayWindow.xaml Kivi.App/Views/OverlayWindow.xaml.cs Kivi.App/ViewModels/OverlayViewModel.cs Kivi.App/Kivi.App.csproj Kivi.App/App.xaml.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): bare-bird orb overlay (recovered + fixed black-window bug)

Recovers KiviOrbControl/OverlayWindow/NativeMethods/OverlayViewModel
from the reverted commits, then fixes them for the approved bare-bird
design: the orb is the dot-matrix kiwi silhouette floating directly on
the desktop (no pill/box container), scaling small->large and
recoloring per state. Active states (listening/speaking/done) tint with
AppConfig.OrbAccentColor; system states keep fixed tokens. Fixes the
fullscreen-black-window regression by ensuring transparent backdrop +
resize-before-show.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: Onboarding shell + Login page + Permissions page

**Files:**
- Create: `Kivi.App/Views/Onboarding/OnboardingWindow.xaml(.cs)`
- Create: `Kivi.App/Views/Onboarding/LoginPage.xaml(.cs)`
- Create: `Kivi.App/Views/Onboarding/PermissionsPage.xaml(.cs)`
- Create: `Kivi.App/Services/MicPermission.cs`
- Modify: `Kivi.App/Kivi.App.csproj` (register the new pages/window)

**Interfaces:**
- Consumes: design tokens in `Themes/Tokens.xaml`, the recovered `KiviOrbControl` (Task 2) for the wordmark/mark, `MicPermission.CheckAsync()`.
- Produces:
  - `OnboardingWindow` — a normal chromed window hosting a `Frame`, with `public void NavigateTo(Type page)` and an `event Action? Completed` raised when the Config page finishes (Config page added in Task 4).
  - `LoginPage` — Google-stub + email-stub buttons that call `OnboardingWindow.NavigateTo(typeof(PermissionsPage))`.
  - `PermissionsPage` — real mic check via `MicPermission`, "Continue" navigates to the Config page (Task 4) only when mic is granted.
  - `MicPermission.CheckAsync()` returns `Task<bool>` (granted); `MicPermission.RequestAsync()` returns `Task<bool>`; `MicPermission.OpenSettings()` opens `ms-settings:privacy-microphone`.
  Task 4 adds `ConfigPage` and wires `OnboardingWindow`'s nav to it. Task 6 constructs `OnboardingWindow` and handles `Completed`.

- [ ] **Step 3a: Mic permission helper**

Create `Kivi.App/Services/MicPermission.cs`. Use the WinRT `AppCapability` API (available via WinAppSDK) to check/request microphone access, with a graceful fallback:

```csharp
using Windows.Security.Authorization.AppCapabilityAccess;

namespace Kivi.App.Services;

public static class MicPermission
{
    public static async Task<bool> CheckAsync()
    {
        try
        {
            var cap = AppCapability.Create("microphone");
            var status = cap.CheckAccess();
            if (status == AppCapabilityAccessStatus.Allowed) return true;
            return false;
        }
        catch { return true; } // if the capability API is unavailable, don't hard-block dictation
    }

    public static async Task<bool> RequestAsync()
    {
        try
        {
            var cap = AppCapability.Create("microphone");
            var status = await cap.RequestAccessAsync();
            return status == AppCapabilityAccessStatus.Allowed;
        }
        catch { return true; }
    }

    public static void OpenSettings()
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-microphone"));
    }
}
```

(Fail-open on exception is deliberate: an unpackaged app on some Windows configs can't query `AppCapability`, and we must not permanently block a user whose mic actually works. The real gate is the actual capture succeeding at dictation time.)

- [ ] **Step 3b: Onboarding shell window**

Create `Kivi.App/Views/Onboarding/OnboardingWindow.xaml`:

```xml
<Window
    x:Class="Kivi.App.Views.Onboarding.OnboardingWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid Background="{ThemeResource KiviSurfaceAltBrush}">
        <Frame x:Name="RootFrame"/>
    </Grid>
</Window>
```

Create `Kivi.App/Views/Onboarding/OnboardingWindow.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.Onboarding;

public sealed partial class OnboardingWindow : Window
{
    public event Action? Completed;

    public OnboardingWindow(bool startAtPermissions)
    {
        InitializeComponent();
        Title = "Kivi";
        RootFrame.Navigate(startAtPermissions ? typeof(PermissionsPage) : typeof(LoginPage), this);
    }

    public void NavigateTo(Type page) => RootFrame.Navigate(page, this);

    public void RaiseCompleted() => Completed?.Invoke();
}
```

(Pages receive the `OnboardingWindow` as their navigation parameter so they can call `NavigateTo`/`RaiseCompleted`.)

- [ ] **Step 3c: Login page (pixel-faithful, Google + email stubs, no Apple)**

Create `Kivi.App/Views/Onboarding/LoginPage.xaml` matching the `ui/04 - mockups.png` login frame — centered wordmark, tagline, dark Google pill, email link, mono footer. All colors/spacing from tokens:

```xml
<Page
    x:Class="Kivi.App.Views.Onboarding.LoginPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:Kivi.App.Controls"
    Background="{ThemeResource KiviSurfaceAltBrush}">
    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="0">
        <controls:KiviOrbControl x:Name="Mark" Width="72" Height="98" HorizontalAlignment="Center"/>
        <TextBlock Text="kivi" HorizontalAlignment="Center"
                   FontFamily="{ThemeResource KiviWordmarkFontFamily}"
                   FontSize="46" FontWeight="Medium"
                   Foreground="{ThemeResource KiviTextPrimaryBrush}"
                   Margin="0,22,0,0"/>
        <TextBlock Text="Your voice, polished." HorizontalAlignment="Center"
                   FontFamily="{ThemeResource KiviFontFamily}" FontSize="17"
                   Foreground="{ThemeResource KiviTextSecondaryBrush}" Margin="0,6,0,0"/>
        <Button x:Name="GoogleButton" Click="OnGoogle" Margin="0,40,0,0"
                HorizontalAlignment="Stretch" Width="300"
                Background="{ThemeResource KiviBrandInkBrush}"
                Foreground="{ThemeResource KiviSurfaceBrush}"
                CornerRadius="{ThemeResource KiviRadiusFull}" Padding="12,10">
            <TextBlock Text="Continue with Google" FontFamily="{ThemeResource KiviFontFamily}"
                       FontSize="14" FontWeight="Medium"/>
        </Button>
        <HyperlinkButton x:Name="EmailButton" Click="OnEmail" HorizontalAlignment="Center"
                         Content="Use work email instead" Margin="0,18,0,0"
                         Foreground="{ThemeResource KiviTextSecondaryBrush}"/>
        <TextBlock Text="free &amp; unlimited during launch — your words stay yours"
                   HorizontalAlignment="Center" FontFamily="{ThemeResource KiviMonoFontFamily}"
                   FontSize="11" Foreground="{ThemeResource KiviTextTertiaryBrush}" Margin="0,34,0,0"/>
    </StackPanel>
</Page>
```

Create `Kivi.App/Views/Onboarding/LoginPage.xaml.cs`:

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

public sealed partial class LoginPage : Page
{
    private OnboardingWindow? _host;
    public LoginPage() => InitializeComponent();
    protected override void OnNavigatedTo(NavigationEventArgs e) { _host = e.Parameter as OnboardingWindow; }
    private void OnGoogle(object s, RoutedEventArgs e) => _host?.NavigateTo(typeof(PermissionsPage));
    private void OnEmail(object s, RoutedEventArgs e) => _host?.NavigateTo(typeof(PermissionsPage));
}
```

**Note on tokens:** this references `KiviBrandInkBrush` and `KiviTextTertiaryBrush`. Check `Themes/Tokens.xaml` — if `KiviBrandInkBrush` or `KiviTextTertiaryBrush` semantic brushes don't exist yet, add them to both Light and Dark `ThemeDictionaries` (BrandInk → `KiviColorBrandInk`/`KiviColorBrandInkDark`; TextTertiary → `KiviColorFg3`/`KiviColorFg3Dark`), following the existing brush-definition pattern in that file. Do not hardcode the hex in the view.

- [ ] **Step 3d: Permissions page (pixel-faithful, real mic check)**

Create `Kivi.App/Views/Onboarding/PermissionsPage.xaml` matching the "two permissions, then you talk" card frame — a centered card with heading, subcopy, a Microphone row and a Screen-context row (each with an icon, label, subtext, and a state chip), the "Right Ctrl" hotkey badge, footer copy, and a Continue button. Use tokens throughout. (Full XAML — write it to match the frame's layout: `Border` card with `CornerRadius="{ThemeResource KiviRadiusLg}"`, inner `StackPanel`, two permission rows as `Grid`s with icon/text/chip columns, the hotkey badge as a small bordered `Border` with mono "R Ctrl" text, and the Continue `Button` styled like the Google button.) The Microphone chip text/color is bound in code-behind from the real check; the Screen-context chip is always "granted" (informational).

Create `Kivi.App/Views/Onboarding/PermissionsPage.xaml.cs`:

```csharp
using Kivi.App.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

public sealed partial class PermissionsPage : Page
{
    private OnboardingWindow? _host;
    public PermissionsPage() => InitializeComponent();

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        _host = e.Parameter as OnboardingWindow;
        await RefreshMicAsync();
    }

    private async Task RefreshMicAsync()
    {
        bool granted = await MicPermission.CheckAsync();
        if (!granted) granted = await MicPermission.RequestAsync();
        MicChip.Text = granted ? "granted" : "denied";
        // MicChip styling: granted -> KiviPositive tokens; denied -> KiviDanger tokens (set in code)
        ContinueButton.IsEnabled = granted;
        OpenSettingsLink.Visibility = granted ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenSettings(object s, RoutedEventArgs e) => MicPermission.OpenSettings();
    private async void OnRecheck(object s, RoutedEventArgs e) => await RefreshMicAsync();
    private void OnContinue(object s, RoutedEventArgs e) => _host?.NavigateTo(typeof(ConfigPage));
}
```

(`ConfigPage` is created in Task 4. This file references it by type; if building Task 3 in isolation before Task 4 exists, temporarily reference `LoginPage` instead and fix in Task 4 — but under subagent-driven execution Tasks 3 and 4 run in sequence, so create a minimal empty `ConfigPage` stub in Task 3 if needed to compile, which Task 4 fills in. Preferred: Task 3 creates the `ConfigPage.xaml(.cs)` empty shell (a `Page` that compiles) so the `NavigateTo(typeof(ConfigPage))` reference resolves; Task 4 fills in its real content.)

- [ ] **Step 3e: Register pages in csproj, build**

Add `<Page>` entries for `OnboardingWindow.xaml`, `LoginPage.xaml`, `PermissionsPage.xaml`, `ConfigPage.xaml` (the stub) to `Kivi.App.csproj` (same `<Page Update="..."><Generator>MSBuild:Compile</Generator></Page>` pattern). Run `dotnet build Kivi.App` — expect 0 errors.

- [ ] **Step 3f: Commit**

```bash
git add Kivi.App/Views/Onboarding/ Kivi.App/Services/MicPermission.cs Kivi.App/Kivi.App.csproj Kivi.App/Themes/Tokens.xaml
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): onboarding shell + Login and Permissions pages

OnboardingWindow hosts a Frame navigating Login -> Permissions ->
Config. Login is a UI stub (Google/email both advance, no OAuth, no
Apple button). Permissions does a real Windows mic-capability check
(fail-open if the API is unavailable) with open-settings/recheck
affordances; Screen-context is informational/auto-granted; hotkey
badge reads "Right Ctrl". Pixel-faithful to the ui/ frames via tokens.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Config page + ConfigViewModel + launch-at-login

**Files:**
- Modify: `Kivi.App/Views/Onboarding/ConfigPage.xaml(.cs)` (fill in the stub from Task 3)
- Create: `Kivi.App/ViewModels/ConfigViewModel.cs`
- Create: `Kivi.App/Services/StartupLauncher.cs`
- Create: `Kivi.App/Controls/HotkeyCaptureBox.cs`

**Interfaces:**
- Consumes: `AppConfig` + `IAppConfigStore` + `ISecretStore`-free (no secrets here) + `IHotkeyService` (all from `App.Services` DI, Task 1's new fields/SetHotkey), `StartupLauncher`, `HotkeyCaptureBox`.
- Produces: `ConfigPage` — the design's card grid with hotkey capture, orb accent color picker, language chips, launch-at-login toggle, screen-context toggle, and a "Done" button that persists config, sets `OnboardingCompleted=true`, and calls `OnboardingWindow.RaiseCompleted()`. `ConfigViewModel` exposes bindable config state. `StartupLauncher.SetEnabled(bool)` / `.IsEnabled()` manage the Windows Run-key. `HotkeyCaptureBox` is a control that captures a keypress and exposes its VK code.

- [ ] **Step 4a: Launch-at-login helper**

Create `Kivi.App/Services/StartupLauncher.cs` using the Windows registry `Run` key:

```csharp
using Microsoft.Win32;

namespace Kivi.App.Services;

public static class StartupLauncher
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Kivi";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, false);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
            ?? Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
```

- [ ] **Step 4b: Hotkey capture control**

Create `Kivi.App/Controls/HotkeyCaptureBox.cs` — a `Button` subclass that, when clicked, enters "listening" mode and captures the next key down, exposing its VK code:

```csharp
using Microsoft.UI.Xaml.Controls;
using Windows.System;

namespace Kivi.App.Controls;

public sealed class HotkeyCaptureBox : Button
{
    private bool _capturing;
    public uint VirtualKeyCode { get; private set; } = 0xA3;
    public event Action<uint>? HotkeyChanged;

    public HotkeyCaptureBox()
    {
        Click += (_, _) => { _capturing = true; Content = "press a key…"; };
        KeyDown += OnKeyDown;
    }

    public void SetInitial(uint vk) { VirtualKeyCode = vk; Content = Label(vk); }

    private void OnKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (!_capturing) return;
        _capturing = false;
        VirtualKeyCode = (uint)e.Key;
        Content = Label(VirtualKeyCode);
        HotkeyChanged?.Invoke(VirtualKeyCode);
        e.Handled = true;
    }

    private static string Label(uint vk) => vk switch
    {
        0xA3 => "Right Ctrl",
        0xA2 => "Left Ctrl",
        0xA0 => "Left Shift",
        0xA1 => "Right Shift",
        _    => ((VirtualKey)vk).ToString()
    };
}
```

- [ ] **Step 4c: ConfigViewModel**

Create `Kivi.App/ViewModels/ConfigViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;

namespace Kivi.App.ViewModels;

public partial class ConfigViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly IAppConfigStore _store;
    private readonly IHotkeyService _hotkey;

    public ConfigViewModel(AppConfig config, IAppConfigStore store, IHotkeyService hotkey)
    {
        _config = config; _store = store; _hotkey = hotkey;
        OrbAccentColor = config.OrbAccentColor;
        TranscriptionLanguage = config.TranscriptionLanguage ?? "auto";
        ScreenContextEnabled = config.ScreenContextEnabled;
        HotkeyVk = config.HotkeyVirtualKeyCode;
        LaunchAtLogin = Kivi.App.Services.StartupLauncher.IsEnabled();
    }

    [ObservableProperty] private string _orbAccentColor = "#41691E";
    [ObservableProperty] private string _transcriptionLanguage = "auto";
    [ObservableProperty] private bool _screenContextEnabled = true;
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private uint _hotkeyVk = 0xA3;

    partial void OnOrbAccentColorChanged(string v) => _config.OrbAccentColor = v;
    partial void OnTranscriptionLanguageChanged(string v)
        => _config.TranscriptionLanguage = v == "auto" ? null : v;
    partial void OnScreenContextEnabledChanged(bool v) => _config.ScreenContextEnabled = v;
    partial void OnLaunchAtLoginChanged(bool v) => Kivi.App.Services.StartupLauncher.SetEnabled(v);
    partial void OnHotkeyVkChanged(uint v) { _config.HotkeyVirtualKeyCode = v; _hotkey.SetHotkey(v); }

    public void Persist()
    {
        _config.OnboardingCompleted = true;
        _store.Save(_config);
    }
}
```

- [ ] **Step 4d: ConfigPage XAML (fill in the Task 3 stub)**

Replace the empty `ConfigPage.xaml` stub with the design's card grid — hotkey card (hosting a `HotkeyCaptureBox`), orb-color card (a row of color swatch buttons), language card (chips), behaviour card (launch-at-login + screen-context `ToggleSwitch`es), and a "Done" `Button`. Match the settings frame's card/grid/type styling via tokens. Omit Hey-kivi, press-hold-delay, memory, incognito, clear-history, sound-on-paste (not built). Bind everything to `ConfigViewModel`. The color swatches set `ViewModel.OrbAccentColor` to the chosen hex; the language chips set `ViewModel.TranscriptionLanguage`. "Done" calls `ViewModel.Persist()` then `_host.RaiseCompleted()`.

`ConfigPage.xaml.cs`:

```csharp
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Onboarding;

public sealed partial class ConfigPage : Page
{
    private OnboardingWindow? _host;
    public ConfigViewModel ViewModel { get; }

    public ConfigPage()
    {
        ViewModel = Kivi.App.App.Services.GetRequiredService<ConfigViewModel>();
        InitializeComponent();
        HotkeyBox.SetInitial(ViewModel.HotkeyVk);
        HotkeyBox.HotkeyChanged += vk => ViewModel.HotkeyVk = vk;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) { _host = e.Parameter as OnboardingWindow; }

    private void OnDone(object s, RoutedEventArgs e)
    {
        ViewModel.Persist();
        _host?.RaiseCompleted();
    }
}
```

(Requires `using Microsoft.Extensions.DependencyInjection;` for `GetRequiredService`. `ConfigViewModel` must be registered in DI — done in Task 6, but since Task 4 builds before Task 6 wires it, register `ConfigViewModel` in `App.xaml.cs`'s service collection as part of this task instead, or construct it directly. Preferred: register `services.AddTransient<ConfigViewModel>();` in `App.xaml.cs` in this task.)

- [ ] **Step 4e: Register ConfigViewModel in DI**

In `Kivi.App/App.xaml.cs`, add to the service registrations (near the other `AddSingleton` calls): `services.AddTransient<ViewModels.ConfigViewModel>();`

- [ ] **Step 4f: Build + manual smoke test**

Run: `dotnet build Kivi.App` — expect 0 errors.
`dotnet run --project Kivi.App` — the orb still shows (onboarding gate not wired until Task 6, so the app currently goes straight to the orb; that's fine for this task's build check). Report build success; full onboarding-flow smoke test happens in Task 6 once the gate is wired.

- [ ] **Step 4g: Commit**

```bash
git add Kivi.App/Views/Onboarding/ConfigPage.xaml Kivi.App/Views/Onboarding/ConfigPage.xaml.cs Kivi.App/ViewModels/ConfigViewModel.cs Kivi.App/Services/StartupLauncher.cs Kivi.App/Controls/HotkeyCaptureBox.cs Kivi.App/App.xaml.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): onboarding Config page (hotkey/color/language/toggles)

Config page in the design's card-grid style: real hotkey-capture
(rebinds via IHotkeyService.SetHotkey), orb accent color picker,
language chips (TranscriptionLanguage), launch-at-login (registry Run
key) and screen-context toggles. "Done" persists config + sets
OnboardingCompleted. Omits not-yet-supported controls per spec.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Startup gate — wire onboarding into App.xaml.cs

**Files:**
- Modify: `Kivi.App/App.xaml.cs`

**Interfaces:**
- Consumes: `AppConfig.OnboardingCompleted` + `HotkeyVirtualKeyCode` (Task 1), `OnboardingWindow` + its `Completed` event (Task 3), `MicPermission.CheckAsync` (Task 3), `OverlayWindow` (Task 2), `IHotkeyService.SetHotkey`.
- Produces: the final startup flow — onboarding gate → orb, with hotkey re-applied from config on every launch.

- [ ] **Step 5a: Implement the startup gate**

In `Kivi.App/App.xaml.cs`'s `OnLaunched`, replace the temporary orb-only wiring from Task 2's Step 2f with the full gate. After `orchestrator.Start();` and building the DI graph, apply the saved hotkey, then branch:

```csharp
        // Re-apply the user's saved hotkey on every launch.
        var hotkey = Services.GetRequiredService<IHotkeyService>();
        hotkey.SetHotkey(appConfig.HotkeyVirtualKeyCode);

        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        Controls.KiviOrbControl.AccentBrush = new Microsoft.UI.Xaml.Media.SolidColorBrush(
            ColorFromHex(appConfig.OrbAccentColor));

        async void ShowOrb()
        {
            var overlayVm = new ViewModels.OverlayViewModel(orchestrator, dispatcher);
            _overlayWindow = new Views.OverlayWindow(overlayVm);
            await Task.CompletedTask;
        }

        if (!appConfig.OnboardingCompleted)
        {
            var win = new Views.Onboarding.OnboardingWindow(startAtPermissions: false);
            win.Completed += () => { win.Close(); ShowOrb(); };
            win.Activate();
        }
        else
        {
            bool micOk = await Services.GetRequiredService<...>() // see note
                ; // replaced below
        }
```

Because `OnLaunched` is not `async` by default, and the mic re-check must be awaited, make the gate a separate `async` method invoked fire-and-forget from `OnLaunched`. Concretely, replace the branch above with a call to a new private async method:

```csharp
        _ = RunStartupGateAsync(appConfig, orchestrator, dispatcher);
```

and add:

```csharp
    private async Task RunStartupGateAsync(AppConfig appConfig, IDictationOrchestrator orchestrator, Microsoft.UI.Dispatching.DispatcherQueue dispatcher)
    {
        void ShowOrb()
        {
            var overlayVm = new ViewModels.OverlayViewModel(orchestrator, dispatcher);
            _overlayWindow = new Views.OverlayWindow(overlayVm);
        }

        if (!appConfig.OnboardingCompleted)
        {
            var win = new Views.Onboarding.OnboardingWindow(startAtPermissions: false);
            win.Completed += () => { win.Close(); ShowOrb(); };
            win.Activate();
            return;
        }

        // Onboarding done previously — but re-check mic; if revoked, re-show only Permissions.
        bool micOk = await Services.MicPermission.CheckAsync(); // static, see note
        if (!micOk)
        {
            var win = new Views.Onboarding.OnboardingWindow(startAtPermissions: true);
            win.Completed += () => { win.Close(); ShowOrb(); };
            win.Activate();
            return;
        }

        ShowOrb();
    }
```

Fix the `MicPermission` reference: it's a static class `Kivi.App.Services.MicPermission`, so call `await Kivi.App.Services.MicPermission.CheckAsync();`. Note: when `startAtPermissions: true` and onboarding was already completed, the Permissions page's "Continue" navigates to `ConfigPage` per Task 3's code — that's wrong for the re-check case (we don't want to re-run Config). Handle this by having the Permissions page, when it is the re-check entry point, call `RaiseCompleted()` directly instead of navigating to Config. Pass that intent through `OnboardingWindow` (add a `bool _permissionsOnly` set from the `startAtPermissions` constructor arg; expose it as a property the `PermissionsPage` reads in `OnNavigatedTo` to decide whether "Continue" goes to Config or calls `RaiseCompleted()`).

Add to `OnboardingWindow`: `public bool PermissionsOnly { get; }` set from the constructor's `startAtPermissions`. In `PermissionsPage.OnContinue`: `if (_host?.PermissionsOnly == true) _host.RaiseCompleted(); else _host?.NavigateTo(typeof(ConfigPage));`

- [ ] **Step 5b: Build + full manual smoke test**

Run: `dotnet build Kivi.App` — expect 0 errors.
`dotnet run --project Kivi.App`. **First run** (fresh `%APPDATA%\Kivi\settings.json` or with `OnboardingCompleted=false`): confirm the onboarding window appears at Login → clicking Google advances to Permissions → mic check shows real state → Continue advances to Config → Done closes the window and the orb appears. **Second run** (now `OnboardingCompleted=true`): confirm onboarding is skipped entirely and the orb appears directly. Report honestly what's verifiable without screenshots (at minimum: first run shows a window, second run shows none / goes straight to orb; check `settings.json` has `OnboardingCompleted: true` after first run).

- [ ] **Step 5c: Commit**

```bash
git add Kivi.App/App.xaml.cs Kivi.App/Views/Onboarding/OnboardingWindow.xaml.cs Kivi.App/Views/Onboarding/PermissionsPage.xaml.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): startup onboarding gate + persist-once flag

App.xaml.cs now gates on AppConfig.OnboardingCompleted: first launch
runs Login->Permissions->Config then shows the orb; later launches skip
straight to the orb. A revoked mic permission re-shows only the
Permissions page (which then completes without re-running Config). The
saved hotkey is re-applied via SetHotkey on every launch.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Whole-flow manual verification + visual fidelity pass

**Files:** none modified — verification only.

**Interfaces:** none new.

- [ ] **Step 6a: Full end-to-end manual test**

Run `dotnet run --project Kivi.App` with a fresh/reset `%APPDATA%\Kivi\settings.json`. Verify and report each:
1. Login screen appears, matches the `ui/` login frame (centered wordmark, tagline, Google pill, email link, footer; no Apple button).
2. Google/email both advance to Permissions.
3. Permissions screen matches the "two permissions, then you talk" frame; mic shows real granted/denied; "Right Ctrl" badge present; Screen-context shown granted.
4. Continue (enabled only when mic granted) advances to Config.
5. Config screen matches the design's card-grid style with only the built controls; changing orb color / language / toggles works; hotkey capture rebinds.
6. Done closes onboarding; the bare-bird orb appears bottom-center with NO black box/window.
7. Hold Right Ctrl (or the rebound key), speak — orb grows + recolors, text pastes, orb returns to idle.
8. Quit and relaunch — onboarding is skipped, orb appears directly.
9. `settings.json` shows `OnboardingCompleted: true` and the chosen orb color / language / hotkey persisted.

- [ ] **Step 6b: Run the full Kivi.Core test suite**

Run: `dotnet test Kivi.Core.Tests`
Expected: all tests pass (Phase A didn't regress under the full build).

- [ ] **Step 6c: Confirm whole solution builds clean**

Run: `dotnet build`
Expected: `Kivi.Core`, `Kivi.Core.Tests`, `Kivi.Platform`, `Kivi.App` all build with no new errors/warnings.

- [ ] **Step 6d: Update the progress ledger**

Append a completion entry to `.superpowers/sdd/progress.md` recording the onboarding+orb build complete, the commits, and any deferred items (transcript-surface-above-orb if not built, sound-on-paste, orb position config, reopen-config-after-onboarding).

- [ ] **Step 6e: Commit the ledger**

```bash
git add .superpowers/sdd/progress.md
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
docs: record onboarding + orb build completion in progress ledger

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review Notes

**Spec coverage:** §1 (onboarding gate/persistence) → Task 5. §2.1 (Login) → Task 3. §2.2 (Permissions, real mic, informational accessibility) → Task 3. §2.3 (Config: hotkey/language/orb-color/launch-at-login/screen-context; excludes the deferred controls) → Task 4. §3 (bare-bird orb, scales+recolors, accent-color tint, transparent no-container, black-window fix) → Task 2. §4 (runtime hotkey rebinding) → Task 1. §4a (visual fidelity to `ui/`) → Tasks 2/3/4 build to it, Task 6 human-verifies it. §5 (what doesn't change) → honored; only the four AppConfig fields, one interface method, and one orchestrator conditional touch `Kivi.Core`/`Kivi.Platform`. §6 deferred items → surfaced in Task 6's ledger entry.

**Transcript-surface decision:** the spec (§3) left "build the floating transcript card now vs. defer" as a plan-level call. This plan **defers** it — the bare bird + scaling + recoloring is the committed scope; the transcript-above-orb surface needs live/final transcript text piped out of the orchestrator (which today only raises `RecordingState`, not text), i.e. real new engine plumbing beyond "UI only." Noted as deferred in Task 6's ledger. This keeps the plan within the "UI flow only, no backend" boundary the user set.

**Placeholder scan:** no TBD/TODO. Task 3's `ConfigPage` stub and Task 4 filling it in is an explicit, sequenced handoff (stub compiles, real content follows), not a placeholder gap. Task 5's first code block is shown, then explicitly superseded by the `RunStartupGateAsync` version — the superseding version is the one to implement; the first is left visible only to explain why the async refactor is needed (a reviewer reading it should implement `RunStartupGateAsync`).

**Type consistency:** `AppConfig.HotkeyVirtualKeyCode` is `uint` throughout (Task 1 default `0xA3`, `IHotkeyService.SetHotkey(uint)`, `HotkeyCaptureBox.VirtualKeyCode` `uint`, `ConfigViewModel.HotkeyVk` `uint`). `OrbAccentColor` is `string` hex throughout (AppConfig, ConfigViewModel, `ColorFromHex` in App.xaml.cs, `KiviOrbControl.AccentBrush` built from it). `MicPermission` is a static class with `CheckAsync()/RequestAsync()/OpenSettings()` used consistently in Task 3 and Task 5. `OnboardingWindow.RaiseCompleted()`/`Completed`/`NavigateTo`/`PermissionsOnly` are defined in Task 3 (PermissionsOnly added in Task 5) and used consistently.

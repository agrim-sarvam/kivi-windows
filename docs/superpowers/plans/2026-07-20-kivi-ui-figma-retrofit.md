# Kivi UI Figma Retrofit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expand `RecordingState` to 7 values matching the real Kivi Figma design, then build the WinUI 3 "Kivi.App" presentation layer (tokens, dot-matrix orb overlay, tray, settings shell) against the already-designed impl-03 architecture, using real Figma tokens/components instead of placeholders.

**Architecture:** Phase A (`Kivi.Core`) is TDD: expand `RecordingState`, surface `GroqPolishClient`'s existing rate-limit cooldown as a real orchestrator state, add a transient post-success state. Phase B (`Kivi.App`) converts the console app to a WinUI 3 app per impl-03 §1/§7, then builds tokens, the orb control, overlay window, tray, and settings shell in dependency order. Phase B tasks are build-gated (compile + manual smoke test), not unit-tested — this matches the existing convention for `Kivi.Platform`'s hardware-dependent code, since WinUI3 windows/controls have no meaningful headless unit test story.

**Task structure:** 5 subagent dispatches total — Task 1 (Core state model, 3 step-groups), Task 2 (WinUI3 project + tokens + app shell, 3 step-groups), Task 3 (view models + tray + orb + overlay window, 4 step-groups), Task 4 (settings shell), Task 5 (whole-app verification). Each lettered step-group (1a/1b/1c, 2a/2b/2c, 3a/3b/3c/3d) keeps its own build check and commit for traceability, but all step-groups within a numbered task are done by the same subagent dispatch and reviewed together as one unit.

**Tech Stack:** .NET 8, C#, WinUI 3 (Microsoft.WindowsAppSDK), CommunityToolkit.Mvvm, H.NotifyIcon.WinUI, xUnit (Phase A only).

## Global Constraints

- Dependency direction: `Kivi.Core` has zero Windows/UI dependencies; `Kivi.Platform` and `Kivi.App` depend on `Kivi.Core`, never the reverse.
- Never log the API key (not even truncated), transcript text, audio bytes, or captured screen context.
- `RecordingState` values used elsewhere in the codebase (`Transcribing`, `Pasting`) are being renamed to `Processing`/`Speaking` — every reference must be updated in the same task that renames the enum, so the build never sits broken.
- Hotkey is Right Ctrl (already correct in the existing engine and in the `ui/components` retrofit done earlier this session) — never reintroduce `fn`/Mac-specific copy in any new UI text.
- Fonts: Inter for body/UI text, Space Grotesk (weight 500, -4% tracking) for the "kivi" wordmark only, Space Mono for hotkey badges/metadata labels (LIVE, hi-IN · auto, state chip labels). No Matter/Season Mix (unlicensed) in this pass.
- Colors/spacing/radii come verbatim from `ui/components/fig-tokens.css` — do not invent new hex values or scale steps.
- Kivi.App becomes unpackaged (`WindowsPackageType=None`), self-contained WinAppSDK, per impl-03 §7 — this is a deployment decision already made, not open for reinterpretation.
- TDD for all `Kivi.Core` changes (Phase A). No unit tests required for `Kivi.App` WinUI3 code (Phase B) — verify via build success + the manual smoke-test steps specified in each task.

---

## Phase A — Kivi.Core state model (TDD)

> Tasks 1a-1c below are merged into a single subagent dispatch, **Task 1**, per
> execution preference (fewer, larger units). They are kept as separate
> step-groups here only so file/interface boundaries stay explicit — the
> implementer works through 1a, then 1b, then 1c, committing after each
> step-group as shown, before the task as a whole is handed to review.

### Task 1a: Expand RecordingState to 7 values

**Files:**
- Modify: `Kivi.Core/Orchestration/RecordingState.cs`
- Modify: `Kivi.Core/Orchestration/DictationOrchestrator.cs`
- Modify: `Kivi.App/Program.cs` (console logger references `RecordingState` values only for logging — no logic change, but must compile)
- Test: `Kivi.Core.Tests/OrchestratorTests.cs`

**Interfaces:**
- Consumes: nothing new — this is a rename/expansion of an existing public enum.
- Produces: `RecordingState` now has values `Idle, Listening, Processing, Speaking, Waiting, Done, Error` (previously `Idle, Listening, Transcribing, Pasting, Error`). Every later task that references `RecordingState.Transcribing` or `.Pasting` must use `.Processing`/`.Speaking` instead. `Waiting` and `Done` transitions are added in step-groups 1b and 1c respectively — this task only adds the enum values and renames existing usages; it does not yet make the orchestrator ever emit `Waiting`/`Done`.

- [ ] **Step 1: Update the failing test's expectations first**

Edit `Kivi.Core.Tests/OrchestratorTests.cs` — change the existing assertions to use the new names:

```csharp
        Assert.Equal("Hello there.", paste.Pasted);
        Assert.Contains(RecordingState.Listening, states);
        Assert.Contains(RecordingState.Processing, states);
        Assert.Contains(RecordingState.Speaking, states);
        Assert.Equal(RecordingState.Idle, orch.State);
```

(Only the two `Assert.Contains` lines change — `Transcribing` → `Processing`, `Pasting` → `Speaking`.)

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Kivi.Core.Tests --filter FullyQualifiedName~OrchestratorTests.FullDictation_RunsStateSequence_AndPastesCleanedText`
Expected: FAIL — compile error, since `RecordingState.Processing`/`.Speaking` don't exist yet.

- [ ] **Step 3: Update the enum**

Edit `Kivi.Core/Orchestration/RecordingState.cs`:

```csharp
namespace Kivi.Core.Orchestration;
public enum RecordingState { Idle, Listening, Processing, Speaking, Waiting, Done, Error }
```

- [ ] **Step 4: Update DictationOrchestrator's state transitions**

Edit `Kivi.Core/Orchestration/DictationOrchestrator.cs` — in `RunPipelineAsync()`, replace every use of `RecordingState.Transcribing` with `RecordingState.Processing` and `RecordingState.Pasting` with `RecordingState.Speaking`:

```csharp
        try
        {
            SetState(RecordingState.Processing);
            var recSw = Stopwatch.StartNew();
            var wav = await _audio.StopRecordingAsync();
            _metrics.RecordStage("record", recSw.Elapsed.TotalMilliseconds);

            var sttSw = Stopwatch.StartNew();
            var raw = await _stt.TranscribeAsync(wav, _cts.Token);
            _metrics.RecordStage("stt", sttSw.Elapsed.TotalMilliseconds);
            if (string.IsNullOrEmpty(raw)) { SetState(RecordingState.Idle); return; }

            var cmd = TranscriptCommands.Parse(raw, _config.PressEnterCommandEnabled);
            string textToPaste;

            var macro = MacroMatcher.FindMatch(cmd.Transcript, _config.Macros);
            if (macro is not null)
            {
                textToPaste = macro.Payload;
            }
            else
            {
                var context = await _contextTask;
                var cleanSw = Stopwatch.StartNew();
                var cleaned = await _polish.CleanupAsync(cmd.Transcript, context, _cts.Token);
                _metrics.RecordStage("cleanup", cleanSw.Elapsed.TotalMilliseconds);
                if (string.IsNullOrEmpty(cleaned)) { SetState(RecordingState.Idle); return; }
                textToPaste = cleaned;
            }

            SetState(RecordingState.Speaking);
            var pasteSw = Stopwatch.StartNew();
            await _paste.InjectTextAsync(textToPaste, cmd.ShouldPressEnter);
            _metrics.RecordStage("paste", pasteSw.Elapsed.TotalMilliseconds);

            SetState(RecordingState.Idle);
        }
        catch
        {
            SetState(RecordingState.Error);
            SetState(RecordingState.Idle);
        }
        finally
        {
            _metrics.RecordTotal(total.Elapsed.TotalMilliseconds);
        }
```

(This is the existing method body with only the two `SetState(...)` enum values changed — no other logic changes in this task.)

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test Kivi.Core.Tests --filter FullyQualifiedName~OrchestratorTests`
Expected: PASS (both `FullDictation_RunsStateSequence_AndPastesCleanedText` and `VoiceMacro_BypassesCleanup_PastesPayload`)

- [ ] **Step 6: Confirm the whole solution still builds**

Run: `dotnet build`
Expected: Build succeeds — `Kivi.App/Program.cs`'s `orchestrator.StateChanged += s => logger.LogInformation("state -> {State}", s);` line references `RecordingState` only via the generic event delegate, so it needs no source change, but this step confirms that assumption holds.

- [ ] **Step 7: Commit**

```bash
git add Kivi.Core/Orchestration/RecordingState.cs Kivi.Core/Orchestration/DictationOrchestrator.cs Kivi.Core.Tests/OrchestratorTests.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(core): expand RecordingState to match Figma design states

Rename Transcribing->Processing and Pasting->Speaking to match the
real Kivi design's state vocabulary (no behavior change). Adds Waiting
and Done as unused enum values for now; wired up in follow-up tasks.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 1b: Surface the Groq rate-limit cooldown as a Waiting state

**Files:**
- Modify: `Kivi.Core/Polish/IPolishClient.cs`
- Modify: `Kivi.Core/Polish/GroqPolishClient.cs`
- Modify: `Kivi.Core/Orchestration/DictationOrchestrator.cs`
- Modify: `Kivi.Core.Tests/Fakes/Fakes.cs` (StubPolish must implement the new interface member)
- Test: `Kivi.Core.Tests/OrchestratorTests.cs`

**Interfaces:**
- Consumes: `RecordingState.Waiting` (from Task 1).
- Produces: `IPolishClient` gains `event Action<string>? EnteringCooldown;` — raised with the model name whenever `GroqPolishClient` puts a model into cooldown after a 429. `DictationOrchestrator` subscribes to this and transitions to `RecordingState.Waiting` for the duration, then continues to `RecordingState.Processing` once the fallback call is issued. This event is additive to `IPolishClient` — existing callers (`StubPolish` in tests) must implement it (can be a no-op `event Action<string>? EnteringCooldown;` auto-property-style event with no invocation).

- [ ] **Step 1: Write the failing test**

Add to `Kivi.Core.Tests/OrchestratorTests.cs`:

```csharp
    [Fact]
    public async Task RateLimitedPolish_EmitsWaitingState_BeforeProcessingResumes()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var polish = new CooldownStubPolish();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), polish, paste, AppConfig.Default(), metrics);

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(20);
        hotkey.FireEnd();
        await Task.Delay(200);

        Assert.Contains(RecordingState.Waiting, states);
        Assert.Equal("Hello there.", paste.Pasted);
    }
```

Add a new fake to `Kivi.Core.Tests/Fakes/Fakes.cs` (append, do not replace existing fakes):

```csharp
public sealed class CooldownStubPolish : Kivi.Core.Polish.IPolishClient
{
    public event Action<string>? EnteringCooldown;
    public async Task<string> CleanupAsync(string transcript, string context, CancellationToken ct)
    {
        EnteringCooldown?.Invoke("primary-model");
        await Task.Delay(10, ct);
        return "Hello there.";
    }
}
```

Also update `StubPolish` in the same file to implement the new interface member (no-op event):

```csharp
public sealed class StubPolish : Kivi.Core.Polish.IPolishClient
{
    public event Action<string>? EnteringCooldown;
    public Task<string> CleanupAsync(string transcript, string context, CancellationToken ct)
        => Task.FromResult("Hello there.");
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Kivi.Core.Tests --filter FullyQualifiedName~RateLimitedPolish_EmitsWaitingState_BeforeProcessingResumes`
Expected: FAIL — compile error (`IPolishClient` has no `EnteringCooldown` member yet; `RecordingState.Waiting` is never raised).

- [ ] **Step 3: Add the event to IPolishClient**

Edit `Kivi.Core/Polish/IPolishClient.cs`:

```csharp
namespace Kivi.Core.Polish;
public interface IPolishClient
{
    event Action<string>? EnteringCooldown;
    Task<string> CleanupAsync(string transcript, string context, CancellationToken ct);
}
```

- [ ] **Step 4: Raise the event from GroqPolishClient's existing cooldown logic**

In `Kivi.Core/Polish/GroqPolishClient.cs`, add the event declaration near the top of the class (alongside the existing fields):

```csharp
    public event Action<string>? EnteringCooldown;
```

Then in the `catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)` block inside `CleanupAsync`, raise the event right where the cooldown is recorded:

```csharp
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _cooldownUntil[model] = DateTimeOffset.UtcNow.AddSeconds(30);
                EnteringCooldown?.Invoke(model);
            }
```

(This is the existing catch block from `GroqPolishClient.cs:53-56` with one line added — no other change to the retry/fallback logic.)

- [ ] **Step 5: Wire the orchestrator to transition through Waiting**

In `Kivi.Core/Orchestration/DictationOrchestrator.cs`, subscribe to `_polish.EnteringCooldown` in the constructor and transition state:

```csharp
    public DictationOrchestrator(IHotkeyService hotkey, IAudioCaptureService audio, IScreenContextProvider context,
        ISttEngine stt, IPolishClient polish, IPasteService paste, AppConfig config, KiviMetrics metrics)
    {
        (_hotkey, _audio, _context, _stt, _polish, _paste, _config, _metrics)
           = (hotkey, audio, context, stt, polish, paste, config, metrics);
        _polish.EnteringCooldown += _ => SetState(RecordingState.Waiting);
    }
```

(Replace the existing single-expression constructor body with this — the tuple assignment stays identical, only the subscription line is added.)

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test Kivi.Core.Tests --filter FullyQualifiedName~OrchestratorTests`
Expected: PASS for all `OrchestratorTests` tests, including the new one.

- [ ] **Step 7: Run the full Core test suite to check for regressions**

Run: `dotnet test Kivi.Core.Tests`
Expected: All tests pass, including `GroqPolishClientTests.RateLimited_FallsBackToSecondModel_AndReturnsCleanedText` (which exercises the same cooldown code path and must still pass unchanged).

- [ ] **Step 8: Commit**

```bash
git add Kivi.Core/Polish/IPolishClient.cs Kivi.Core/Polish/GroqPolishClient.cs Kivi.Core/Orchestration/DictationOrchestrator.cs Kivi.Core.Tests/OrchestratorTests.cs Kivi.Core.Tests/Fakes/Fakes.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(core): surface Groq rate-limit cooldown as RecordingState.Waiting

GroqPolishClient already tracked a per-model cooldown after a 429 but
never exposed it; it was invisible to the user. Adds an EnteringCooldown
event to IPolishClient so the orchestrator can transition through
Waiting instead of silently retrying.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 1c: Add the transient Done state after a successful paste

**Files:**
- Modify: `Kivi.Core/Orchestration/DictationOrchestrator.cs`
- Test: `Kivi.Core.Tests/OrchestratorTests.cs`

**Interfaces:**
- Consumes: `RecordingState.Done` (from Task 1).
- Produces: after a successful `_paste.InjectTextAsync` call, the orchestrator now transitions `Speaking` → `Done` → (after a delay) `Idle`, instead of `Speaking` → `Idle` directly. The delay duration is a `private const int DoneDisplayMs = 1200;` field on `DictationOrchestrator` — later UI tasks do not need to know this value (it's purely a Core-side timing detail), only that `Done` occurs before `Idle`.

- [ ] **Step 1: Write the failing test**

Add to `Kivi.Core.Tests/OrchestratorTests.cs`:

```csharp
    [Fact]
    public async Task SuccessfulDictation_PassesThroughDone_BeforeIdle()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics);

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(20);
        hotkey.FireEnd();
        await Task.Delay(1500); // allow pipeline + Done->Idle delay to complete

        Assert.Contains(RecordingState.Done, states);
        // Done must occur before the final Idle in the sequence.
        var doneIndex = states.LastIndexOf(RecordingState.Done);
        var lastIdleIndex = states.LastIndexOf(RecordingState.Idle);
        Assert.True(doneIndex < lastIdleIndex, "Done must precede the final Idle transition.");
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test Kivi.Core.Tests --filter FullyQualifiedName~SuccessfulDictation_PassesThroughDone_BeforeIdle`
Expected: FAIL — `RecordingState.Done` is never in `states` (orchestrator currently goes straight to `Idle`).

- [ ] **Step 3: Add the Done transition with a delay**

Edit `Kivi.Core/Orchestration/DictationOrchestrator.cs` — add the constant near the top of the class:

```csharp
    private const int DoneDisplayMs = 1200;
```

Then change the success path at the end of `RunPipelineAsync`:

```csharp
            SetState(RecordingState.Speaking);
            var pasteSw = Stopwatch.StartNew();
            await _paste.InjectTextAsync(textToPaste, cmd.ShouldPressEnter);
            _metrics.RecordStage("paste", pasteSw.Elapsed.TotalMilliseconds);

            SetState(RecordingState.Done);
            await Task.Delay(DoneDisplayMs, _cts.Token);
            SetState(RecordingState.Idle);
```

(This replaces the previous `SetState(RecordingState.Speaking); ... SetState(RecordingState.Idle);` tail with the `Done` step inserted, using the same `_cts.Token` already in scope for cancellation consistency with the rest of the method.)

**Important:** the `catch` block's early-return paths (`if (string.IsNullOrEmpty(raw)) { SetState(RecordingState.Idle); return; }` and the equivalent for `cleaned`) and the `catch { SetState(RecordingState.Error); SetState(RecordingState.Idle); }` block do **not** get a `Done` state — `Done` only follows a genuinely successful paste. Do not add `Done` to any other code path.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test Kivi.Core.Tests --filter FullyQualifiedName~OrchestratorTests`
Expected: PASS for all `OrchestratorTests` tests.

- [ ] **Step 5: Run the full Core test suite to check for regressions**

Run: `dotnet test Kivi.Core.Tests`
Expected: All tests pass. Note `VoiceMacro_BypassesCleanup_PastesPayload` also goes through the same success path and now waits through `DoneDisplayMs` — confirm it still passes within its existing `await Task.Delay(200)` window or bump that test's delay if it now needs longer to observe the final `Idle` state. If it needs bumping, change `Kivi.Core.Tests/OrchestratorTests.cs`'s `VoiceMacro_BypassesCleanup_PastesPayload` test's final `await Task.Delay(200);` to `await Task.Delay(1500);` to match the new `DoneDisplayMs` delay.

- [ ] **Step 6: Commit**

```bash
git add Kivi.Core/Orchestration/DictationOrchestrator.cs Kivi.Core.Tests/OrchestratorTests.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(core): add transient Done state after successful paste

Matches the Figma design's 'done' state mark - a brief success flash
before returning to Idle, instead of cutting straight from Speaking to
Idle with no confirmation.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Phase B — Kivi.App WinUI 3 presentation layer (build-gated, no unit tests)

> Phase B tasks follow the existing `Kivi.Platform` convention: hardware/UI-dependent code is verified by successful build + an explicit manual smoke-test procedure per task, not xUnit tests. Each task's smoke test must be run and its result reported before moving to the next task.
>
> Per execution preference (fewer, larger subagent dispatches), Phase B's original
> 9 tasks are grouped into 4 dispatches: **Task 2** (project conversion + tokens +
> app shell — 2a/2b/2c), **Task 3** (view models + tray + orb + overlay window —
> 3a/3b/3c/3d), **Task 4** (settings shell — unchanged, already one unit), and
> **Task 5** (whole-app verification — unchanged). Each lettered step-group keeps
> its own build check + commit; the task as a whole goes to review once all its
> step-groups are done.

### Task 2a: Convert Kivi.App to a WinUI 3 project

**Files:**
- Modify: `Kivi.App/Kivi.App.csproj`
- Create: `Kivi.App/app.manifest`
- Modify: `Kivi.App/Program.cs` (temporary — becomes the WinUI3 App/Main split; further gutted in Task 2c)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `Kivi.App` compiles as a WinUI 3 executable using `Microsoft.WindowsAppSDK`. No visible UI yet — this task only proves the project template/packaging change builds and runs (empty window or console fallback), before any real views are added.

- [ ] **Step 1: Add the WinUI 3 / Windows App SDK package references and project settings**

Edit `Kivi.App/Kivi.App.csproj` — replace the `<PropertyGroup>` and add to the `<ItemGroup>`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <ProjectReference Include="..\Kivi.Core\Kivi.Core.csproj" />
    <ProjectReference Include="..\Kivi.Platform\Kivi.Platform.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Configuration" Version="10.0.10" />
    <PackageReference Include="Microsoft.Extensions.Configuration.EnvironmentVariables" Version="10.0.10" />
    <PackageReference Include="Microsoft.Extensions.Configuration.UserSecrets" Version="10.0.10" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.10" />
    <PackageReference Include="Microsoft.Extensions.Logging.Console" Version="10.0.10" />
    <PackageReference Include="OpenTelemetry" Version="1.17.0" />
    <PackageReference Include="OpenTelemetry.Exporter.Console" Version="1.17.0" />
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" Version="1.17.0" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" Version="1.17.0" />
    <PackageReference Include="Microsoft.WindowsAppSDK" Version="1.6.*" />
    <PackageReference Include="Microsoft.Windows.SDK.BuildTools" Version="10.0.26100.*" />
    <PackageReference Include="CommunityToolkit.Mvvm" Version="8.*" />
    <PackageReference Include="H.NotifyIcon.WinUI" Version="2.*" />
  </ItemGroup>

  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWinUI>true</UseWinUI>
    <UseWindowsForms>true</UseWindowsForms>
    <UserSecretsId>cd802105-0da6-4633-ab38-6bd64331d635</UserSecretsId>

    <WindowsPackageType>None</WindowsPackageType>
    <WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
    <SelfContained>true</SelfContained>
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>

</Project>
```

(`UseWindowsForms` is kept — the existing `MessagePump`/hotkey interop under `Kivi.Platform` relies on WinForms message pumping today; removing it is out of scope for this task and will be revisited only if it causes a real conflict in Task 2c.)

- [ ] **Step 2: Add the standard WinUI 3 app manifest**

Create `Kivi.App/app.manifest`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Kivi.App.app"/>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}"/>
      <supportedOS Id="{1f676c76-80e1-4239-95bb-83d0f6d0da78}"/>
      <supportedOS Id="{4a2f28e3-53b9-4441-ba9c-d69d4a4a6e38}"/>
      <supportedOS Id="{35138b9a-5d96-4fbd-8e2d-a2440225f93a}"/>
      <supportedOS Id="{e2011457-1546-43c5-a5fe-008deee3d3f0}"/>
    </application>
  </compatibility>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">PerMonitorV2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
```

- [ ] **Step 3: Restore packages and confirm the project builds**

Run: `dotnet restore` then `dotnet build Kivi.App`
Expected: Build succeeds. If `Microsoft.WindowsAppSDK` version `1.6.*` fails to resolve, check `dotnet nuget list source` for the correct feed and adjust to the latest available `1.6.x` or `1.7.x` patch — do not silently downgrade to a materially older major version without noting it in the commit message.

- [ ] **Step 4: Manual smoke test**

Run: `dotnet run --project Kivi.App`
Expected: The existing console `Main()` in `Program.cs` still runs unchanged at this point (this task only changed project settings/packages, not `Program.cs` logic) — confirm it starts up exactly as before (prints "Kivi ready..." and responds to Right Ctrl) with no new runtime errors introduced by the WinUI3 package references. Report the console output.

- [ ] **Step 5: Commit**

```bash
git add Kivi.App/Kivi.App.csproj Kivi.App/app.manifest
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): convert Kivi.App to a WinUI 3 project (unpackaged)

Adds Microsoft.WindowsAppSDK, CommunityToolkit.Mvvm, H.NotifyIcon.WinUI
and the unpackaged/self-contained deployment settings from impl-03 S7.
Program.cs logic is unchanged in this task - only project/package
settings, so the existing console entry point still runs as-is.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2b: Create the design token dictionary (Themes/Tokens.xaml)

**Files:**
- Create: `Kivi.App/Themes/Tokens.xaml`
- Create: `Kivi.App/Assets/Fonts/` (directory placeholder — actual font files added in Task 2b, Step 2)
- Modify: `Kivi.App/Kivi.App.csproj` (mark font files as `Content`)

**Interfaces:**
- Consumes: nothing from earlier tasks (pure resource dictionary, no code dependency).
- Produces: `Themes/Tokens.xaml` exposes primitive tokens (`KiviColorPaper`, `KiviColorPaper2`, `KiviColorWarmTint`, `KiviColorLegGreen`, `KiviColorFg1-4`, `KiviColorBorder1-3`, `KiviColorBrandInk`, `KiviColorState{Idle,Listening,Processing,Speaking,Waiting,Error}` + their `*Bg` variants, `KiviColorPositive`, `KiviColorWarning`, `KiviColorDanger` + their `*Bg` variants, `KiviRadiusXs/Sm/Md/Lg/Xl/Full`, `KiviSpaceS1` through `KiviSpaceS32`, `KiviFontFamily` = Inter, `KiviWordmarkFontFamily` = Space Grotesk, `KiviMonoFontFamily` = Space Mono) and semantic `Light`/`Dark` `ThemeDictionaries` entries later views will bind to (`KiviSurfaceBrush`, `KiviSurfaceAltBrush`, `KiviTextPrimaryBrush`, `KiviTextSecondaryBrush`, `KiviStrokeBrush`, `KiviAccentBrush`, `KiviDangerBrush`, plus one brush pair per `RecordingState` value: `OverlayIdleBrush`, `OverlayListeningBrush`, `OverlayProcessingBrush`, `OverlaySpeakingBrush`, `OverlayWaitingBrush`, `OverlayDoneBrush` (aliases `KiviColorPositive`), `OverlayErrorBrush`). Later tasks (orb control, overlay window, settings pages) reference only these semantic keys, never literal values.

- [ ] **Step 1: Create the primitive + semantic token dictionary**

Create `Kivi.App/Themes/Tokens.xaml` with values transcribed exactly from `ui/components/fig-tokens.css`:

```xml
<!-- Kivi.App/Themes/Tokens.xaml -->
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <!-- ============================================================= -->
    <!-- LAYER 1 — PRIMITIVE TOKENS (theme-agnostic raw values)         -->
    <!-- Transcribed verbatim from ui/components/fig-tokens.css         -->
    <!-- ============================================================= -->

    <!-- Light-mode color primitives (from :root) -->
    <Color x:Key="KiviColorPaper">#F1F4EC</Color>
    <Color x:Key="KiviColorPaper2">#FFFFFF</Color>
    <Color x:Key="KiviColorWarmTint">#E7EEDD</Color>
    <Color x:Key="KiviColorLegGreen">#41691E</Color>
    <Color x:Key="KiviColorFg1">#14180E</Color>
    <Color x:Key="KiviColorFg2">#5C6454</Color>
    <Color x:Key="KiviColorFg3">#929A8A</Color>
    <Color x:Key="KiviColorFg4">#B2B8A8</Color>
    <Color x:Key="KiviColorFgInverse">#FFFFFF</Color>
    <Color x:Key="KiviColorBorder1">#EDF0E6</Color>
    <Color x:Key="KiviColorBorder2">#E1E6D8</Color>
    <Color x:Key="KiviColorBorder3">#ACB4A0</Color>
    <Color x:Key="KiviColorBrandInk">#161E10</Color>
    <Color x:Key="KiviColorStateIdle">#929A8A</Color>
    <Color x:Key="KiviColorStateIdleBg">#E7EEDD</Color>
    <Color x:Key="KiviColorStateListening">#E96C2F</Color>
    <Color x:Key="KiviColorStateListeningBg">#FEEDE6</Color>
    <Color x:Key="KiviColorStateProcessing">#4250D5</Color>
    <Color x:Key="KiviColorStateProcessingBg">#E8EFFC</Color>
    <Color x:Key="KiviColorStateSpeaking">#4B7D28</Color>
    <Color x:Key="KiviColorStateSpeakingBg">#E3F1D8</Color>
    <Color x:Key="KiviColorStateWaiting">#D2962D</Color>
    <Color x:Key="KiviColorStateWaitingBg">#FFF2D2</Color>
    <Color x:Key="KiviColorStateError">#B81514</Color>
    <Color x:Key="KiviColorStateErrorBg">#FAD7CD</Color>
    <Color x:Key="KiviColorPositive">#6EA335</Color>
    <Color x:Key="KiviColorPositiveBg">#F2F8EB</Color>
    <Color x:Key="KiviColorWarning">#A27224</Color>
    <Color x:Key="KiviColorWarningBg">#FFF8E6</Color>
    <Color x:Key="KiviColorDanger">#B81514</Color>
    <Color x:Key="KiviColorDangerBg">#FDE7E2</Color>

    <!-- Dark-mode color primitives (from :root[data-theme="dark"]) -->
    <Color x:Key="KiviColorPaperDark">#14180F</Color>
    <Color x:Key="KiviColorPaper2Dark">#1C2116</Color>
    <Color x:Key="KiviColorWarmTintDark">#21271B</Color>
    <Color x:Key="KiviColorLegGreenDark">#6EA335</Color>
    <Color x:Key="KiviColorFg1Dark">#E0E6D8</Color>
    <Color x:Key="KiviColorFg2Dark">#929989</Color>
    <Color x:Key="KiviColorFg3Dark">#626959</Color>
    <Color x:Key="KiviColorFg4Dark">#404737</Color>
    <Color x:Key="KiviColorFgInverseDark">#14180E</Color>
    <Color x:Key="KiviColorBorder1Dark">#32382B</Color>
    <Color x:Key="KiviColorBorder2Dark">#424939</Color>
    <Color x:Key="KiviColorBorder3Dark">#5C6351</Color>
    <Color x:Key="KiviColorBrandInkDark">#E0E6D8</Color>
    <Color x:Key="KiviColorStateIdleDark">#626959</Color>
    <Color x:Key="KiviColorStateIdleBgDark">#21271B</Color>
    <Color x:Key="KiviColorStateListeningDark">#F59666</Color>
    <Color x:Key="KiviColorStateListeningBgDark">#33200F</Color>
    <Color x:Key="KiviColorStateProcessingDark">#7C96E6</Color>
    <Color x:Key="KiviColorStateProcessingBgDark">#171E3A</Color>
    <Color x:Key="KiviColorStateSpeakingDark">#82AF5A</Color>
    <Color x:Key="KiviColorStateSpeakingBgDark">#1D2B10</Color>
    <Color x:Key="KiviColorStateWaitingDark">#F0B95A</Color>
    <Color x:Key="KiviColorStateWaitingBgDark">#33270F</Color>
    <Color x:Key="KiviColorStateErrorDark">#F85149</Color>
    <Color x:Key="KiviColorStateErrorBgDark">#3A1211</Color>
    <Color x:Key="KiviColorPositiveDark">#3FB981</Color>
    <Color x:Key="KiviColorPositiveBgDark">#0E231B</Color>
    <Color x:Key="KiviColorWarningDark">#D29922</Color>
    <Color x:Key="KiviColorWarningBgDark">#271F00</Color>
    <Color x:Key="KiviColorDangerDark">#F85149</Color>
    <Color x:Key="KiviColorDangerBgDark">#2A0C13</Color>

    <!-- Radii (--radius-* from fig-tokens.css) -->
    <CornerRadius x:Key="KiviRadiusXs">4</CornerRadius>
    <CornerRadius x:Key="KiviRadiusSm">8</CornerRadius>
    <CornerRadius x:Key="KiviRadiusMd">12</CornerRadius>
    <CornerRadius x:Key="KiviRadiusLg">20</CornerRadius>
    <CornerRadius x:Key="KiviRadiusXl">28</CornerRadius>
    <CornerRadius x:Key="KiviRadiusFull">9999</CornerRadius>

    <!-- Spacing (--space-s* from fig-tokens.css, 2px base) -->
    <x:Double x:Key="KiviSpaceS1">2</x:Double>
    <x:Double x:Key="KiviSpaceS2">4</x:Double>
    <x:Double x:Key="KiviSpaceS3">6</x:Double>
    <x:Double x:Key="KiviSpaceS4">8</x:Double>
    <x:Double x:Key="KiviSpaceS6">12</x:Double>
    <x:Double x:Key="KiviSpaceS8">16</x:Double>
    <x:Double x:Key="KiviSpaceS10">20</x:Double>
    <x:Double x:Key="KiviSpaceS12">24</x:Double>
    <x:Double x:Key="KiviSpaceS16">32</x:Double>
    <x:Double x:Key="KiviSpaceS20">40</x:Double>
    <x:Double x:Key="KiviSpaceS24">48</x:Double>
    <x:Double x:Key="KiviSpaceS32">64</x:Double>

    <!-- Typography (font sourcing per spec S3 — Inter, not Matter/Season Mix) -->
    <x:String x:Key="KiviFontFamily">Inter</x:String>
    <x:String x:Key="KiviWordmarkFontFamily">Space Grotesk</x:String>
    <x:String x:Key="KiviMonoFontFamily">Space Mono</x:String>
    <x:Double x:Key="KiviFontSizeCaption">11.5</x:Double>
    <x:Double x:Key="KiviFontSizeBody">14</x:Double>
    <x:Double x:Key="KiviFontSizeTitle">20</x:Double>
    <FontWeight x:Key="KiviFontWeightRegular">Normal</FontWeight>
    <FontWeight x:Key="KiviFontWeightMedium">Medium</FontWeight>
    <FontWeight x:Key="KiviFontWeightSemibold">SemiBold</FontWeight>

    <!-- ============================================================= -->
    <!-- LAYER 2 — SEMANTIC TOKENS (role -> primitive, theme-aware)     -->
    <!-- Views bind ONLY to these, via {ThemeResource}.                 -->
    <!-- ============================================================= -->
    <ResourceDictionary.ThemeDictionaries>

        <!-- LIGHT ------------------------------------------------------ -->
        <ResourceDictionary x:Key="Light">
            <SolidColorBrush x:Key="KiviSurfaceBrush"       Color="{StaticResource KiviColorPaper2}"/>
            <SolidColorBrush x:Key="KiviSurfaceAltBrush"    Color="{StaticResource KiviColorPaper}"/>
            <SolidColorBrush x:Key="KiviTextPrimaryBrush"   Color="{StaticResource KiviColorFg1}"/>
            <SolidColorBrush x:Key="KiviTextSecondaryBrush" Color="{StaticResource KiviColorFg2}"/>
            <SolidColorBrush x:Key="KiviStrokeBrush"        Color="{StaticResource KiviColorBorder2}"/>
            <SolidColorBrush x:Key="KiviAccentBrush"        Color="{StaticResource KiviColorLegGreen}"/>
            <SolidColorBrush x:Key="KiviDangerBrush"        Color="{StaticResource KiviColorDanger}"/>
            <SolidColorBrush x:Key="OverlayIdleBrush"       Color="{StaticResource KiviColorStateIdle}"/>
            <SolidColorBrush x:Key="OverlayListeningBrush"  Color="{StaticResource KiviColorStateListening}"/>
            <SolidColorBrush x:Key="OverlayProcessingBrush" Color="{StaticResource KiviColorStateProcessing}"/>
            <SolidColorBrush x:Key="OverlaySpeakingBrush"   Color="{StaticResource KiviColorStateSpeaking}"/>
            <SolidColorBrush x:Key="OverlayWaitingBrush"    Color="{StaticResource KiviColorStateWaiting}"/>
            <SolidColorBrush x:Key="OverlayDoneBrush"       Color="{StaticResource KiviColorPositive}"/>
            <SolidColorBrush x:Key="OverlayErrorBrush"      Color="{StaticResource KiviColorStateError}"/>
        </ResourceDictionary>

        <!-- DARK ------------------------------------------------------- -->
        <ResourceDictionary x:Key="Dark">
            <SolidColorBrush x:Key="KiviSurfaceBrush"       Color="{StaticResource KiviColorPaper2Dark}"/>
            <SolidColorBrush x:Key="KiviSurfaceAltBrush"    Color="{StaticResource KiviColorPaperDark}"/>
            <SolidColorBrush x:Key="KiviTextPrimaryBrush"   Color="{StaticResource KiviColorFg1Dark}"/>
            <SolidColorBrush x:Key="KiviTextSecondaryBrush" Color="{StaticResource KiviColorFg2Dark}"/>
            <SolidColorBrush x:Key="KiviStrokeBrush"        Color="{StaticResource KiviColorBorder2Dark}"/>
            <SolidColorBrush x:Key="KiviAccentBrush"        Color="{StaticResource KiviColorLegGreenDark}"/>
            <SolidColorBrush x:Key="KiviDangerBrush"        Color="{StaticResource KiviColorDangerDark}"/>
            <SolidColorBrush x:Key="OverlayIdleBrush"       Color="{StaticResource KiviColorStateIdleDark}"/>
            <SolidColorBrush x:Key="OverlayListeningBrush"  Color="{StaticResource KiviColorStateListeningDark}"/>
            <SolidColorBrush x:Key="OverlayProcessingBrush" Color="{StaticResource KiviColorStateProcessingDark}"/>
            <SolidColorBrush x:Key="OverlaySpeakingBrush"   Color="{StaticResource KiviColorStateSpeakingDark}"/>
            <SolidColorBrush x:Key="OverlayWaitingBrush"    Color="{StaticResource KiviColorStateWaitingDark}"/>
            <SolidColorBrush x:Key="OverlayDoneBrush"       Color="{StaticResource KiviColorPositiveDark}"/>
            <SolidColorBrush x:Key="OverlayErrorBrush"      Color="{StaticResource KiviColorStateErrorDark}"/>
        </ResourceDictionary>

        <!-- HIGHCONTRAST — map to system colors, never hard-code -->
        <ResourceDictionary x:Key="HighContrast">
            <SolidColorBrush x:Key="KiviSurfaceBrush"       Color="{ThemeResource SystemColorWindowColor}"/>
            <SolidColorBrush x:Key="KiviSurfaceAltBrush"    Color="{ThemeResource SystemColorWindowColor}"/>
            <SolidColorBrush x:Key="KiviTextPrimaryBrush"   Color="{ThemeResource SystemColorWindowTextColor}"/>
            <SolidColorBrush x:Key="KiviTextSecondaryBrush" Color="{ThemeResource SystemColorGrayTextColor}"/>
            <SolidColorBrush x:Key="KiviStrokeBrush"        Color="{ThemeResource SystemColorWindowTextColor}"/>
            <SolidColorBrush x:Key="KiviAccentBrush"        Color="{ThemeResource SystemColorHighlightColor}"/>
            <SolidColorBrush x:Key="KiviDangerBrush"        Color="{ThemeResource SystemColorWindowTextColor}"/>
            <SolidColorBrush x:Key="OverlayIdleBrush"       Color="{ThemeResource SystemColorGrayTextColor}"/>
            <SolidColorBrush x:Key="OverlayListeningBrush"  Color="{ThemeResource SystemColorHighlightColor}"/>
            <SolidColorBrush x:Key="OverlayProcessingBrush" Color="{ThemeResource SystemColorHighlightColor}"/>
            <SolidColorBrush x:Key="OverlaySpeakingBrush"   Color="{ThemeResource SystemColorHighlightColor}"/>
            <SolidColorBrush x:Key="OverlayWaitingBrush"    Color="{ThemeResource SystemColorHighlightColor}"/>
            <SolidColorBrush x:Key="OverlayDoneBrush"       Color="{ThemeResource SystemColorHighlightColor}"/>
            <SolidColorBrush x:Key="OverlayErrorBrush"      Color="{ThemeResource SystemColorWindowTextColor}"/>
        </ResourceDictionary>
    </ResourceDictionary.ThemeDictionaries>
</ResourceDictionary>
```

- [ ] **Step 2: Source and bundle the font files**

Download (or locate already-licensed copies of) these font files and place them under `Kivi.App/Assets/Fonts/`:
- `Inter-Regular.ttf`, `Inter-Medium.ttf`, `Inter-SemiBold.ttf` (Google Fonts, OFL license)
- `SpaceGrotesk-Medium.ttf` (Google Fonts, OFL license)
- `SpaceMono-Regular.ttf` (Google Fonts, OFL license)

Add them to `Kivi.App/Kivi.App.csproj` as content:

```xml
  <ItemGroup>
    <Content Include="Assets\Fonts\*.ttf">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
```

- [ ] **Step 3: Update Tokens.xaml font family declarations to reference the bundled files**

Edit the three `x:String` font family entries in `Kivi.App/Themes/Tokens.xaml` from Step 1 to point at the bundled `.ttf` files:

```xml
    <x:String x:Key="KiviFontFamily">/Assets/Fonts/Inter-Regular.ttf#Inter</x:String>
    <x:String x:Key="KiviWordmarkFontFamily">/Assets/Fonts/SpaceGrotesk-Medium.ttf#Space Grotesk</x:String>
    <x:String x:Key="KiviMonoFontFamily">/Assets/Fonts/SpaceMono-Regular.ttf#Space Mono</x:String>
```

- [ ] **Step 4: Confirm the project builds with the new resource dictionary and assets present**

Run: `dotnet build Kivi.App`
Expected: Build succeeds (the dictionary is not yet merged into `App.xaml`, so no runtime binding occurs yet — this step only confirms the XAML itself is well-formed and the font content files are included without error).

- [ ] **Step 5: Commit**

```bash
git add Kivi.App/Themes/Tokens.xaml Kivi.App/Assets/Fonts/ Kivi.App/Kivi.App.csproj
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): add real Kivi design tokens (colors, spacing, radii, fonts)

Transcribes ui/components/fig-tokens.css into Themes/Tokens.xaml
following impl-03's two-layer (primitive + semantic ThemeDictionaries)
pattern. Bundles Inter/Space Grotesk/Space Mono (all OFL) as the real
shipping fonts per the retrofit spec S3 - Matter/Season Mix are
unlicensed and not used.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2c: WinUI 3 app lifecycle + DI composition root (App.xaml/App.xaml.cs)

**Files:**
- Create: `Kivi.App/App.xaml`
- Create: `Kivi.App/App.xaml.cs`
- Modify: `Kivi.App/Program.cs` (becomes the `Main` entry point only, delegates to `App`)
- Create: `Kivi.App/DotEnv.cs` — already exists, verify it's still referenced correctly (no change expected, listed for completeness)

**Interfaces:**
- Consumes: `Themes/Tokens.xaml` (Task 2b), all existing `Kivi.Core`/`Kivi.Platform` DI registrations (unchanged from current `Program.cs`).
- Produces: `App` class (`Kivi.App.App : Application`) that owns the `IServiceProvider`, exposes a static `public static IServiceProvider Services { get; }` for views/viewmodels to resolve dependencies, and merges `Themes/Tokens.xaml` into `Application.Resources`. No visible window yet — `OnLaunched` is a no-op placeholder in this task (the tray host is added in Task 3b, the overlay in Task 3d). This task's job is purely to prove the WinUI3 app object model boots and the DI graph still resolves everything it did before.

- [ ] **Step 1: Create App.xaml wiring in the token dictionary**

Create `Kivi.App/App.xaml`:

```xml
<!-- Kivi.App/App.xaml -->
<Application
    x:Class="Kivi.App.App"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Application.Resources>
        <ResourceDictionary>
            <ResourceDictionary.MergedDictionaries>
                <XamlControlsResources xmlns="using:Microsoft.UI.Xaml.Controls"/>
                <ResourceDictionary Source="ms-appx:///Themes/Tokens.xaml"/>
            </ResourceDictionary.MergedDictionaries>
        </ResourceDictionary>
    </Application.Resources>
</Application>
```

- [ ] **Step 2: Create App.xaml.cs — move the existing DI composition root here**

Create `Kivi.App/App.xaml.cs`, moving the service-registration block from the current `Program.cs` almost verbatim:

```csharp
using Kivi.App;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Diagnostics;
using Kivi.Core.Http;
using Kivi.Core.Orchestration;
using Kivi.Core.Polish;
using Kivi.Core.Stt;
using Kivi.Platform.Audio;
using Kivi.Platform.Context;
using Kivi.Platform.Hotkey;
using Kivi.Platform.Paste;
using Kivi.Platform.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace Kivi.App;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        DotEnv.Load();

        bool metricsEnabled = Environment.GetEnvironmentVariable("KIVI_METRICS") == "1";

        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddUserSecrets(typeof(App).Assembly, optional: true)
            .Build();

        var configStore = new JsonAppConfigStore();
        var appConfig = configStore.Load();
        appConfig.MetricsEnabled = metricsEnabled;
        appConfig.Validate();

        var services = new ServiceCollection();
        services.AddLogging(b => b.AddSimpleConsole());
        services.AddSingleton(appConfig);
        services.AddSingleton<IAppConfigStore>(configStore);
        services.AddSingleton(new HttpClient());
        services.AddSingleton<OpenAiCompatibleClient>();
        services.AddSingleton(new KiviMetrics());

        services.AddSingleton<ISecretStore>(_ =>
        {
            var envKey = configuration["GROQ_API_KEY"];
            var dpapi = new DpapiSecretStore();
            if (!string.IsNullOrEmpty(envKey)) dpapi.SetApiKey(envKey);
            return dpapi;
        });

        services.AddSingleton<ISttEngine, GroqSttEngine>();
        services.AddSingleton<IPolishClient, GroqPolishClient>();
        services.AddSingleton<IHotkeyService, LowLevelKeyboardHookService>();
        services.AddSingleton<IAudioCaptureService, WasapiAudioCaptureService>();
        services.AddSingleton<IScreenContextProvider, UiaScreenContextProvider>();
        services.AddSingleton<IPasteService, SendInputPasteService>();
        services.AddSingleton<IDictationOrchestrator, DictationOrchestrator>();

        Services = services.BuildServiceProvider();

        var logger = Services.GetRequiredService<ILogger<App>>();
        var metrics = Services.GetRequiredService<KiviMetrics>();
        Observability.Start(metricsEnabled, metrics);

        var orchestrator = Services.GetRequiredService<IDictationOrchestrator>();
        orchestrator.StateChanged += s => logger.LogInformation("state -> {State}", s);
        orchestrator.Start();

        logger.LogInformation("Kivi ready. Hold RIGHT-CTRL to dictate. Metrics={Metrics}.", metricsEnabled);

        // No window shown yet - overlay/tray added in Task 3 (3a-3d).
    }
}
```

(Note: `Observability.Start`'s return value, previously assigned to `using var _obs`, is not disposed here since there's no natural `Main`-scoped `using` block in a WinUI3 app lifecycle — this is a known follow-up; add a field `private IDisposable? _obs;` on `App` and assign it, disposing in an `OnSuspending`/exit handler, if metrics-disposal-on-clean-exit turns out to matter. Out of scope to resolve fully in this task; note it in the task's commit message as a known simplification.)

Adjust the field to track for disposal:

```csharp
    private IDisposable? _obs;
    // ... inside OnLaunched, replace the bare call with:
        _obs = Observability.Start(metricsEnabled, metrics);
```

- [ ] **Step 3: Replace Program.cs with the minimal WinUI3 entry point**

Replace the entire contents of `Kivi.App/Program.cs`:

```csharp
using Microsoft.UI.Xaml;

namespace Kivi.App;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Application.Start(_ =>
        {
            var context = new global::Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                global::Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread());
            System.Threading.SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
```

- [ ] **Step 4: Confirm the project builds**

Run: `dotnet build Kivi.App`
Expected: Build succeeds. If `InitializeComponent()` is not found, confirm `App.xaml`'s `x:Class` matches the namespace/class name exactly and that the XAML compiler ran (check `obj/` for a generated `App.g.cs`).

- [ ] **Step 5: Manual smoke test**

Run: `dotnet run --project Kivi.App`
Expected: The process starts, logs "Kivi ready. Hold RIGHT-CTRL to dictate..." to the console (via the `SimpleConsole` logger, same as before), and the hotkey/orchestrator pipeline is live (test by holding Right Ctrl and speaking — confirm text still pastes into a focused app, exactly as it did before this conversion). No visible window appears yet, which is expected — report the console output and confirm the paste still works end-to-end.

- [ ] **Step 6: Commit**

```bash
git add Kivi.App/App.xaml Kivi.App/App.xaml.cs Kivi.App/Program.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): move DI composition root into WinUI3 App.xaml.cs

Program.cs is now a minimal STAThread WinUI3 entry point; App.xaml.cs
owns the DI container (moved near-verbatim from the old Program.cs)
and exposes App.Services statically for views/viewmodels. No visible
window yet - overlay and tray come in Task 3 (3a-3d). Verified the existing
hotkey->paste pipeline still works end-to-end after the conversion.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3a: OverlayViewModel — bind to the expanded RecordingState

**Files:**
- Create: `Kivi.App/ViewModels/OverlayViewModel.cs`

**Interfaces:**
- Consumes: `IDictationOrchestrator` (existing, from `Kivi.Core`), the 7-value `RecordingState` (Task 1a-1c).
- Produces: `OverlayViewModel : ObservableObject` with `[ObservableProperty] RecordingState State`, and bindable derived flags `IsListening`, `IsProcessing`, `IsSpeaking`, `IsWaiting`, `IsDone`, `IsError`, `IsVisible` (true whenever `State != Idle`), plus `public string StateColorTokenKey` returning the semantic brush key name (e.g. `"OverlayListeningBrush"`) for the current state — the orb control (Task 3c) binds to this to pick its dot color. Task 3c (`KiviOrbControl`) and Task 3d (`OverlayWindow`) both depend on this class's public surface exactly as named here.

- [ ] **Step 1: Create the view model**

Create `Kivi.App/ViewModels/OverlayViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Kivi.Core.Orchestration;
using Microsoft.UI.Dispatching;

namespace Kivi.App.ViewModels;

public partial class OverlayViewModel : ObservableObject
{
    private readonly IDictationOrchestrator _orch;
    private readonly DispatcherQueue _ui;

    public OverlayViewModel(IDictationOrchestrator orch, DispatcherQueue ui)
    {
        _orch = orch;
        _ui = ui;
        _orch.StateChanged += OnStateChanged;
        Apply(_orch.State);
    }

    [ObservableProperty] private RecordingState _state;

    public bool IsVisible    => State != RecordingState.Idle;
    public bool IsListening  => State == RecordingState.Listening;
    public bool IsProcessing => State == RecordingState.Processing;
    public bool IsSpeaking   => State == RecordingState.Speaking;
    public bool IsWaiting    => State == RecordingState.Waiting;
    public bool IsDone       => State == RecordingState.Done;
    public bool IsError      => State == RecordingState.Error;

    public string StateColorTokenKey => State switch
    {
        RecordingState.Idle       => "OverlayIdleBrush",
        RecordingState.Listening  => "OverlayListeningBrush",
        RecordingState.Processing => "OverlayProcessingBrush",
        RecordingState.Speaking   => "OverlaySpeakingBrush",
        RecordingState.Waiting    => "OverlayWaitingBrush",
        RecordingState.Done       => "OverlayDoneBrush",
        RecordingState.Error      => "OverlayErrorBrush",
        _                         => "OverlayIdleBrush"
    };

    private void OnStateChanged(RecordingState newState) => _ui.TryEnqueue(() => Apply(newState));

    private void Apply(RecordingState state)
    {
        State = state;
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsSpeaking));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(StateColorTokenKey));
    }
}
```

(Note: `IDictationOrchestrator.StateChanged` is `event Action<RecordingState>` per the actual current interface in `Kivi.Core/Orchestration/IDictationOrchestrator.cs` — not the `EventHandler<RecordingStateChangedEventArgs>` shape from the older impl-03 draft. `OnStateChanged` above matches the real signature: a single `RecordingState` parameter, no sender/args wrapper.)

- [ ] **Step 2: Confirm the project builds**

Run: `dotnet build Kivi.App`
Expected: Build succeeds. This class has no XAML/window dependency yet, so this step just confirms it compiles against the real `IDictationOrchestrator`/`RecordingState` shapes.

- [ ] **Step 3: Commit**

```bash
git add Kivi.App/ViewModels/OverlayViewModel.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): add OverlayViewModel bound to the 7-state RecordingState

Marshals IDictationOrchestrator.StateChanged onto the UI thread and
exposes bindable Is* flags plus a StateColorTokenKey the orb control
uses to pick its dot color per state.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3b: Tray host (H.NotifyIcon) with Start/Stop and Quit

**Files:**
- Create: `Kivi.App/ViewModels/TrayViewModel.cs`
- Create: `Kivi.App/Views/TrayWindow.xaml`
- Create: `Kivi.App/Views/TrayWindow.xaml.cs`
- Create: `Kivi.App/Assets/Tray/idle.ico`, `active.ico`, `error.ico`
- Modify: `Kivi.App/App.xaml.cs` (show the tray host at the end of `OnLaunched`)

**Interfaces:**
- Consumes: `IDictationOrchestrator` (existing), `OverlayViewModel`'s `RecordingState` mapping pattern (Task 3a, same `Apply`-on-`StateChanged` shape, independent instance).
- Produces: a resident `TrayWindow` that never closes for the app's lifetime, giving the user Start/Stop dictation and Quit from the taskbar. `IDictationOrchestrator` already has `Start()`/`Stop()` (no `StartAsync`/`StopAsync`/`CopyLastResultAgainAsync` — those were in the older impl-03 draft's aspirational interface, not the real one). This task uses only the interface members that actually exist today.

- [ ] **Step 1: Create simple placeholder tray icons**

Since no dedicated tray-icon asset export exists yet from the Figma file, create three minimal solid-color `.ico` placeholders under `Kivi.App/Assets/Tray/` for this task (idle = `KiviColorStateIdle` gray, active = `KiviColorLegGreen` green, error = `KiviColorStateError` red) using any `.ico` generation tool available, or a simple 16x16/32x32 solid square converted to `.ico`. These are placeholders — replacing them with real dot-matrix-mark-derived icons is a follow-up task once the orb control (Task 3c) exists and can be rendered to a bitmap, not part of this task's scope.

- [ ] **Step 2: Add the tray view model**

Create `Kivi.App/ViewModels/TrayViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kivi.Core.Orchestration;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kivi.App.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    private readonly IDictationOrchestrator _orch;
    private readonly DispatcherQueue _ui;

    public TrayViewModel(IDictationOrchestrator orch, DispatcherQueue ui)
    {
        _orch = orch;
        _ui = ui;
        _orch.StateChanged += s => _ui.TryEnqueue(() => Apply(s));
        Apply(_orch.State);
    }

    [ObservableProperty] private BitmapImage? _trayIcon;
    [ObservableProperty] private string _startStopLabel = "Start dictation";

    private void Apply(RecordingState state)
    {
        StartStopLabel = state == RecordingState.Idle ? "Start dictation" : "Stop dictation";
        var asset = state switch
        {
            RecordingState.Idle  => "ms-appx:///Assets/Tray/idle.ico",
            RecordingState.Error => "ms-appx:///Assets/Tray/error.ico",
            _                    => "ms-appx:///Assets/Tray/active.ico"
        };
        TrayIcon = new BitmapImage(new Uri(asset));
    }

    [RelayCommand]
    private void ToggleDictation()
    {
        if (_orch.State == RecordingState.Idle) _orch.Start();
        else _orch.Stop();
    }

    [RelayCommand]
    private void Quit() => Microsoft.UI.Xaml.Application.Current.Exit();
}
```

(This uses only `IDictationOrchestrator.State`/`.Start()`/`.Stop()`/`.StateChanged` — the real interface. `CopyLastResultAgainAsync`/`LastResult`/`LastError` from the older impl-03 draft do not exist on the real interface and are out of scope for this task; if last-result copy is wanted later, it requires first adding that capability to `IDictationOrchestrator` in `Kivi.Core`, which is a separate Core-layer task, not part of this UI retrofit.)

- [ ] **Step 3: Create the tray host window**

Create `Kivi.App/Views/TrayWindow.xaml`:

```xml
<!-- Kivi.App/Views/TrayWindow.xaml -->
<Window
    x:Class="Kivi.App.Views.TrayWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:tb="using:H.NotifyIcon">

    <Grid>
        <tb:TaskbarIcon x:Name="Tray"
                        ToolTipText="Kivi"
                        IconSource="{x:Bind ViewModel.TrayIcon, Mode=OneWay}"
                        LeftClickCommand="{x:Bind ViewModel.ToggleDictationCommand}">
            <tb:TaskbarIcon.ContextFlyout>
                <MenuFlyout>
                    <MenuFlyoutItem Text="{x:Bind ViewModel.StartStopLabel, Mode=OneWay}"
                                    Command="{x:Bind ViewModel.ToggleDictationCommand}"/>
                    <MenuFlyoutSeparator/>
                    <MenuFlyoutItem Text="Quit Kivi"
                                    Command="{x:Bind ViewModel.QuitCommand}"/>
                </MenuFlyout>
            </tb:TaskbarIcon.ContextFlyout>
        </tb:TaskbarIcon>
    </Grid>
</Window>
```

Create `Kivi.App/Views/TrayWindow.xaml.cs`:

```csharp
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml;

namespace Kivi.App.Views;

public sealed partial class TrayWindow : Window
{
    public TrayViewModel ViewModel { get; }

    public TrayWindow(TrayViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
```

- [ ] **Step 4: Show the tray host from App.xaml.cs**

Edit `Kivi.App/App.xaml.cs` — add the field and instantiate at the end of `OnLaunched` (after the existing `orchestrator.Start();` call):

```csharp
    private Views.TrayWindow? _trayWindow;

    // ... at the end of OnLaunched, after orchestrator.Start():
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        var trayVm = new ViewModels.TrayViewModel(orchestrator, dispatcher);
        _trayWindow = new Views.TrayWindow(trayVm);
        _trayWindow.Activate();
```

(`DispatcherQueue` requires `using Microsoft.UI.Dispatching;` at the top of `App.xaml.cs` — add it alongside the existing usings.)

- [ ] **Step 5: Confirm the project builds**

Run: `dotnet build Kivi.App`
Expected: Build succeeds.

- [ ] **Step 6: Manual smoke test**

Run: `dotnet run --project Kivi.App`
Expected: A Kivi tray icon appears in the Windows system tray. Right-click it — confirm the context menu shows "Start dictation" (or "Stop dictation" if already listening) and "Quit Kivi". Click "Quit Kivi" — confirm the app exits cleanly. Re-run and click the tray icon directly (left-click) — confirm it toggles dictation on/off. Report what you observed.

- [ ] **Step 7: Commit**

```bash
git add Kivi.App/ViewModels/TrayViewModel.cs Kivi.App/Views/TrayWindow.xaml Kivi.App/Views/TrayWindow.xaml.cs Kivi.App/Assets/Tray/ Kivi.App/App.xaml.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): add H.NotifyIcon tray host (Start/Stop, Quit)

Kivi is now a tray-resident app per impl-03 S4. Tray icons are
placeholder solid colors for now - real dot-matrix-derived icons
follow once the orb control (Task 3c) can render to a bitmap.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3c: KiviOrbControl — the dot-matrix status mark

**Files:**
- Create: `Kivi.App/Assets/Icons/kivi-mask.png` (or `.svg` — the traced 120x162 silhouette mask)
- Create: `Kivi.App/Controls/KiviOrbControl.cs`
- Create: `Kivi.App/Controls/KiviOrbControl.xaml` (if using a templated `Control` rather than a pure code-drawn `Canvas` subclass — chosen approach determined in Step 1)

**Interfaces:**
- Consumes: `OverlayViewModel.StateColorTokenKey` (Task 3a) to resolve which semantic brush to tint dots with; the mask asset from this task's Step 1.
- Produces: `KiviOrbControl`, a `Control` with a `DependencyProperty` `public RecordingState State { get; set; }` and a `Posture` enum property (`RestPill`, `Woken`, `Satellites`, `Box` — matching the design's 4 named postures) that determines its rendered size (39×15, 61×61, 23×23, 322×108 respectively, per the mockups page). `OverlayWindow` (Task 3d) hosts one instance of this control and drives its `State`/`Posture` properties from `OverlayViewModel`.

- [ ] **Step 1: Export or trace the silhouette mask asset**

From the Figma file (or the already-exported `ui/02 - brand.png` / `ui/kivi design.png`), export the kiwi silhouette as a single-color alpha-masked PNG at a resolution that divides cleanly for a 24-column dot sample (e.g. 240×324, exactly 2x the stated 120×162 trace size). Save it as `Kivi.App/Assets/Icons/kivi-mask.png`. Add it to `Kivi.App.csproj` as content:

```xml
  <ItemGroup>
    <Content Include="Assets\Icons\kivi-mask.png">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
```

- [ ] **Step 2: Create the orb control**

Create `Kivi.App/Controls/KiviOrbControl.cs` — a `Canvas`-based control that samples the mask into a grid of `Ellipse` dots (simplest correct approach; avoids a `Win2D` dependency the spec didn't require):

```csharp
using Kivi.Core.Orchestration;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Storage.Streams;
using Windows.Graphics.Imaging;

namespace Kivi.App.Controls;

public sealed class KiviOrbControl : Canvas
{
    public static readonly DependencyProperty StateProperty =
        DependencyProperty.Register(nameof(State), typeof(RecordingState), typeof(KiviOrbControl),
            new PropertyMetadata(RecordingState.Idle, OnStateChanged));

    public RecordingState State
    {
        get => (RecordingState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    private const int Columns = 24;
    private SoftwareBitmap? _mask;
    private readonly List<Ellipse> _dots = new();

    public KiviOrbControl()
    {
        Loaded += async (_, _) => await LoadMaskAndBuildDotsAsync();
    }

    private async Task LoadMaskAndBuildDotsAsync()
    {
        var uri = new Uri("ms-appx:///Assets/Icons/kivi-mask.png");
        var file = await Windows.Storage.StorageFile.GetFileFromApplicationUriAsync(uri);
        using var stream = await file.OpenAsync(Windows.Storage.FileAccessMode.Read);
        var decoder = await BitmapDecoder.CreateAsync(stream);
        _mask = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
        BuildDots();
        ApplyStateColor();
    }

    private void BuildDots()
    {
        if (_mask is null) return;
        Children.Clear();
        _dots.Clear();

        int rows = (int)((double)_mask.PixelHeight / _mask.PixelWidth * Columns);
        double cellW = ActualWidth > 0 ? ActualWidth / Columns : 120.0 / Columns;
        double cellH = ActualHeight > 0 ? ActualHeight / rows : 162.0 / rows;
        double dotSize = Math.Min(cellW, cellH) * 0.7;

        var buffer = new byte[4 * _mask.PixelWidth * _mask.PixelHeight];
        _mask.CopyToBuffer(buffer.AsBuffer());

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < Columns; col++)
            {
                int px = (int)((double)col / Columns * _mask.PixelWidth);
                int py = (int)((double)row / rows * _mask.PixelHeight);
                int offset = (py * _mask.PixelWidth + px) * 4;
                byte alpha = offset + 3 < buffer.Length ? buffer[offset + 3] : (byte)0;
                if (alpha < 32) continue; // transparent -> no dot here

                var dot = new Ellipse { Width = dotSize, Height = dotSize };
                SetLeft(dot, col * cellW + (cellW - dotSize) / 2);
                SetTop(dot, row * cellH + (cellH - dotSize) / 2);
                Children.Add(dot);
                _dots.Add(dot);
            }
        }
    }

    private static void OnStateChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((KiviOrbControl)d).ApplyStateColor();

    private void ApplyStateColor()
    {
        var key = State switch
        {
            RecordingState.Idle       => "OverlayIdleBrush",
            RecordingState.Listening  => "OverlayListeningBrush",
            RecordingState.Processing => "OverlayProcessingBrush",
            RecordingState.Speaking   => "OverlaySpeakingBrush",
            RecordingState.Waiting    => "OverlayWaitingBrush",
            RecordingState.Done       => "OverlayDoneBrush",
            RecordingState.Error      => "OverlayErrorBrush",
            _                         => "OverlayIdleBrush"
        };
        if (Application.Current.Resources.TryGetValue(key, out var brushObj) && brushObj is Brush brush)
        {
            foreach (var dot in _dots) dot.Fill = brush;
        }
    }
}
```

(This is a straightforward, dependency-free implementation — `SoftwareBitmap`/`BitmapDecoder` are part of the Windows SDK projection already available via `Microsoft.WindowsAppSDK`, no extra package needed. Motion/breathing animation is explicitly deferred to Task 3d, where the control is hosted inside `OverlayWindow` and can have a `Storyboard` applied externally — keeping `KiviOrbControl` itself simple and motion-agnostic.)

- [ ] **Step 3: Confirm the project builds**

Run: `dotnet build Kivi.App`
Expected: Build succeeds.

- [ ] **Step 4: Manual smoke test via a temporary test harness**

Since `KiviOrbControl` has no host window yet (that's Task 3d), temporarily add it to `TrayWindow.xaml`'s `Grid` for visual verification only (do not commit this temporary change — revert it after checking):

```xml
        <local:KiviOrbControl xmlns:local="using:Kivi.App.Controls" Width="120" Height="162" State="Listening"/>
```

Run: `dotnet run --project Kivi.App`, make the `TrayWindow` visible temporarily (it's normally not activated with visible content — for this smoke test only, confirm the dot-matrix bird renders as a grid of colored dots roughly matching the silhouette shape from `ui/02 - brand.png`). Revert the temporary XAML addition before committing.

- [ ] **Step 5: Commit**

```bash
git add Kivi.App/Assets/Icons/kivi-mask.png Kivi.App/Controls/KiviOrbControl.cs Kivi.App/Kivi.App.csproj
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): add KiviOrbControl - dot-matrix status mark

Procedurally samples the traced kiwi silhouette mask onto a 24-column
dot grid per the brand page spec, tinting dots by RecordingState via
the semantic Overlay*Brush tokens from Task 2b/3a. Motion/breathing
animation is applied externally by OverlayWindow (Task 3d), not built
into this control.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3d: OverlayWindow — borderless, click-through, posture-driven

**Files:**
- Create: `Kivi.App/Interop/NativeMethods.cs`
- Create: `Kivi.App/Views/OverlayWindow.xaml`
- Create: `Kivi.App/Views/OverlayWindow.xaml.cs`
- Modify: `Kivi.App/App.xaml.cs` (create and show the overlay alongside the tray)

**Interfaces:**
- Consumes: `OverlayViewModel` (Task 3a), `KiviOrbControl` (Task 3c).
- Produces: a borderless, always-on-top, click-through window anchored bottom-center of the screen that shows/hides and resizes per `RecordingState`, hosting one `KiviOrbControl`. This is the visible "orb overlay" the whole design centers on.

- [ ] **Step 1: Add the Win32 interop for click-through**

Create `Kivi.App/Interop/NativeMethods.cs`:

```csharp
using System.Runtime.InteropServices;

namespace Kivi.App.Interop;

internal static class NativeMethods
{
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED     = 0x00080000;
    public const int WS_EX_TOOLWINDOW  = 0x00000080;
    public const uint LWA_ALPHA        = 0x00000002;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    public static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    public static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetLayeredWindowAttributes(nint hWnd, uint crKey, byte bAlpha, uint dwFlags);
}
```

- [ ] **Step 2: Create the overlay window XAML**

Create `Kivi.App/Views/OverlayWindow.xaml`:

```xml
<!-- Kivi.App/Views/OverlayWindow.xaml -->
<Window
    x:Class="Kivi.App.Views.OverlayWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:controls="using:Kivi.App.Controls">

    <Grid x:Name="Root" Background="Transparent">
        <controls:KiviOrbControl x:Name="Orb"
                                  HorizontalAlignment="Center" VerticalAlignment="Center"/>
    </Grid>
</Window>
```

- [ ] **Step 3: Create the overlay window code-behind — presenter, click-through, posture sizing**

Create `Kivi.App/Views/OverlayWindow.xaml.cs`:

```csharp
using Kivi.App.Interop;
using Kivi.App.ViewModels;
using Kivi.Core.Orchestration;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Graphics;

namespace Kivi.App.Views;

public sealed partial class OverlayWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly nint _hwnd;
    private readonly OverlayViewModel _vm;

    public OverlayWindow(OverlayViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        Orb.State = vm.State;

        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsAlwaysOnTop = true;
        presenter.IsResizable = false;
        presenter.IsMaximizable = false;
        presenter.IsMinimizable = false;
        _appWindow.SetPresenter(presenter);
        _appWindow.IsShownInSwitchers = false;

        MakeClickThrough();

        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(OverlayViewModel.State) or nameof(OverlayViewModel.IsVisible))
                ApplyState();
        };
        ApplyState();
    }

    private void ApplyState()
    {
        Orb.State = _vm.State;
        var (w, h) = _vm.State switch
        {
            RecordingState.Idle       => (39, 15),   // rest pill
            RecordingState.Listening  => (61, 61),   // woken
            RecordingState.Processing => (61, 61),
            RecordingState.Waiting    => (23, 23),   // satellites
            RecordingState.Speaking   => (322, 108), // box
            RecordingState.Done       => (61, 61),
            RecordingState.Error      => (61, 61),
            _                         => (39, 15)
        };
        _appWindow.Resize(new SizeInt32(w, h));

        if (_vm.IsVisible) ShowAnchoredBottomCenter();
        else _appWindow.Hide();
    }

    private void ShowAnchoredBottomCenter()
    {
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int w = _appWindow.Size.Width, h = _appWindow.Size.Height;
        var pos = new PointInt32(area.X + (area.Width - w) / 2, area.Y + area.Height - h - 48);
        _appWindow.Move(pos);
        _appWindow.Show(activateWindow: false);
    }

    private void MakeClickThrough()
    {
        nint ex = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE, ex);
        NativeMethods.SetLayeredWindowAttributes(_hwnd, 0, 255, NativeMethods.LWA_ALPHA);
    }
}
```

(Sizes above use the design's stated dimensions — rest pill 39×15, woken 61 (square), satellites 23 (square), box 322×108 — mapped onto the 7 states per the spec's decision that "Speaking" is the paste-injection stage, which is where a transcript box would plausibly show per the mockups; `Processing`/`Done`/`Error` reuse the "woken" 61×61 posture since the design doesn't define separate postures for every one of the 7 states, only 4 named postures. This mapping is a reasonable default — flag it for design review once real screenshots of each posture are available, per spec §7's deferred items.)

- [ ] **Step 4: Show the overlay from App.xaml.cs**

Edit `Kivi.App/App.xaml.cs` — add alongside the tray window creation at the end of `OnLaunched`:

```csharp
    private Views.OverlayWindow? _overlayWindow;

    // ... after creating _trayWindow:
        var overlayVm = new ViewModels.OverlayViewModel(orchestrator, dispatcher);
        _overlayWindow = new Views.OverlayWindow(overlayVm);
```

- [ ] **Step 5: Confirm the project builds**

Run: `dotnet build Kivi.App`
Expected: Build succeeds.

- [ ] **Step 6: Manual smoke test**

Run: `dotnet run --project Kivi.App`. Hold Right Ctrl and speak — confirm a small orb appears bottom-center of the screen, changes size/visibility as the state progresses (Listening → Processing → Speaking → Done → Idle-hidden). Try clicking on something directly behind the overlay while it's visible — confirm the click passes through to the app underneath (click-through works). Report what you observed at each stage.

- [ ] **Step 7: Commit**

```bash
git add Kivi.App/Interop/NativeMethods.cs Kivi.App/Views/OverlayWindow.xaml Kivi.App/Views/OverlayWindow.xaml.cs Kivi.App/App.xaml.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): add borderless click-through OverlayWindow hosting the orb

Bottom-center anchored, always-on-top, WS_EX_TRANSPARENT click-through
per impl-03 S3. Resizes per RecordingState using the design's stated
posture dimensions (rest pill/woken/satellites/box). Posture-to-state
mapping is a reasonable default pending real per-posture design
screenshots - flagged for follow-up review.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: Settings shell — NavigationView with 7 nav items, 2 functional

**Files:**
- Create: `Kivi.App/Views/SettingsWindow.xaml`
- Create: `Kivi.App/Views/SettingsWindow.xaml.cs`
- Create: `Kivi.App/Views/Settings/RecordPage.xaml(.cs)`
- Create: `Kivi.App/Views/Settings/SettingsPage.xaml(.cs)` (the real, functional settings page — Account/Models/Input/Text/Appearance content, per spec §5)
- Create: `Kivi.App/Views/Settings/ComingSoonPage.xaml(.cs)` (one reusable stub page for History/Personas/Presets/Memory/Analytics)
- Create: `Kivi.App/ViewModels/SettingsViewModel.cs`
- Modify: `Kivi.App/ViewModels/TrayViewModel.cs` (add an "Open Settings" menu item)
- Modify: `Kivi.App/Views/TrayWindow.xaml` (wire the new menu item)
- Modify: `Kivi.App/App.xaml.cs` (register a way to open the settings window)

**Interfaces:**
- Consumes: `AppConfig`/`IAppConfigStore` (existing, from `Kivi.Core`), design tokens (Task 2b).
- Produces: a `SettingsWindow` reachable from the tray, with a 7-item nav (record, history, personas, presets, memory, analytics, settings) where only **record** (shortcut to focus the overlay) and **settings** (real `AppConfig`-bound form) show functional content; the other 5 show the shared `ComingSoonPage` stub.

- [ ] **Step 1: Create the SettingsViewModel bound to real AppConfig fields**

Create `Kivi.App/ViewModels/SettingsViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;

namespace Kivi.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly IAppConfigStore _store;
    private readonly ISecretStore _secrets;

    public SettingsViewModel(AppConfig config, IAppConfigStore store, ISecretStore secrets)
    {
        _config = config;
        _store = store;
        _secrets = secrets;
        ApiKey = _secrets.GetApiKey() ?? "";
        SttBaseUrl = _config.TranscriptionBaseUrl;
        CleanupBaseUrl = _config.ChatBaseUrl;
        TranscriptionModel = _config.TranscriptionModel;
        CleanupModel = _config.CleanupModel;
        CustomVocabulary = _config.CustomVocabulary;
        PressEnterEnabled = _config.PressEnterCommandEnabled;
    }

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _sttBaseUrl = "";
    [ObservableProperty] private string _cleanupBaseUrl = "";
    [ObservableProperty] private string _transcriptionModel = "";
    [ObservableProperty] private string _cleanupModel = "";
    [ObservableProperty] private string _customVocabulary = "";
    [ObservableProperty] private bool _pressEnterEnabled;

    partial void OnApiKeyChanged(string value) => _secrets.SetApiKey(value);
    partial void OnSttBaseUrlChanged(string value) => _config.TranscriptionBaseUrl = value;
    partial void OnCleanupBaseUrlChanged(string value) => _config.ChatBaseUrl = value;
    partial void OnTranscriptionModelChanged(string value) => _config.TranscriptionModel = value;
    partial void OnCleanupModelChanged(string value) => _config.CleanupModel = value;
    partial void OnCustomVocabularyChanged(string value) => _config.CustomVocabulary = value;
    partial void OnPressEnterEnabledChanged(bool value) => _config.PressEnterCommandEnabled = value;

    [RelayCommand]
    private void Save() => _store.Save(_config);
}
```

(Uses the real `ISecretStore.GetApiKey()/SetApiKey(string)` and `IAppConfigStore.Save(AppConfig)` signatures already defined in `Kivi.Core` — not the aspirational `Load`/`PingAsync` methods from the older impl-03 draft, which don't exist on the real interfaces.)

- [ ] **Step 2: Create the reusable ComingSoonPage stub**

Create `Kivi.App/Views/Settings/ComingSoonPage.xaml`:

```xml
<!-- Kivi.App/Views/Settings/ComingSoonPage.xaml -->
<Page
    x:Class="Kivi.App.Views.Settings.ComingSoonPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="{ThemeResource KiviSpaceS4}">
        <TextBlock x:Name="TitleText"
                   Foreground="{ThemeResource KiviTextPrimaryBrush}"
                   FontFamily="{ThemeResource KiviFontFamily}"
                   FontSize="{ThemeResource KiviFontSizeTitle}"
                   FontWeight="{ThemeResource KiviFontWeightSemibold}"
                   HorizontalAlignment="Center"/>
        <TextBlock Text="Coming soon"
                   Foreground="{ThemeResource KiviTextSecondaryBrush}"
                   FontFamily="{ThemeResource KiviFontFamily}"
                   FontSize="{ThemeResource KiviFontSizeBody}"
                   HorizontalAlignment="Center"/>
    </StackPanel>
</Page>
```

Create `Kivi.App/Views/Settings/ComingSoonPage.xaml.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kivi.App.Views.Settings;

public sealed partial class ComingSoonPage : Page
{
    public ComingSoonPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        TitleText.Text = e.Parameter as string ?? "Coming soon";
    }
}
```

- [ ] **Step 3: Create the real Settings page (Account/Models/Text sections combined onto one page)**

Create `Kivi.App/Views/Settings/SettingsPage.xaml`:

```xml
<!-- Kivi.App/Views/Settings/SettingsPage.xaml -->
<Page
    x:Class="Kivi.App.Views.Settings.SettingsPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <ScrollViewer>
        <StackPanel Spacing="{ThemeResource KiviSpaceS8}" MaxWidth="520" HorizontalAlignment="Left"
                    Padding="{ThemeResource KiviSpaceS12}">

            <TextBlock Text="Settings"
                       Foreground="{ThemeResource KiviTextPrimaryBrush}"
                       FontFamily="{ThemeResource KiviFontFamily}"
                       FontSize="{ThemeResource KiviFontSizeTitle}"
                       FontWeight="{ThemeResource KiviFontWeightSemibold}"/>

            <StackPanel Spacing="{ThemeResource KiviSpaceS4}">
                <TextBlock Text="API key" Foreground="{ThemeResource KiviTextSecondaryBrush}"
                           FontSize="{ThemeResource KiviFontSizeCaption}"/>
                <PasswordBox Password="{x:Bind ViewModel.ApiKey, Mode=TwoWay}" PlaceholderText="gsk_…"/>
            </StackPanel>

            <StackPanel Spacing="{ThemeResource KiviSpaceS4}">
                <TextBlock Text="Transcription base URL" Foreground="{ThemeResource KiviTextSecondaryBrush}"
                           FontSize="{ThemeResource KiviFontSizeCaption}"/>
                <TextBox Text="{x:Bind ViewModel.SttBaseUrl, Mode=TwoWay}"/>
            </StackPanel>

            <StackPanel Spacing="{ThemeResource KiviSpaceS4}">
                <TextBlock Text="Cleanup base URL" Foreground="{ThemeResource KiviTextSecondaryBrush}"
                           FontSize="{ThemeResource KiviFontSizeCaption}"/>
                <TextBox Text="{x:Bind ViewModel.CleanupBaseUrl, Mode=TwoWay}"/>
            </StackPanel>

            <StackPanel Spacing="{ThemeResource KiviSpaceS4}">
                <TextBlock Text="Transcription model" Foreground="{ThemeResource KiviTextSecondaryBrush}"
                           FontSize="{ThemeResource KiviFontSizeCaption}"/>
                <TextBox Text="{x:Bind ViewModel.TranscriptionModel, Mode=TwoWay}"/>
            </StackPanel>

            <StackPanel Spacing="{ThemeResource KiviSpaceS4}">
                <TextBlock Text="Cleanup model" Foreground="{ThemeResource KiviTextSecondaryBrush}"
                           FontSize="{ThemeResource KiviFontSizeCaption}"/>
                <TextBox Text="{x:Bind ViewModel.CleanupModel, Mode=TwoWay}"/>
            </StackPanel>

            <StackPanel Spacing="{ThemeResource KiviSpaceS4}">
                <TextBlock Text="Custom vocabulary (comma or newline separated)"
                           Foreground="{ThemeResource KiviTextSecondaryBrush}"
                           FontSize="{ThemeResource KiviFontSizeCaption}"/>
                <TextBox Text="{x:Bind ViewModel.CustomVocabulary, Mode=TwoWay}" AcceptsReturn="True" Height="80" TextWrapping="Wrap"/>
            </StackPanel>

            <ToggleSwitch Header="Press Enter after paste" IsOn="{x:Bind ViewModel.PressEnterEnabled, Mode=TwoWay}"/>

            <TextBlock Text="Hotkey: hold Right Ctrl to dictate"
                       Foreground="{ThemeResource KiviTextSecondaryBrush}"
                       FontSize="{ThemeResource KiviFontSizeCaption}"/>

            <Button Content="Save" Command="{x:Bind ViewModel.SaveCommand}"/>
        </StackPanel>
    </ScrollViewer>
</Page>
```

Create `Kivi.App/Views/Settings/SettingsPage.xaml.cs`:

```csharp
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.Settings;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; }

    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
    }
}
```

(Note: the hotkey is documented as fixed to Right Ctrl in this pass — a capture-your-own-hotkey control is out of scope, since `Kivi.Core`'s `IHotkeyService` doesn't currently expose a way to change the bound key at runtime; that's a separate `Kivi.Core`/`Kivi.Platform` feature, not part of this UI retrofit.)

- [ ] **Step 4: Create RecordPage (a shortcut, not new content)**

Create `Kivi.App/Views/Settings/RecordPage.xaml`:

```xml
<!-- Kivi.App/Views/Settings/RecordPage.xaml -->
<Page
    x:Class="Kivi.App.Views.Settings.RecordPage"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="{ThemeResource KiviSpaceS4}">
        <TextBlock Text="Hold Right Ctrl anywhere to dictate."
                   Foreground="{ThemeResource KiviTextPrimaryBrush}"
                   FontFamily="{ThemeResource KiviFontFamily}"
                   FontSize="{ThemeResource KiviFontSizeBody}"
                   HorizontalAlignment="Center"/>
        <TextBlock Text="The orb appears at the bottom of your screen while dictating."
                   Foreground="{ThemeResource KiviTextSecondaryBrush}"
                   FontSize="{ThemeResource KiviFontSizeCaption}"
                   HorizontalAlignment="Center"/>
    </StackPanel>
</Page>
```

Create `Kivi.App/Views/Settings/RecordPage.xaml.cs`:

```csharp
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.Settings;

public sealed partial class RecordPage : Page
{
    public RecordPage()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5: Create the SettingsWindow shell with the 7-item NavigationView**

Create `Kivi.App/Views/SettingsWindow.xaml`:

```xml
<!-- Kivi.App/Views/SettingsWindow.xaml -->
<Window
    x:Class="Kivi.App.Views.SettingsWindow"
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <NavigationView x:Name="Nav"
                    PaneDisplayMode="Left"
                    IsSettingsVisible="False"
                    IsBackButtonVisible="Collapsed"
                    PaneTitle="Kivi"
                    SelectionChanged="Nav_SelectionChanged"
                    Background="{ThemeResource KiviSurfaceBrush}">
        <NavigationView.MenuItems>
            <NavigationViewItem Content="Record"    Tag="record"    IsSelected="True"/>
            <NavigationViewItem Content="History"   Tag="history"/>
            <NavigationViewItem Content="Personas"  Tag="personas"/>
            <NavigationViewItem Content="Presets"   Tag="presets"/>
            <NavigationViewItem Content="Memory"    Tag="memory"/>
            <NavigationViewItem Content="Analytics" Tag="analytics"/>
            <NavigationViewItem Content="Settings"  Tag="settings"/>
        </NavigationView.MenuItems>

        <Frame x:Name="ContentFrame" Padding="{ThemeResource KiviSpaceS12}"/>
    </NavigationView>
</Window>
```

Create `Kivi.App/Views/SettingsWindow.xaml.cs`:

```csharp
using Kivi.App.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace Kivi.App.Views;

public sealed partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _settingsVm;

    public SettingsWindow(SettingsViewModel settingsVm)
    {
        InitializeComponent();
        _settingsVm = settingsVm;
        Nav_SelectionChanged(Nav, null!);
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs? e)
    {
        var tag = (Nav.SelectedItem as NavigationViewItem)?.Tag as string ?? "record";
        switch (tag)
        {
            case "record":
                ContentFrame.Navigate(typeof(Settings.RecordPage), null, new EntranceNavigationTransitionInfo());
                break;
            case "settings":
                ContentFrame.Content = new Settings.SettingsPage(_settingsVm);
                break;
            case "history":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "History", new EntranceNavigationTransitionInfo());
                break;
            case "personas":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "Personas", new EntranceNavigationTransitionInfo());
                break;
            case "presets":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "Presets", new EntranceNavigationTransitionInfo());
                break;
            case "memory":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "Memory", new EntranceNavigationTransitionInfo());
                break;
            case "analytics":
                ContentFrame.Navigate(typeof(Settings.ComingSoonPage), "Analytics", new EntranceNavigationTransitionInfo());
                break;
        }
    }
}
```

(`SettingsPage` is set via `ContentFrame.Content =` directly, not `Frame.Navigate`, because it needs constructor injection of `SettingsViewModel` — `Frame.Navigate(Type, parameter)` requires a parameterless constructor. This is a deliberate, minimal deviation from the uniform `Navigate` pattern used for the other pages, scoped to the one page that needs a real view model.)

- [ ] **Step 6: Wire "Open Settings" from the tray menu**

Edit `Kivi.App/Views/TrayWindow.xaml` — add a menu item:

```xml
                    <MenuFlyoutItem Text="{x:Bind ViewModel.StartStopLabel, Mode=OneWay}"
                                    Command="{x:Bind ViewModel.ToggleDictationCommand}"/>
                    <MenuFlyoutItem Text="Settings…"
                                    Command="{x:Bind ViewModel.OpenSettingsCommand}"/>
                    <MenuFlyoutSeparator/>
                    <MenuFlyoutItem Text="Quit Kivi"
                                    Command="{x:Bind ViewModel.QuitCommand}"/>
```

Edit `Kivi.App/ViewModels/TrayViewModel.cs` — add an `Action` callback and command:

```csharp
public partial class TrayViewModel : ObservableObject
{
    private readonly IDictationOrchestrator _orch;
    private readonly DispatcherQueue _ui;
    private readonly Action _openSettings;

    public TrayViewModel(IDictationOrchestrator orch, DispatcherQueue ui, Action openSettings)
    {
        _orch = orch;
        _ui = ui;
        _openSettings = openSettings;
        _orch.StateChanged += s => _ui.TryEnqueue(() => Apply(s));
        Apply(_orch.State);
    }

    // ... existing members unchanged ...

    [RelayCommand]
    private void OpenSettings() => _openSettings();
}
```

(This changes `TrayViewModel`'s constructor signature — update its instantiation in `App.xaml.cs` accordingly in the next step.)

- [ ] **Step 7: Update App.xaml.cs to construct SettingsViewModel/SettingsWindow and pass the open-callback**

Edit `Kivi.App/App.xaml.cs`:

```csharp
    private Views.SettingsWindow? _settingsWindow;

    // ... replace the TrayViewModel construction line with:
        var settingsVm = new ViewModels.SettingsViewModel(
            appConfig,
            Services.GetRequiredService<IAppConfigStore>(),
            Services.GetRequiredService<ISecretStore>());

        var trayVm = new ViewModels.TrayViewModel(orchestrator, dispatcher, () =>
        {
            if (_settingsWindow is null)
            {
                _settingsWindow = new Views.SettingsWindow(settingsVm);
            }
            _settingsWindow.Activate();
        });
```

- [ ] **Step 8: Confirm the project builds**

Run: `dotnet build Kivi.App`
Expected: Build succeeds.

- [ ] **Step 9: Manual smoke test**

Run: `dotnet run --project Kivi.App`. Right-click the tray icon, click "Settings…" — confirm the Settings window opens showing the 7-item nav. Click through History/Personas/Presets/Memory/Analytics — confirm each shows its "Coming soon" stub with the correct title. Click Settings — confirm the real form appears with the current API key (masked), base URLs, models, custom vocabulary, and the press-Enter toggle, all pre-populated from the actual `AppConfig`. Change a value (e.g. custom vocabulary) and click Save — confirm no error is thrown. Click Record — confirm the static instructional text appears. Report what you observed for each nav item.

- [ ] **Step 10: Commit**

```bash
git add Kivi.App/Views/SettingsWindow.xaml Kivi.App/Views/SettingsWindow.xaml.cs Kivi.App/Views/Settings/ Kivi.App/ViewModels/SettingsViewModel.cs Kivi.App/ViewModels/TrayViewModel.cs Kivi.App/Views/TrayWindow.xaml Kivi.App/App.xaml.cs
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
feat(app): add Settings shell with 7-item nav (2 functional, 5 stubbed)

NavigationView matches the Figma sidebar order (record, history,
personas, presets, memory, analytics, settings). Record and Settings
are fully functional (Settings binds to real AppConfig/ISecretStore);
History/Personas/Presets/Memory/Analytics show a shared "coming soon"
stub since none have backing storage in Kivi.Core yet, per retrofit
spec S5.

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Whole-app manual verification pass

**Files:** none modified — this task is verification only, confirming Tasks 2-4 work together as a whole.

**Interfaces:** none new.

- [ ] **Step 1: Full end-to-end smoke test**

Run: `dotnet run --project Kivi.App`. Perform, in order, and report the result of each:
1. Confirm the tray icon appears and shows the idle icon.
2. Hold Right Ctrl and speak a short phrase, then release. Confirm: the overlay orb appears bottom-center, resizes/changes color through Listening → Processing → Speaking → Done → disappears (Idle), and the cleaned text is pasted into whatever app has focus.
3. While the orb is visible mid-dictation, click on the desktop or another window directly behind/through the orb's screen position — confirm the click reaches the app underneath (click-through still works with the real orb, not just the earlier placeholder pill).
4. Open Settings from the tray, verify all 7 nav items render as expected (2 functional, 5 "coming soon").
5. In Settings, change the custom vocabulary field, click Save, close the window, reopen Settings — confirm the change persisted (round-trips through `IAppConfigStore`).
6. Quit via the tray menu — confirm the process exits cleanly (no orphaned window, no crash dialog).

- [ ] **Step 2: Run the full Kivi.Core.Tests suite one more time to confirm no regressions from the whole plan**

Run: `dotnet test Kivi.Core.Tests`
Expected: All tests pass (this re-confirms Phase A's changes didn't regress under the full solution build produced by Phase B).

- [ ] **Step 3: Confirm the whole solution builds cleanly**

Run: `dotnet build`
Expected: `Kivi.Core`, `Kivi.Core.Tests`, `Kivi.Platform`, and `Kivi.App` all build with no errors or new warnings introduced by this plan.

- [ ] **Step 4: Update the progress ledger**

Append to `.superpowers/sdd/progress.md` (create the file if a prior one from the non-UI build no longer exists at that path):

```markdown

## KIVI UI FIGMA RETROFIT — Tasks 1-5 complete (manual E2E verified)

State model expanded (Idle/Listening/Processing/Speaking/Waiting/Done/Error) per
docs/superpowers/specs/2026-07-20-kivi-ui-figma-retrofit-design.md. GroqPolishClient's
rate-limit cooldown now surfaces as Waiting; a transient Done state follows a
successful paste. Kivi.App converted to WinUI3 (unpackaged, self-contained):
real design tokens from ui/components/fig-tokens.css, a KiviOrbControl rendering
the dot-matrix kiwi mark as the live status indicator, a borderless click-through
overlay window (bottom-center anchored, posture-sized per state), an H.NotifyIcon
tray host, and a 7-item Settings nav (Record + Settings functional; History/
Personas/Presets/Memory/Analytics stubbed as "coming soon" pending their own specs).

Manually verified end-to-end: hotkey -> orb appears/resizes/recolors through the
full state sequence -> cleaned text pastes -> orb disappears; click-through
confirmed with the real orb; Settings round-trips through IAppConfigStore; tray
Quit exits cleanly.

Deferred (not part of this plan, each needs its own spec): History/Personas/
Presets/Memory/Analytics backends; Matter/Season Mix font licensing (Inter used
for now); real per-posture design screenshots to verify the posture-to-state
size mapping in OverlayWindow; real dot-matrix-derived tray icons (placeholders
used in Task 3b); POA #5 perf validation run against the WinUI3 build; POA #6
installer.
```

- [ ] **Step 5: Commit the ledger update**

```bash
git add .superpowers/sdd/progress.md
git -c user.name="Agrim" -c user.email="agrim@sarvam.ai" commit -m "$(cat <<'EOF'
docs: record Kivi UI Figma retrofit completion in progress ledger

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-Review Notes

**Spec coverage:** §1 (state model) → Task 1 (1a-1c). §2 (tokens) → Task 2b. §3 (fonts) → Task 2b Steps 2-3. §4 (orb replaces pill, postures, click-through-always) → Task 3 (3c-3d). §5 (nav surface, functional vs. stubbed split) → Task 4. §6 (what doesn't change) → honored throughout; no task touches `Kivi.Platform` or existing Groq HTTP/prompt/pipeline logic beyond the one `EnteringCooldown` event addition. §7 (deferred items) → explicitly re-stated in Task 5's ledger entry so they're not silently dropped.

**Placeholder scan:** no TBD/TODO markers. The one "temporary test harness" callout in Task 3c Step 4 is explicit about being temporary and reverted before commit — not a silent gap.

**Type consistency:** verified `IDictationOrchestrator`'s real shape (`event Action<RecordingState> StateChanged`, `void Start()`, `void Stop()` — no `StartAsync`/`LastResult`/`CopyLastResultAgainAsync`) against every task that consumes it (Tasks 3a, 3b, 3d, 4) and corrected all view-model code to match the real interface rather than the older impl-03 draft's aspirational one. Verified `ISecretStore.GetApiKey()/SetApiKey(string)` and `IAppConfigStore.Save(AppConfig)` against `Kivi.Core`'s real interfaces for Task 4's `SettingsViewModel`. `RecordingState.Waiting`/`.Done` (Task 1) are referenced identically in Tasks 1b, 1c, 3a, 3c, 3d — no naming drift.

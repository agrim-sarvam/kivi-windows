# Kivi Overlay Postures + Hey-Kivi Rewrite Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the overlay's 2-posture (orb/box) system with the real 4-posture design (rest pill / woken orb / dictating box / hey-kivi box) and add a working "hey kivi" voice-rewrite feature that edits Kivi's last dictation via a second hotkey, live partial transcription, a word diff, and an accept/reject step.

**Architecture:** `Kivi.Core` gains pure/testable pieces (a word-diff algorithm, a `RewriteAsync` prompt path reusing the already-ported `CommandModeSystem` prompt, two new `RecordingState` values, and `DictationOrchestrator` logic for a second hold-to-talk hotkey + live partial transcription). `Kivi.Platform` gains the OS-level plumbing those interfaces need (a second bound key + scoped Enter/Esc capture in the keyboard hook, an in-progress audio snapshot, an undo keystroke). `Kivi.App`'s `LayeredOrb` (Win32 layered window, GDI+) is rewritten to draw the four postures with real text content instead of today's graphic placeholders, reading state directly off `OverlayViewModel` every frame instead of via an external `SetState` push.

**Tech Stack:** .NET 8, WinUI 3 (Kivi.App), System.Drawing.Common/GDI+ (the layered orb), CsWin32 (low-level keyboard hook, SendInput), NAudio/WASAPI (Kivi.Platform), xUnit (Kivi.Core.Tests — the only project with an existing test harness).

## Global Constraints

- All colors/radii/spacing values must come from `ui/components/fig-tokens.css` / `Kivi.App/Themes/Tokens.xaml` — no hand-picked literals (spec §1).
- Rest pill: 39×15px. Woken orb: 61×61px, satellites 23px out from the orb edge. Dictating box: 322×108px, radius 20. Hey-kivi box: same shell, dynamic width up to 480px cap (spec §1).
- Live partial transcript: snapshot-and-retranscribe every 1.0s, skip the first snapshot until ≥0.5s of audio captured (spec §2).
- Rewrite hotkey default: Right Alt (`0xA5`). Configurable independently of the dictation hotkey (spec §3, §7).
- Rewrite targets only `DictationOrchestrator`'s own last-pasted text — no OS text-selection reading (spec §3).
- No new NuGet dependency for the diff algorithm (spec §4). Reuse `Prompts.CommandModeSystem` verbatim for the rewrite call — do not paraphrase it (spec §5, and the file's own existing header comment).
- Holding both hotkeys at once is unsupported: the second `HoldStarted` is ignored while a capture is already in progress (spec §6).
- Every task must leave `dotnet build` (whole solution) and `dotnet test Kivi.Core.Tests` green before moving to the next task — this repo's only automated test coverage lives in `Kivi.Core.Tests`; `Kivi.Platform` and `Kivi.App` changes have no existing test harness to extend (consistent with today's `LowLevelKeyboardHookService`/`WasapiAudioCaptureService`/`LayeredOrb`, none of which have unit tests) and are verified by a clean build plus the manual smoke test in Task 16.

---

### Task 1: Word-level diff (`WordDiff`)

**Files:**
- Create: `Kivi.Core/Text/WordDiff.cs`
- Test: `Kivi.Core.Tests/WordDiffTests.cs`

**Interfaces:**
- Produces: `Kivi.Core.Text.DiffOp` (enum: `Equal`, `Delete`, `Insert`), `Kivi.Core.Text.DiffToken` (readonly record struct: `DiffOp Op`, `string Text`), `Kivi.Core.Text.WordDiff.Compute(string original, string rewritten) : IReadOnlyList<DiffToken>`. Consumed later by `DictationOrchestrator` (Task 9) and `LayeredOrb` (Task 11).

- [ ] **Step 1: Write the failing tests**

```csharp
// Kivi.Core.Tests/WordDiffTests.cs
using Kivi.Core.Text;
using Xunit;

public class WordDiffTests
{
    [Fact]
    public void Compute_IdenticalText_AllEqual_AndReconstructs()
    {
        var diff = WordDiff.Compute("hello world", "hello world");
        Assert.All(diff, t => Assert.Equal(DiffOp.Equal, t.Op));
        Assert.Equal("hello world", string.Concat(diff.Select(t => t.Text)));
    }

    [Fact]
    public void Compute_ReconstructsOriginal_FromEqualAndDeleteTokens()
    {
        var diff = WordDiff.Compute("Kal 3 PM works fine", "Kal 3 PM works great");
        var original = string.Concat(diff.Where(t => t.Op != DiffOp.Insert).Select(t => t.Text));
        Assert.Equal("Kal 3 PM works fine", original);
    }

    [Fact]
    public void Compute_ReconstructsRewritten_FromEqualAndInsertTokens()
    {
        var diff = WordDiff.Compute("Kal 3 PM works fine", "Kal 3 PM works great");
        var rewritten = string.Concat(diff.Where(t => t.Op != DiffOp.Delete).Select(t => t.Text));
        Assert.Equal("Kal 3 PM works great", rewritten);
    }

    [Fact]
    public void Compute_PureInsertion_HasNoDeletes()
    {
        var diff = WordDiff.Compute("hello", "hello world");
        Assert.DoesNotContain(diff, t => t.Op == DiffOp.Delete);
        Assert.Contains(diff, t => t.Op == DiffOp.Insert && t.Text.Contains("world"));
    }

    [Fact]
    public void Compute_EmptyOriginal_HasNoDeletes()
    {
        var diff = WordDiff.Compute("", "new text");
        Assert.DoesNotContain(diff, t => t.Op == DiffOp.Delete);
    }

    [Fact]
    public void Compute_EmptyRewritten_HasNoInserts()
    {
        var diff = WordDiff.Compute("old text", "");
        Assert.DoesNotContain(diff, t => t.Op == DiffOp.Insert);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Kivi.Core.Tests --filter WordDiffTests`
Expected: FAIL to build — `Kivi.Core.Text` namespace / `WordDiff` type doesn't exist yet.

- [ ] **Step 3: Write the implementation**

```csharp
// Kivi.Core/Text/WordDiff.cs
using System.Text.RegularExpressions;

namespace Kivi.Core.Text;

public enum DiffOp { Equal, Delete, Insert }

public readonly record struct DiffToken(DiffOp Op, string Text);

/// <summary>
/// Word-level LCS diff for the "hey kivi" rewrite review UI: original vs. rewritten text,
/// tokenized on runs of whitespace/non-whitespace so each token already carries its own
/// spacing and the renderer can just concatenate runs directly.
/// </summary>
public static class WordDiff
{
    public static IReadOnlyList<DiffToken> Compute(string original, string rewritten)
    {
        var a = Tokenize(original);
        var b = Tokenize(rewritten);
        int n = a.Count, m = b.Count;

        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<DiffToken>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { result.Add(new DiffToken(DiffOp.Equal, a[x])); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { result.Add(new DiffToken(DiffOp.Delete, a[x])); x++; }
            else { result.Add(new DiffToken(DiffOp.Insert, b[y])); y++; }
        }
        while (x < n) { result.Add(new DiffToken(DiffOp.Delete, a[x])); x++; }
        while (y < m) { result.Add(new DiffToken(DiffOp.Insert, b[y])); y++; }
        return result;
    }

    private static List<string> Tokenize(string text) =>
        Regex.Matches(text, @"\S+|\s+").Select(m => m.Value).ToList();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Kivi.Core.Tests --filter WordDiffTests`
Expected: PASS (6 tests)

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Text/WordDiff.cs Kivi.Core.Tests/WordDiffTests.cs
git commit -m "feat(core): add word-level diff for hey-kivi rewrite review"
```

---

### Task 2: `RecordingState` additions + `AppConfig.RewriteHotkeyVirtualKeyCode`

**Files:**
- Modify: `Kivi.Core/Orchestration/RecordingState.cs`
- Modify: `Kivi.Core/Config/AppConfig.cs`
- Test: `Kivi.Core.Tests/AppConfigTests.cs`

**Interfaces:**
- Produces: `RecordingState.RewritePending`, `RecordingState.RewriteReview` (consumed by Tasks 9-11). `AppConfig.RewriteHotkeyVirtualKeyCode` (uint, default `0xA5`) (consumed by Tasks 9, 13, 15).

- [ ] **Step 1: Write the failing test**

Add to `Kivi.Core.Tests/AppConfigTests.cs` (append inside the `AppConfigTests` class, after the existing `Default_HasOnboardingAndOrbAndContextAndHotkeyDefaults` test):

```csharp
    [Fact]
    public void Default_HasRewriteHotkeyDefault()
    {
        var c = AppConfig.Default();
        Assert.Equal(0xA5u, c.RewriteHotkeyVirtualKeyCode);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Kivi.Core.Tests --filter Default_HasRewriteHotkeyDefault`
Expected: FAIL to build — `AppConfig` has no `RewriteHotkeyVirtualKeyCode` member yet.

- [ ] **Step 3: Implement**

In `Kivi.Core/Orchestration/RecordingState.cs`, replace the whole file:

```csharp
namespace Kivi.Core.Orchestration;
public enum RecordingState { Idle, Listening, Processing, Speaking, Waiting, Done, Error, RewritePending, RewriteReview }
```

In `Kivi.Core/Config/AppConfig.cs`, add a new property right after `HotkeyVirtualKeyCode`:

```csharp
    public uint HotkeyVirtualKeyCode { get; set; } = 0xA3; // VK_RCONTROL (Right Ctrl)
    public uint RewriteHotkeyVirtualKeyCode { get; set; } = 0xA5; // VK_RMENU (Right Alt) -- "hey kivi" rewrite
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Kivi.Core.Tests --filter AppConfigTests`
Expected: PASS (all `AppConfigTests`, including the new one)

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Orchestration/RecordingState.cs Kivi.Core/Config/AppConfig.cs Kivi.Core.Tests/AppConfigTests.cs
git commit -m "feat(core): add RewritePending/RewriteReview states and rewrite hotkey config"
```

---

### Task 3: `Prompts.CommandModeUserMessage`

**Files:**
- Modify: `Kivi.Core/Prompts/Prompts.cs`
- Test: `Kivi.Core.Tests/PromptsTests.cs`

**Interfaces:**
- Produces: `Prompts.CommandModeUserMessage(string selectedText, string voiceCommand) : string`. Consumed by `GroqPolishClient.RewriteAsync` (Task 4).
- Consumes: `Prompts.CommandModeSystem` (already exists, unchanged — `Kivi.Core/Prompts/Prompts.cs:92-111`).

- [ ] **Step 1: Write the failing tests**

Add to `Kivi.Core.Tests/PromptsTests.cs` (append inside the `PromptsTests` class):

```csharp
    [Fact]
    public void CommandModeSystem_HasHardContractForSelectedTextAndVoiceCommand()
    {
        Assert.Contains("SELECTED_TEXT as the only source material", Prompts.CommandModeSystem);
        Assert.Contains("VOICE_COMMAND as the user's instruction", Prompts.CommandModeSystem);
    }

    [Fact]
    public void CommandModeUserMessage_WrapsSelectedTextAndVoiceCommand()
    {
        var msg = Prompts.CommandModeUserMessage("Kal 3 PM works.", "make it formal");
        Assert.Contains("<<<SELECTED_TEXT", msg);
        Assert.Contains("Kal 3 PM works.", msg);
        Assert.Contains("<<<VOICE_COMMAND", msg);
        Assert.Contains("make it formal", msg);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Kivi.Core.Tests --filter CommandMode`
Expected: `CommandModeSystem_HasHardContractForSelectedTextAndVoiceCommand` passes already (the prompt exists); `CommandModeUserMessage_WrapsSelectedTextAndVoiceCommand` FAILS to build — the method doesn't exist yet.

- [ ] **Step 3: Implement**

In `Kivi.Core/Prompts/Prompts.cs`, add a new method right after `CleanupUserMessage` (after line 155, before `VocabularyAppend`):

```csharp
    /// <summary>
    /// User-message template for the hey-kivi rewrite call, paired with
    /// <see cref="CommandModeSystem"/>. Field names (SELECTED_TEXT, VOICE_COMMAND) match
    /// what that system prompt's contract already references.
    /// </summary>
    public static string CommandModeUserMessage(string selectedText, string voiceCommand) => $"""
Instructions: Transform SELECTED_TEXT according to VOICE_COMMAND and return only the replacement text.

SELECTED_TEXT:
<<<SELECTED_TEXT
{selectedText}
SELECTED_TEXT

VOICE_COMMAND:
<<<VOICE_COMMAND
{voiceCommand}
VOICE_COMMAND
""";
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Kivi.Core.Tests --filter CommandMode`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Prompts/Prompts.cs Kivi.Core.Tests/PromptsTests.cs
git commit -m "feat(core): add CommandModeUserMessage prompt template for hey-kivi rewrite"
```

---

### Task 4: `IPolishClient.RewriteAsync`

**Files:**
- Modify: `Kivi.Core/Polish/IPolishClient.cs`
- Modify: `Kivi.Core/Polish/GroqPolishClient.cs`
- Modify: `Kivi.Core.Tests/Fakes/Fakes.cs`
- Test: `Kivi.Core.Tests/GroqPolishClientTests.cs`

**Interfaces:**
- Produces: `IPolishClient.RewriteAsync(string selectedText, string voiceCommand, CancellationToken ct) : Task<string>`. Consumed by `DictationOrchestrator` (Task 9).
- Consumes: `Prompts.CommandModeSystem`, `Prompts.CommandModeUserMessage` (Task 3), `Prompts.VocabularyAppend` (existing).

- [ ] **Step 1: Write the failing tests**

Add to `Kivi.Core.Tests/GroqPolishClientTests.cs` (append inside the `GroqPolishClientTests` class):

```csharp
    [Fact]
    public async Task RewriteAsync_ReturnsRewrittenText()
    {
        var client = Client(Chat("Confirming tomorrow at 3 PM."));
        var result = await client.RewriteAsync("Kal 3 PM works.", "make it formal", default);
        Assert.Equal("Confirming tomorrow at 3 PM.", result);
    }

    [Fact]
    public async Task RewriteAsync_RateLimited_FallsBackToSecondModel()
    {
        var handler = new SequencedFakeHttpMessageHandler(
            ("{\"error\":\"rate limited\"}", HttpStatusCode.TooManyRequests),
            (Chat("Confirming tomorrow at 3 PM."), HttpStatusCode.OK));
        var client = SequencedClient(handler);

        var result = await client.RewriteAsync("Kal 3 PM works.", "make it formal", default);

        Assert.Equal("Confirming tomorrow at 3 PM.", result);
        Assert.Equal(2, handler.RequestBodies.Count);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Kivi.Core.Tests --filter RewriteAsync`
Expected: FAIL to build — `IPolishClient`/`GroqPolishClient` have no `RewriteAsync` yet.

- [ ] **Step 3: Implement**

In `Kivi.Core/Polish/IPolishClient.cs`, replace the whole file:

```csharp
namespace Kivi.Core.Polish;
public interface IPolishClient
{
    event Action<string>? EnteringCooldown;
    Task<string> CleanupAsync(string transcript, string context, CancellationToken ct);
    Task<string> RewriteAsync(string selectedText, string voiceCommand, CancellationToken ct);
}
```

In `Kivi.Core/Polish/GroqPolishClient.cs`, add a new public method right after `CleanupAsync` (after line 62, before the private `Models()` method), and a new private helper right after the existing `BuildSystemPrompt` method (after line 83):

```csharp
    public async Task<string> RewriteAsync(string selectedText, string voiceCommand, CancellationToken ct)
    {
        var key = _secrets.GetApiKey() ?? throw new InvalidOperationException("Missing API key");
        var system = BuildRewriteSystemPrompt();
        var user = Kivi.Core.Prompts.Prompts.CommandModeUserMessage(selectedText, voiceCommand);

        foreach (var model in Models())
        {
            if (InCooldown(model)) continue;
            try
            {
                var payload = BuildPayload(model, system, user);
                var body = await _http.PostChatCompletionAsync(_config.ChatBaseUrl, key, payload,
                    TimeSpan.FromSeconds(_config.TimeoutSeconds), ct);
                var content = ExtractContent(body);
                if (model == _config.FallbackModel) content = StripThinkTags(content);
                if (string.IsNullOrWhiteSpace(content)) continue; // truly blank output -> try fallback
                return Sanitize(content);
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                _cooldownUntil[model] = DateTimeOffset.UtcNow.AddSeconds(30);
                EnteringCooldown?.Invoke(model);
            }
        }
        return selectedText; // all models failed -> safe fallback to the unmodified text
    }
```

```csharp
    private string BuildRewriteSystemPrompt()
    {
        var s = Kivi.Core.Prompts.Prompts.CommandModeSystem;
        var vocab = string.Join(", ", _config.CustomVocabulary
            .Split(new[] { '\n', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct());
        if (vocab.Length > 0) s += "\n\n" + Kivi.Core.Prompts.Prompts.VocabularyAppend(vocab);
        return s;
    }
```

In `Kivi.Core.Tests/Fakes/Fakes.cs`, update `StubPolish` and `CooldownStubPolish` to implement the new interface member (replace both class bodies):

```csharp
public sealed class StubPolish : Kivi.Core.Polish.IPolishClient
{
    public event Action<string>? EnteringCooldown;
    public Task<string> CleanupAsync(string transcript, string context, CancellationToken ct)
        => Task.FromResult("Hello there.");
    public Task<string> RewriteAsync(string selectedText, string voiceCommand, CancellationToken ct)
        => Task.FromResult("Rewritten text.");
}

public sealed class CooldownStubPolish : Kivi.Core.Polish.IPolishClient
{
    public event Action<string>? EnteringCooldown;
    public async Task<string> CleanupAsync(string transcript, string context, CancellationToken ct)
    {
        EnteringCooldown?.Invoke("primary-model");
        await Task.Delay(10, ct);
        return "Hello there.";
    }
    public Task<string> RewriteAsync(string selectedText, string voiceCommand, CancellationToken ct)
        => Task.FromResult(selectedText);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Kivi.Core.Tests`
Expected: PASS — the full suite (this touches a shared fake, so run the whole project, not just the filter)

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Polish/IPolishClient.cs Kivi.Core/Polish/GroqPolishClient.cs Kivi.Core.Tests/Fakes/Fakes.cs Kivi.Core.Tests/GroqPolishClientTests.cs
git commit -m "feat(core): add IPolishClient.RewriteAsync using the CommandMode prompt"
```

---

### Task 5: `IPasteService.UndoAsync`

**Files:**
- Modify: `Kivi.Core/Abstractions/IPasteService.cs`
- Modify: `Kivi.Platform/Paste/SendInputPasteService.cs`
- Modify: `Kivi.Core.Tests/Fakes/Fakes.cs`

**Interfaces:**
- Produces: `IPasteService.UndoAsync() : Task`. Consumed by `DictationOrchestrator` (Task 9).

No dedicated test: `Kivi.Platform` has no test project (confirmed — `Kivi.Core.Tests.csproj` only references `Kivi.Core.csproj`), matching the existing untested state of `SendInputPasteService.InjectTextAsync`. Verified by a clean solution build here and exercised indirectly by `DictationOrchestrator` tests (Task 9) via the `SpyPaste` fake, plus the Task 16 manual smoke test.

- [ ] **Step 1: Update the interface**

In `Kivi.Core/Abstractions/IPasteService.cs`, replace the whole file:

```csharp
namespace Kivi.Core.Abstractions;

public interface IPasteService
{
    Task InjectTextAsync(string text, bool pressEnter);
    Task UndoAsync();
}
```

- [ ] **Step 2: Implement in `SendInputPasteService`**

In `Kivi.Platform/Paste/SendInputPasteService.cs`, add a new constant next to the existing `VK_*` constants (after line 33):

```csharp
    private const ushort VK_Z = 0x5A;
```

Add a new public method right after `InjectTextAsync` (after line 65, before `WaitForModifiersReleasedAsync`):

```csharp
    /// <summary>
    /// Sends Ctrl+Z to undo the last edit in the focused app -- used by the hey-kivi
    /// rewrite flow to undo the single-paste insertion InjectTextAsync made for the
    /// original dictation before pasting the rewritten replacement.
    /// </summary>
    public async Task UndoAsync()
    {
        await WaitForModifiersReleasedAsync();
        SendKeyDown(VK_CONTROL);
        SendKeyDown(VK_Z);
        SendKeyUp(VK_Z);
        SendKeyUp(VK_CONTROL);
    }
```

- [ ] **Step 3: Update the fake**

In `Kivi.Core.Tests/Fakes/Fakes.cs`, replace the `SpyPaste` class body:

```csharp
public sealed class SpyPaste : IPasteService
{
    public string? Pasted; public bool PressedEnter; public int UndoCalls;
    public Task InjectTextAsync(string text, bool pressEnter) { Pasted = text; PressedEnter = pressEnter; return Task.CompletedTask; }
    public Task UndoAsync() { UndoCalls++; return Task.CompletedTask; }
}
```

- [ ] **Step 4: Verify the solution still builds and existing tests pass**

Run: `dotnet build Kivi.sln`
Expected: Build succeeded (0 errors)

Run: `dotnet test Kivi.Core.Tests`
Expected: PASS — full existing suite unaffected

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Abstractions/IPasteService.cs Kivi.Platform/Paste/SendInputPasteService.cs Kivi.Core.Tests/Fakes/Fakes.cs
git commit -m "feat(platform): add IPasteService.UndoAsync for hey-kivi replace step"
```

---

### Task 6: `IAudioCaptureService.SnapshotRecording`

**Files:**
- Modify: `Kivi.Core/Abstractions/IAudioCaptureService.cs`
- Modify: `Kivi.Platform/Audio/WasapiAudioCaptureService.cs`
- Modify: `Kivi.Core.Tests/Fakes/Fakes.cs`

**Interfaces:**
- Produces: `IAudioCaptureService.SnapshotRecording() : byte[]` — a valid, decodable WAV of everything captured so far, without stopping capture. Consumed by `DictationOrchestrator`'s partial-transcript loop (Task 8).

No dedicated test, same reasoning as Task 5 (`Kivi.Platform` has no test project). Verified by build + Task 8's orchestrator tests (via the `FakeAudio` fake) + Task 16's manual smoke test.

- [ ] **Step 1: Update the interface**

In `Kivi.Core/Abstractions/IAudioCaptureService.cs`, replace the whole file:

```csharp
namespace Kivi.Core.Abstractions;

// Returns 16k mono PCM16 WAV bytes.
public interface IAudioCaptureService
{
    Task StartRecordingAsync(CancellationToken ct);
    Task<byte[]> StopRecordingAsync();
    // Returns a valid WAV of everything captured so far WITHOUT stopping capture -- used to
    // drive live partial transcription while a recording is in progress. Empty array if no
    // recording is in progress.
    byte[] SnapshotRecording();
    event Action<string>? DeviceChanged;
}
```

- [ ] **Step 2: Implement in `WasapiAudioCaptureService`**

In `Kivi.Platform/Audio/WasapiAudioCaptureService.cs`, add a new public method right after `StopRecordingAsync` (after line 107, before `OnData`):

```csharp
    public byte[] SnapshotRecording()
    {
        lock (_gate)
        {
            if (_writer is null || _stream is null) return Array.Empty<byte>();
            // WaveFileWriter patches the RIFF header sizes in place on Flush() (same as it
            // does on Dispose()), so the stream bytes are a valid, decodable WAV right after
            // this call -- capture keeps accumulating into the same _writer/_stream afterward.
            _writer.Flush();
            return _stream.ToArray();
        }
    }
```

- [ ] **Step 3: Update the fake**

In `Kivi.Core.Tests/Fakes/Fakes.cs`, replace the `FakeAudio` class body:

```csharp
public sealed class FakeAudio : IAudioCaptureService
{
    public event Action<string>? DeviceChanged;
    public byte[] Wav = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
    public Task StartRecordingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<byte[]> StopRecordingAsync() => Task.FromResult(Wav);
    public byte[] SnapshotRecording() => Wav;
}
```

- [ ] **Step 4: Verify the solution still builds and existing tests pass**

Run: `dotnet build Kivi.sln`
Expected: Build succeeded (0 errors)

Run: `dotnet test Kivi.Core.Tests`
Expected: PASS — full existing suite unaffected

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Abstractions/IAudioCaptureService.cs Kivi.Platform/Audio/WasapiAudioCaptureService.cs Kivi.Core.Tests/Fakes/Fakes.cs
git commit -m "feat(platform): add IAudioCaptureService.SnapshotRecording for live partials"
```

---

### Task 7: `IHotkeyService` second channel + scoped review-key capture

**Files:**
- Modify: `Kivi.Core/Abstractions/IHotkeyService.cs`
- Modify: `Kivi.Platform/Hotkey/LowLevelKeyboardHookService.cs`
- Modify: `Kivi.Core.Tests/Fakes/Fakes.cs`

**Interfaces:**
- Produces: `IHotkeyService.RewriteHoldStarted`/`RewriteHoldEnded` events, `SetRewriteHotkey(uint)`, `ArmReviewKeys()`/`DisarmReviewKeys()`, `ReviewAccepted`/`ReviewCancelled` events. Consumed by `DictationOrchestrator` (Tasks 8-9).

No dedicated test, same reasoning as Task 5/6 (`Kivi.Platform` has no test project; a real `WH_KEYBOARD_LL` hook needs live OS key injection, which the existing `SetHotkey`/hold-detection code also has zero test coverage for). Verified by build + Task 8-9's orchestrator tests (via the `FakeHotkey` fake) + Task 16's manual smoke test.

- [ ] **Step 1: Update the interface**

In `Kivi.Core/Abstractions/IHotkeyService.cs`, replace the whole file:

```csharp
namespace Kivi.Core.Abstractions;

public interface IHotkeyService
{
    event Action? HoldStarted;
    event Action? HoldEnded;
    event Action? RewriteHoldStarted;
    event Action? RewriteHoldEnded;
    // Fire only while ArmReviewKeys() is active -- the hey-kivi accept/reject step.
    event Action? ReviewAccepted;
    event Action? ReviewCancelled;
    void Start();
    void Stop();
    void SetHotkey(uint virtualKeyCode);
    void SetRewriteHotkey(uint virtualKeyCode);
    void ArmReviewKeys();
    void DisarmReviewKeys();
}
```

- [ ] **Step 2: Implement in `LowLevelKeyboardHookService`**

Replace the whole file `Kivi.Platform/Hotkey/LowLevelKeyboardHookService.cs`:

```csharp
using System.Runtime.InteropServices;
using Kivi.Core.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Kivi.Platform.Hotkey;

public sealed class LowLevelKeyboardHookService : IHotkeyService, IDisposable
{
    private uint _boundVk = 0xA3;    // VK_RCONTROL default; changeable via SetHotkey
    private uint _rewriteVk = 0xA5;  // VK_RMENU default; changeable via SetRewriteHotkey
    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYUP = 0x0105;
    private const uint VK_RETURN = 0x0D, VK_ESCAPE = 0x1B;

    public event Action? HoldStarted;
    public event Action? HoldEnded;
    public event Action? RewriteHoldStarted;
    public event Action? RewriteHoldEnded;
    public event Action? ReviewAccepted;
    public event Action? ReviewCancelled;

    private HOOKPROC? _proc;   // keep alive to avoid GC of the delegate while the hook is installed
    private UnhookWindowsHookExSafeHandle? _hook;
    private bool _held;
    private bool _rewriteHeld;
    private volatile bool _reviewArmed;

    public unsafe void Start()
    {
        _proc = HookCallback;
        _hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _proc,
            PInvoke.GetModuleHandle((string?)null), 0);
    }

    public void Stop()
    {
        _hook?.Dispose();
        _hook = null;
    }

    public void Dispose() => Stop();

    public void SetHotkey(uint virtualKeyCode)
    {
        _boundVk = virtualKeyCode;
        // If a hold was in progress on the old key, clear it so state doesn't stick.
        if (_held) { _held = false; HoldEnded?.Invoke(); }
    }

    public void SetRewriteHotkey(uint virtualKeyCode)
    {
        _rewriteVk = virtualKeyCode;
        if (_rewriteHeld) { _rewriteHeld = false; RewriteHoldEnded?.Invoke(); }
    }

    public void ArmReviewKeys() => _reviewArmed = true;
    public void DisarmReviewKeys() => _reviewArmed = false;

    private LRESULT HookCallback(int nCode, WPARAM wParam, LPARAM lParam)
    {
        if (nCode >= 0)
        {
            var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            uint msg = (uint)wParam.Value;
            bool isDown = msg == WM_KEYDOWN;
            bool isUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;

            if (data.vkCode == _boundVk)
            {
                if (isDown && !_held) { _held = true; HoldStarted?.Invoke(); }
                else if (isUp && _held) { _held = false; HoldEnded?.Invoke(); }
            }
            else if (data.vkCode == _rewriteVk)
            {
                if (isDown && !_rewriteHeld) { _rewriteHeld = true; RewriteHoldStarted?.Invoke(); }
                else if (isUp && _rewriteHeld) { _rewriteHeld = false; RewriteHoldEnded?.Invoke(); }
            }
            else if (_reviewArmed && isDown && data.vkCode == VK_RETURN)
            {
                _reviewArmed = false;
                ReviewAccepted?.Invoke();
                return new LRESULT(1); // swallow: confirms the rewrite, not a newline in the target app
            }
            else if (_reviewArmed && isDown && data.vkCode == VK_ESCAPE)
            {
                _reviewArmed = false;
                ReviewCancelled?.Invoke();
                return new LRESULT(1); // swallow: discards the rewrite, not an app-level cancel
            }
        }

        return PInvoke.CallNextHookEx(_hook, nCode, wParam, lParam); // non-suppressing (except the two swallowed cases above)
    }
}
```

- [ ] **Step 3: Update the fake**

In `Kivi.Core.Tests/Fakes/Fakes.cs`, replace the `FakeHotkey` class body:

```csharp
public sealed class FakeHotkey : IHotkeyService
{
    public event Action? HoldStarted;
    public event Action? HoldEnded;
    public event Action? RewriteHoldStarted;
    public event Action? RewriteHoldEnded;
    public event Action? ReviewAccepted;
    public event Action? ReviewCancelled;
    public bool ReviewArmed { get; private set; }

    public void Start() { } public void Stop() { }
    public void SetHotkey(uint virtualKeyCode) { }
    public void SetRewriteHotkey(uint virtualKeyCode) { }
    public void ArmReviewKeys() => ReviewArmed = true;
    public void DisarmReviewKeys() => ReviewArmed = false;

    public void FireStart() => HoldStarted?.Invoke();
    public void FireEnd() => HoldEnded?.Invoke();
    public void FireRewriteStart() => RewriteHoldStarted?.Invoke();
    public void FireRewriteEnd() => RewriteHoldEnded?.Invoke();
    public void FireReviewAccepted() => ReviewAccepted?.Invoke();
    public void FireReviewCancelled() => ReviewCancelled?.Invoke();
}
```

- [ ] **Step 4: Verify the solution still builds and existing tests pass**

Run: `dotnet build Kivi.sln`
Expected: Build succeeded (0 errors)

Run: `dotnet test Kivi.Core.Tests`
Expected: PASS — full existing suite unaffected

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Abstractions/IHotkeyService.cs Kivi.Platform/Hotkey/LowLevelKeyboardHookService.cs Kivi.Core.Tests/Fakes/Fakes.cs
git commit -m "feat(platform): add second hotkey channel + scoped Enter/Esc review capture"
```

---

### Task 8: `DictationOrchestrator` — last-dictated tracking + live partial transcript

**Files:**
- Modify: `Kivi.Core/Orchestration/IDictationOrchestrator.cs`
- Modify: `Kivi.Core/Orchestration/DictationOrchestrator.cs`
- Test: `Kivi.Core.Tests/OrchestratorTests.cs`

**Interfaces:**
- Produces: `IDictationOrchestrator.PartialTranscriptChanged` event (`Action<string>`). Consumed by `OverlayViewModel` (Task 10).
- Consumes: `IAudioCaptureService.SnapshotRecording()` (Task 6).

This task deliberately does NOT touch the rewrite hotkey yet — `IHotkeyService.RewriteHoldStarted`/`RewriteHoldEnded` exist (Task 7) but `DictationOrchestrator` doesn't subscribe to them until Task 9. This task only adds: (a) tracking the exact text last pasted, and (b) the live-partial-transcript loop for the *existing* dictation hotkey.

- [ ] **Step 1: Write the failing test**

Add to `Kivi.Core.Tests/OrchestratorTests.cs` (append inside the `OrchestratorTests` class):

```csharp
    [Fact]
    public async Task Listening_EmitsPartialTranscript_AfterWarmup()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var stt = new StubStt { Result = "partial words" };
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            stt, new StubPolish(), paste, AppConfig.Default(), metrics);

        var partials = new List<string>();
        orch.PartialTranscriptChanged += p => partials.Add(p);
        orch.Start();

        hotkey.FireStart();
        await Task.Delay(700); // past the 500ms warmup -> at least one snapshot should fire
        hotkey.FireEnd();
        await Task.Delay(1500);

        Assert.Contains("partial words", partials);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test Kivi.Core.Tests --filter Listening_EmitsPartialTranscript_AfterWarmup`
Expected: FAIL to build — `IDictationOrchestrator`/`DictationOrchestrator` have no `PartialTranscriptChanged` yet.

- [ ] **Step 3: Implement**

In `Kivi.Core/Orchestration/IDictationOrchestrator.cs`, replace the whole file:

```csharp
namespace Kivi.Core.Orchestration;
public interface IDictationOrchestrator
{
    RecordingState State { get; }
    event Action<RecordingState> StateChanged;
    event Action<string> PartialTranscriptChanged;
    void Start();
    void Stop();
}
```

In `Kivi.Core/Orchestration/DictationOrchestrator.cs`, replace the whole file:

```csharp
using System.Diagnostics;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Diagnostics;
using Kivi.Core.Macros;
using Kivi.Core.Polish;
using Kivi.Core.Stt;

namespace Kivi.Core.Orchestration;

public sealed class DictationOrchestrator : IDictationOrchestrator
{
    private readonly IHotkeyService _hotkey;
    private readonly IAudioCaptureService _audio;
    private readonly IScreenContextProvider _context;
    private readonly ISttEngine _stt;
    private readonly IPolishClient _polish;
    private readonly IPasteService _paste;
    private readonly AppConfig _config;
    private readonly KiviMetrics _metrics;
    private readonly object _lock = new();
    private const int DoneDisplayMs = 1200;
    private const int PartialIntervalMs = 1000;
    private const int PartialWarmupMs = 500;

    private Task<string> _contextTask = Task.FromResult("");
    private CancellationTokenSource _cts = new();
    private CancellationTokenSource _partialLoopCts = new();
    private string _lastDictatedText = "";

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public event Action<RecordingState>? StateChanged;
    public event Action<string>? PartialTranscriptChanged;

    public DictationOrchestrator(IHotkeyService hotkey, IAudioCaptureService audio, IScreenContextProvider context,
        ISttEngine stt, IPolishClient polish, IPasteService paste, AppConfig config, KiviMetrics metrics)
    {
        (_hotkey, _audio, _context, _stt, _polish, _paste, _config, _metrics)
           = (hotkey, audio, context, stt, polish, paste, config, metrics);
        _polish.EnteringCooldown += _ => SetState(RecordingState.Waiting);
    }

    public void Start()
    {
        _hotkey.HoldStarted += OnHoldStarted;
        _hotkey.HoldEnded += OnHoldEnded;
        _hotkey.Start();
    }

    public void Stop()
    {
        _hotkey.HoldStarted -= OnHoldStarted;
        _hotkey.HoldEnded -= OnHoldEnded;
        _hotkey.Stop();
    }

    private void SetState(RecordingState s)
    {
        lock (_lock) { State = s; }
        StateChanged?.Invoke(s);
    }

    private void OnHoldStarted()
    {
        _cts = new CancellationTokenSource();
        _partialLoopCts = new CancellationTokenSource();
        SetState(RecordingState.Listening);
        _contextTask = _config.ScreenContextEnabled
            ? _context.CaptureContextAsync(_cts.Token)
            : Task.FromResult("");
        _ = _audio.StartRecordingAsync(_cts.Token);
        _ = RunPartialLoopAsync(_partialLoopCts.Token);
    }

    private async Task RunPartialLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(PartialWarmupMs, ct);
            while (!ct.IsCancellationRequested)
            {
                var wav = _audio.SnapshotRecording();
                if (wav.Length > 0)
                {
                    var partial = await _stt.TranscribeAsync(wav, ct);
                    if (!string.IsNullOrEmpty(partial))
                        PartialTranscriptChanged?.Invoke(partial);
                }
                await Task.Delay(PartialIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { /* recording ended -> stop snapshotting */ }
    }

    private void OnHoldEnded()
    {
        _partialLoopCts.Cancel();
        _ = RunPipelineAsync();
    }

    private async Task RunPipelineAsync()
    {
        var total = Stopwatch.StartNew();
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
            _lastDictatedText = textToPaste;

            SetState(RecordingState.Done);
            await Task.Delay(DoneDisplayMs, _cts.Token);
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
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test Kivi.Core.Tests`
Expected: PASS — full suite, including the new partial-transcript test (this test takes ~2.2s of real wall-clock delay, matching the existing tests' pattern of real `Task.Delay` waits)

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Orchestration/IDictationOrchestrator.cs Kivi.Core/Orchestration/DictationOrchestrator.cs Kivi.Core.Tests/OrchestratorTests.cs
git commit -m "feat(core): track last-dictated text and emit live partial transcripts"
```

---

### Task 9: `DictationOrchestrator` — hey-kivi rewrite flow

**Files:**
- Modify: `Kivi.Core/Orchestration/IDictationOrchestrator.cs`
- Modify: `Kivi.Core/Orchestration/DictationOrchestrator.cs`
- Test: `Kivi.Core.Tests/OrchestratorTests.cs`

**Interfaces:**
- Produces: `IDictationOrchestrator.IsRewriteCapture`, `Instruction`, `LastErrorMessage`, `Diff` properties. Consumed by `OverlayViewModel` (Task 10).
- Consumes: `IHotkeyService.RewriteHoldStarted/Ended`, `ReviewAccepted/Cancelled`, `ArmReviewKeys/DisarmReviewKeys` (Task 7); `IPolishClient.RewriteAsync` (Task 4); `IPasteService.UndoAsync` (Task 5); `Kivi.Core.Text.WordDiff.Compute`/`DiffToken` (Task 1); `RecordingState.RewritePending/RewriteReview` (Task 2).

- [ ] **Step 1: Write the failing tests**

Add to `Kivi.Core.Tests/OrchestratorTests.cs` (append inside the `OrchestratorTests` class):

```csharp
    [Fact]
    public async Task RewriteHotkey_WithNoPriorDictation_ShowsErrorAndReturnsToIdle()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics);

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        hotkey.FireRewriteStart();
        await Task.Delay(20);
        hotkey.FireRewriteEnd();
        await Task.Delay(1500);

        Assert.Contains(RecordingState.Error, states);
        Assert.Equal(RecordingState.Idle, orch.State);
        Assert.Null(paste.Pasted); // nothing was ever pasted
    }

    [Fact]
    public async Task RewriteHotkey_AfterDictation_ComputesDiff_AndArmsReviewKeys()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var polish = new StubPolish(); // RewriteAsync -> "Rewritten text."
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), polish, paste, AppConfig.Default(), metrics);

        var states = new List<RecordingState>();
        orch.StateChanged += s => states.Add(s);
        orch.Start();

        // First, a normal dictation so there's something to rewrite.
        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);
        Assert.Equal("Hello there.", paste.Pasted);

        // Now hold the rewrite hotkey.
        hotkey.FireRewriteStart();
        await Task.Delay(20);
        hotkey.FireRewriteEnd();
        await Task.Delay(500);

        Assert.Contains(RecordingState.RewritePending, states);
        Assert.Equal(RecordingState.RewriteReview, orch.State);
        Assert.True(hotkey.ReviewArmed);
        Assert.NotNull(orch.Diff);
        Assert.Equal("Rewritten text.", string.Concat(orch.Diff!.Where(t => t.Op != Kivi.Core.Text.DiffOp.Delete).Select(t => t.Text)));
    }

    [Fact]
    public async Task ReviewAccepted_UndoesThenPastesRewrittenText()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics);
        orch.Start();

        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);
        hotkey.FireRewriteStart(); await Task.Delay(20); hotkey.FireRewriteEnd(); await Task.Delay(500);

        hotkey.FireReviewAccepted();
        await Task.Delay(1500);

        Assert.Equal(1, paste.UndoCalls);
        Assert.Equal("Rewritten text.", paste.Pasted);
        Assert.Equal(RecordingState.Idle, orch.State);
        Assert.False(hotkey.ReviewArmed);
    }

    [Fact]
    public async Task ReviewCancelled_LeavesOriginalPasteUntouched()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics);
        orch.Start();

        hotkey.FireStart(); await Task.Delay(20); hotkey.FireEnd(); await Task.Delay(1500);
        hotkey.FireRewriteStart(); await Task.Delay(20); hotkey.FireRewriteEnd(); await Task.Delay(500);

        hotkey.FireReviewCancelled();

        Assert.Equal(0, paste.UndoCalls);
        Assert.Equal("Hello there.", paste.Pasted); // still the original dictation, never overwritten
        Assert.Equal(RecordingState.Idle, orch.State);
        Assert.False(hotkey.ReviewArmed);
    }

    [Fact]
    public async Task BothHotkeysHeldAtOnce_SecondHoldIsIgnored()
    {
        var hotkey = new FakeHotkey();
        var paste = new SpyPaste();
        using var metrics = new KiviMetrics();
        var orch = new DictationOrchestrator(hotkey, new FakeAudio(), new FakeContext(),
            new StubStt(), new StubPolish(), paste, AppConfig.Default(), metrics);
        orch.Start();

        hotkey.FireStart();
        hotkey.FireRewriteStart(); // ignored: a capture is already in progress
        await Task.Delay(20);
        hotkey.FireEnd();
        await Task.Delay(1500);

        Assert.Equal("Hello there.", paste.Pasted); // normal dictation completed; rewrite never ran
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test Kivi.Core.Tests --filter "RewriteHotkey|ReviewAccepted|ReviewCancelled|BothHotkeysHeldAtOnce"`
Expected: FAIL to build — none of `IsRewriteCapture`/`Diff`/`FireRewriteStart` etc. exist on the orchestrator yet.

- [ ] **Step 3: Implement**

In `Kivi.Core/Orchestration/IDictationOrchestrator.cs`, replace the whole file:

```csharp
using Kivi.Core.Text;

namespace Kivi.Core.Orchestration;

public interface IDictationOrchestrator
{
    RecordingState State { get; }
    bool IsRewriteCapture { get; }
    string? Instruction { get; }
    string? LastErrorMessage { get; }
    IReadOnlyList<DiffToken>? Diff { get; }
    event Action<RecordingState> StateChanged;
    event Action<string> PartialTranscriptChanged;
    void Start();
    void Stop();
}
```

In `Kivi.Core/Orchestration/DictationOrchestrator.cs`, replace the whole file:

```csharp
using System.Diagnostics;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;
using Kivi.Core.Diagnostics;
using Kivi.Core.Macros;
using Kivi.Core.Polish;
using Kivi.Core.Stt;
using Kivi.Core.Text;

namespace Kivi.Core.Orchestration;

public sealed class DictationOrchestrator : IDictationOrchestrator
{
    private readonly IHotkeyService _hotkey;
    private readonly IAudioCaptureService _audio;
    private readonly IScreenContextProvider _context;
    private readonly ISttEngine _stt;
    private readonly IPolishClient _polish;
    private readonly IPasteService _paste;
    private readonly AppConfig _config;
    private readonly KiviMetrics _metrics;
    private readonly object _lock = new();
    private const int DoneDisplayMs = 1200;
    private const int PartialIntervalMs = 1000;
    private const int PartialWarmupMs = 500;

    private Task<string> _contextTask = Task.FromResult("");
    private CancellationTokenSource _cts = new();
    private CancellationTokenSource _partialLoopCts = new();
    private bool _capturing;
    private string _lastDictatedText = "";
    private string _pendingRewrite = "";

    public RecordingState State { get; private set; } = RecordingState.Idle;
    public bool IsRewriteCapture { get; private set; }
    public string? Instruction { get; private set; }
    public string? LastErrorMessage { get; private set; }
    public IReadOnlyList<DiffToken>? Diff { get; private set; }

    public event Action<RecordingState>? StateChanged;
    public event Action<string>? PartialTranscriptChanged;

    public DictationOrchestrator(IHotkeyService hotkey, IAudioCaptureService audio, IScreenContextProvider context,
        ISttEngine stt, IPolishClient polish, IPasteService paste, AppConfig config, KiviMetrics metrics)
    {
        (_hotkey, _audio, _context, _stt, _polish, _paste, _config, _metrics)
           = (hotkey, audio, context, stt, polish, paste, config, metrics);
        _polish.EnteringCooldown += _ => SetState(RecordingState.Waiting);
    }

    public void Start()
    {
        _hotkey.HoldStarted += OnHoldStarted;
        _hotkey.HoldEnded += OnHoldEnded;
        _hotkey.RewriteHoldStarted += OnRewriteHoldStarted;
        _hotkey.RewriteHoldEnded += OnRewriteHoldEnded;
        _hotkey.ReviewAccepted += OnReviewAccepted;
        _hotkey.ReviewCancelled += OnReviewCancelled;
        _hotkey.Start();
    }

    public void Stop()
    {
        _hotkey.HoldStarted -= OnHoldStarted;
        _hotkey.HoldEnded -= OnHoldEnded;
        _hotkey.RewriteHoldStarted -= OnRewriteHoldStarted;
        _hotkey.RewriteHoldEnded -= OnRewriteHoldEnded;
        _hotkey.ReviewAccepted -= OnReviewAccepted;
        _hotkey.ReviewCancelled -= OnReviewCancelled;
        _hotkey.Stop();
    }

    private void SetState(RecordingState s)
    {
        lock (_lock) { State = s; }
        StateChanged?.Invoke(s);
    }

    private void OnHoldStarted()
    {
        if (_capturing) return; // both hotkeys held at once is unsupported -- ignore the second
        _capturing = true;
        IsRewriteCapture = false;
        StartCaptureCommon();
        _contextTask = _config.ScreenContextEnabled
            ? _context.CaptureContextAsync(_cts.Token)
            : Task.FromResult("");
    }

    private void OnRewriteHoldStarted()
    {
        if (_capturing) return;
        _capturing = true;
        IsRewriteCapture = true;
        StartCaptureCommon();
    }

    private void StartCaptureCommon()
    {
        _cts = new CancellationTokenSource();
        _partialLoopCts = new CancellationTokenSource();
        SetState(RecordingState.Listening);
        _ = _audio.StartRecordingAsync(_cts.Token);
        _ = RunPartialLoopAsync(_partialLoopCts.Token);
    }

    private async Task RunPartialLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(PartialWarmupMs, ct);
            while (!ct.IsCancellationRequested)
            {
                var wav = _audio.SnapshotRecording();
                if (wav.Length > 0)
                {
                    var partial = await _stt.TranscribeAsync(wav, ct);
                    if (!string.IsNullOrEmpty(partial))
                        PartialTranscriptChanged?.Invoke(partial);
                }
                await Task.Delay(PartialIntervalMs, ct);
            }
        }
        catch (OperationCanceledException) { /* recording ended -> stop snapshotting */ }
    }

    private void OnHoldEnded()
    {
        if (!_capturing || IsRewriteCapture) return;
        _capturing = false;
        _partialLoopCts.Cancel();
        _ = RunPipelineAsync();
    }

    private void OnRewriteHoldEnded()
    {
        if (!_capturing || !IsRewriteCapture) return;
        _capturing = false;
        _partialLoopCts.Cancel();
        _ = RunRewritePipelineAsync();
    }

    private async Task RunPipelineAsync()
    {
        var total = Stopwatch.StartNew();
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
            _lastDictatedText = textToPaste;

            SetState(RecordingState.Done);
            await Task.Delay(DoneDisplayMs, _cts.Token);
            SetState(RecordingState.Idle);
        }
        catch
        {
            LastErrorMessage = "Couldn't catch that.";
            SetState(RecordingState.Error);
            SetState(RecordingState.Idle);
        }
        finally
        {
            _metrics.RecordTotal(total.Elapsed.TotalMilliseconds);
        }
    }

    private async Task RunRewritePipelineAsync()
    {
        try
        {
            SetState(RecordingState.RewritePending);
            var wav = await _audio.StopRecordingAsync();
            var instructionRaw = await _stt.TranscribeAsync(wav, _cts.Token);
            if (string.IsNullOrEmpty(instructionRaw)) { IsRewriteCapture = false; SetState(RecordingState.Idle); return; }
            var instruction = instructionRaw.Trim();
            Instruction = instruction;

            if (string.IsNullOrEmpty(_lastDictatedText))
            {
                await FailRewriteAsync("Nothing to rewrite yet.");
                return;
            }

            var rewritten = await _polish.RewriteAsync(_lastDictatedText, instruction, _cts.Token);
            Diff = WordDiff.Compute(_lastDictatedText, rewritten);
            _pendingRewrite = rewritten;
            SetState(RecordingState.RewriteReview);
            _hotkey.ArmReviewKeys();
        }
        catch
        {
            await FailRewriteAsync("Couldn't catch that.");
        }
    }

    private async Task FailRewriteAsync(string message)
    {
        LastErrorMessage = message;
        SetState(RecordingState.Error);
        await Task.Delay(DoneDisplayMs, CancellationToken.None);
        IsRewriteCapture = false;
        SetState(RecordingState.Idle);
    }

    private void OnReviewAccepted()
    {
        _hotkey.DisarmReviewKeys();
        _ = ApplyAcceptedRewriteAsync();
    }

    private async Task ApplyAcceptedRewriteAsync()
    {
        try
        {
            await _paste.UndoAsync();
            await _paste.InjectTextAsync(_pendingRewrite, false);
            _lastDictatedText = _pendingRewrite;
            SetState(RecordingState.Done);
            await Task.Delay(DoneDisplayMs);
        }
        catch
        {
            LastErrorMessage = "Couldn't catch that.";
            SetState(RecordingState.Error);
            await Task.Delay(DoneDisplayMs);
        }
        finally
        {
            IsRewriteCapture = false;
            SetState(RecordingState.Idle);
        }
    }

    private void OnReviewCancelled()
    {
        _hotkey.DisarmReviewKeys();
        IsRewriteCapture = false;
        SetState(RecordingState.Idle);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test Kivi.Core.Tests`
Expected: PASS — full suite (this rewrites the file touched by Task 8, so re-run everything, not just the new filter)

- [ ] **Step 5: Commit**

```bash
git add Kivi.Core/Orchestration/IDictationOrchestrator.cs Kivi.Core/Orchestration/DictationOrchestrator.cs Kivi.Core.Tests/OrchestratorTests.cs
git commit -m "feat(core): implement hey-kivi rewrite flow (diff, review, accept/reject)"
```

---

### Task 10: `OverlayViewModel` — expose rewrite/partial-transcript state

**Files:**
- Modify: `Kivi.App/ViewModels/OverlayViewModel.cs`

**Interfaces:**
- Consumes: `IDictationOrchestrator.IsRewriteCapture/Instruction/LastErrorMessage/Diff/PartialTranscriptChanged` (Tasks 8-9).
- Produces: `OverlayViewModel.PartialTranscript`, `IsRewriteCapture`, `IsRewritePending`, `IsRewriteReview`, `Instruction`, `LastErrorMessage`, `Diff` — all consumed directly by `LayeredOrb` (Task 11).

No dedicated test: this project has no ViewModel test coverage today (`Kivi.Core.Tests` only references `Kivi.Core`, not `Kivi.App`). Verified by build + Task 16's manual smoke test.

- [ ] **Step 1: Implement**

Replace the whole file `Kivi.App/ViewModels/OverlayViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using Kivi.Core.Orchestration;
using Kivi.Core.Text;
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
        _orch.StateChanged += OnOrchestratorStateChanged;
        _orch.PartialTranscriptChanged += OnPartialTranscriptChanged;
        Apply(_orch.State);
    }

    [ObservableProperty] private RecordingState _state;
    [ObservableProperty] private string _partialTranscript = "";

    public bool IsVisible    => true;
    public bool IsListening  => State == RecordingState.Listening;
    public bool IsProcessing => State == RecordingState.Processing;
    public bool IsSpeaking   => State == RecordingState.Speaking;
    public bool IsWaiting    => State == RecordingState.Waiting;
    public bool IsDone       => State == RecordingState.Done;
    public bool IsError      => State == RecordingState.Error;
    public bool IsRewritePending => State == RecordingState.RewritePending;
    public bool IsRewriteReview  => State == RecordingState.RewriteReview;

    public bool IsRewriteCapture => _orch.IsRewriteCapture;
    public string? Instruction => _orch.Instruction;
    public string? LastErrorMessage => _orch.LastErrorMessage;
    public IReadOnlyList<DiffToken>? Diff => _orch.Diff;

    public string StateColorTokenKey => State switch
    {
        RecordingState.Idle       => "OverlayIdleBrush",
        RecordingState.Listening  => "OverlayListeningBrush",
        RecordingState.Processing => "OverlayProcessingBrush",
        RecordingState.Speaking   => "OverlaySpeakingBrush",
        RecordingState.Waiting    => "OverlayWaitingBrush",
        RecordingState.Done       => "OverlayDoneBrush",
        RecordingState.Error      => "OverlayErrorBrush",
        RecordingState.RewritePending => "OverlayProcessingBrush",
        RecordingState.RewriteReview  => "OverlayProcessingBrush",
        _                         => "OverlayIdleBrush"
    };

    private void OnOrchestratorStateChanged(RecordingState newState) => _ui.TryEnqueue(() => Apply(newState));
    private void OnPartialTranscriptChanged(string text) => _ui.TryEnqueue(() => PartialTranscript = text);

    private void Apply(RecordingState state)
    {
        State = state;
        if (state != RecordingState.Listening) PartialTranscript = ""; // clear stale partial once recording ends
        OnPropertyChanged(nameof(IsVisible));
        OnPropertyChanged(nameof(IsListening));
        OnPropertyChanged(nameof(IsProcessing));
        OnPropertyChanged(nameof(IsSpeaking));
        OnPropertyChanged(nameof(IsWaiting));
        OnPropertyChanged(nameof(IsDone));
        OnPropertyChanged(nameof(IsError));
        OnPropertyChanged(nameof(IsRewritePending));
        OnPropertyChanged(nameof(IsRewriteReview));
        OnPropertyChanged(nameof(IsRewriteCapture));
        OnPropertyChanged(nameof(Instruction));
        OnPropertyChanged(nameof(LastErrorMessage));
        OnPropertyChanged(nameof(Diff));
        OnPropertyChanged(nameof(StateColorTokenKey));
    }
}
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Kivi.sln`
Expected: Build succeeded (0 errors)

- [ ] **Step 3: Commit**

```bash
git add Kivi.App/ViewModels/OverlayViewModel.cs
git commit -m "feat(app): expose rewrite/partial-transcript state on OverlayViewModel"
```

---

### Task 11: `LayeredOrb` — four-posture rendering overhaul

**Files:**
- Modify: `Kivi.App/Controls/LayeredOrb.cs`

**Interfaces:**
- Consumes: `OverlayViewModel.State/PartialTranscript/IsRewriteCapture/Instruction/LastErrorMessage/Diff` (Task 10); `Kivi.Core.Text.DiffOp/DiffToken` (Task 1).
- Produces: `LayeredOrb(OverlayViewModel vm, Color accent, string languageLabel)` constructor — a breaking change from today's `LayeredOrb(Color accent)` + `SetState(RecordingState)`. Consumed by `OverlayWindow.xaml.cs` (Task 12).

No dedicated test: GDI+ rendering has no test coverage today (the existing `LayeredOrb.cs` has zero unit tests). Verified by build + Task 16's manual smoke test (this is the highest-value manual check in the whole plan — see Task 16).

- [ ] **Step 1: Implement**

Replace the whole file `Kivi.App/Controls/LayeredOrb.cs`:

```csharp
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Kivi.App.Interop;
using Kivi.App.ViewModels;
using Kivi.Core.Orchestration;
using Kivi.Core.Text;
using Microsoft.UI.Dispatching;

namespace Kivi.App.Controls;

/// <summary>
/// The persistent desktop overlay, drawn as a genuinely transparent, click-through, always-on-top
/// Win32 <b>layered window</b> (UpdateLayeredWindow + a premultiplied-ARGB GDI+ bitmap) - WinUI 3
/// composites its own windows opaquely, so it can't float a soft-glowing free-form shape.
///
/// Four postures, growing from a fixed bottom-center anchor (brand "kivi on the desktop"):
///  - <b>Rest</b>  -> a small breathing pill.
///  - <b>Woken</b> -> a brief transitional round orb (dot-matrix kiwi + satellites) shown right
///    after leaving rest, before the box appears.
///  - <b>Dictating</b> -> a text-layout box (header/body/footer) for Listening and every
///    subsequent pipeline state.
///  - <b>Hey kivi</b> -> the same box, wider, rendering a word diff instead of plain body text,
///    while awaiting an accept (Enter) or reject (Esc).
///
/// Reads live state directly off <see cref="OverlayViewModel"/> every frame instead of being
/// pushed updates, since the viewmodel already marshals orchestrator events onto this same UI
/// thread's DispatcherQueue.
/// </summary>
public sealed class LayeredOrb : IDisposable
{
    private const string ClassName = "KiviOrbLayered";
    private static NativeMethods.WndProc? _wndProcKeepAlive;
    private static ushort _classAtom;

    // Design sizes in effective (96-dpi) px; scaled by the monitor DPI when drawn.
    private const double CanvasW = 520, CanvasH = 170;
    private const double Baseline = CanvasH - 30;       // shared bottom edge; postures grow upward
    private const double PillW = 39, PillH = 15;
    private const double OrbDiameter = 61;
    private const double SatelliteGap = 23;             // from the orb's edge
    private const double BoxW = 322, BoxH = 108, BoxRadius = 20;
    private const double BoxMaxWidthHeyKivi = 480;
    private const double WokenHoldSeconds = 0.25;        // how long the woken orb holds before growing into a box

    private static readonly Color Forest     = Color.FromArgb(255, 0x18, 0x30, 0x0F); // --brand-orbforest
    private static readonly Color Rim        = Color.FromArgb(255, 0x37, 0x63, 0x30);
    private static readonly Color BirdDots   = Color.FromArgb(255, 0xCF, 0xE0, 0xB0);
    private static readonly Color Satellite  = Color.FromArgb(235, 0xFF, 0xFF, 0xFF);
    private static readonly Color Paper2     = Color.FromArgb(255, 0xFF, 0xFF, 0xFF); // --color-paper2
    private static readonly Color Border1    = Color.FromArgb(255, 0xED, 0xF0, 0xE6); // --color-border1
    private static readonly Color Fg1        = Color.FromArgb(255, 0x14, 0x18, 0x0E); // --color-fg1
    private static readonly Color Fg2        = Color.FromArgb(255, 0x5C, 0x64, 0x54); // --color-fg2
    private static readonly Color Fg3        = Color.FromArgb(255, 0x92, 0x9A, 0x8A); // --color-fg3
    private static readonly Color Positive   = Color.FromArgb(255, 0x6E, 0xA3, 0x35); // --color-positive
    private static readonly Color PositiveBg = Color.FromArgb(255, 0xF2, 0xF8, 0xEB); // --color-positivebg

    // Fixed, distinct per-state colours (foundation palette) so transitions are unmistakable.
    private static readonly Color CIdle       = Color.FromArgb(0x6E, 0xA3, 0x35);
    private static readonly Color CListening  = Color.FromArgb(0xE9, 0x6C, 0x2F);
    private static readonly Color CProcessing = Color.FromArgb(0x42, 0x50, 0xD5);
    private static readonly Color CSpeaking   = Color.FromArgb(0x4B, 0x7D, 0x28);
    private static readonly Color CWaiting    = Color.FromArgb(0xD2, 0x96, 0x2D);
    private static readonly Color CDone       = Color.FromArgb(0x6E, 0xA3, 0x35);
    private static readonly Color CError      = Color.FromArgb(0xB8, 0x15, 0x14);

    private readonly nint _hwnd;
    private readonly OverlayViewModel _vm;
    private readonly Color _accent;
    private readonly string _languageLabel;
    private readonly DispatcherQueueTimer _timer;
    private readonly double _scale;

    private byte[]? _mask;
    private int _maskW, _maskH, _maskStride;
    private PrivateFontCollection? _fonts;
    private FontFamily? _interFamily;
    private FontFamily? _monoFamily;

    private RecordingState _prevState = RecordingState.Idle;
    private double _activeSeconds;
    private double _orbAmount;               // 0 = pill, 1 = orb
    private double _boxAmount;                // 0 = orb, 1 = box (eased)
    private ColorF _glow;                    // current glow colour (lerped)
    private long _lastTicks;
    private double _phase;                   // seconds, drives breathing + waveform
    private bool _disposed;

    public LayeredOrb(OverlayViewModel vm, Color accent, string languageLabel)
    {
        _vm = vm;
        _accent = accent;
        _languageLabel = languageLabel;
        _glow = ColorF.From(CIdle);
        EnsureClassRegistered();

        _hwnd = NativeMethods.CreateWindowExW(
            NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_TOPMOST | NativeMethods.WS_EX_NOACTIVATE,
            ClassName, "kivi", NativeMethods.WS_POPUP,
            0, 0, 10, 10, 0, 0, NativeMethods.GetModuleHandleW(null), 0);

        uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
        _scale = dpi == 0 ? 1.0 : dpi / 96.0;

        LoadMask();
        LoadFonts();

        NativeMethods.ShowWindow(_hwnd, NativeMethods.SW_SHOWNOACTIVATE);

        _lastTicks = Environment.TickCount64;
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) => Frame();
        _timer.Start();

        Frame();
    }

    // ---- animation loop ----
    private void Frame()
    {
        if (_disposed) return;
        long now = Environment.TickCount64;
        double dt = Math.Clamp((now - _lastTicks) / 1000.0, 0, 0.1);
        _lastTicks = now;
        _phase += dt;

        var state = _vm.State;
        bool isIdle = state == RecordingState.Idle;
        if (_prevState == RecordingState.Idle && !isIdle) _activeSeconds = 0;
        if (!isIdle) _activeSeconds += dt;
        _prevState = state;

        double orbTarget = isIdle ? 0.0 : 1.0;
        double boxTarget = (!isIdle && _activeSeconds > WokenHoldSeconds) ? 1.0 : 0.0;
        _orbAmount = Approach(_orbAmount, orbTarget, dt / 0.12);
        _boxAmount = Approach(_boxAmount, boxTarget, dt / 0.12);

        var gTarget = ColorF.From(StateColor(state));
        _glow = ColorF.Lerp(_glow, gTarget, Math.Clamp(dt / 0.12, 0, 1));

        Render();

        bool settled = isIdle && _orbAmount < 0.001 && _glow.Near(gTarget);
        var want = TimeSpan.FromMilliseconds(settled ? 50 : 16);
        if (Math.Abs(_timer.Interval.TotalMilliseconds - want.TotalMilliseconds) > 1) _timer.Interval = want;
    }

    private Color StateColor(RecordingState s) => s switch
    {
        RecordingState.Listening  => _vm.IsRewriteCapture ? CProcessing : CListening,
        RecordingState.Processing => CProcessing,
        RecordingState.Speaking   => CSpeaking,
        RecordingState.Waiting    => CWaiting,
        RecordingState.Done       => CDone,
        RecordingState.Error      => CError,
        RecordingState.RewritePending => CProcessing,
        RecordingState.RewriteReview  => CProcessing,
        _                         => IdleGlow(),
    };

    // At rest, honour the user's accent colour if it's bright enough to read as a glow.
    private Color IdleGlow()
    {
        double lum = (0.299 * _accent.R + 0.587 * _accent.G + 0.114 * _accent.B) / 255.0;
        return lum > 0.28 ? _accent : CIdle;
    }

    private void Render()
    {
        int w = (int)Math.Round(CanvasW * _scale);
        int h = (int)Math.Round(CanvasH * _scale);
        using var bmp = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            double orbT = Smooth(_orbAmount);
            double boxT = Smooth(_boxAmount);
            float cx = w / 2f;
            float baseline = (float)(Baseline * _scale);

            float pillAlpha = (float)(1 - orbT);
            float orbAlpha = (float)(orbT * (1 - boxT));
            float boxAlpha = (float)boxT;

            if (pillAlpha > 0.001f) DrawPill(g, cx, baseline, pillAlpha);
            if (orbAlpha > 0.001f) DrawOrb(g, cx, baseline, orbAlpha);
            if (boxAlpha > 0.001f) DrawBox(g, cx, baseline, boxAlpha);
        }
        PushLayered(bmp, w, h);
    }

    // ---- rest posture ----
    private void DrawPill(Graphics g, float cx, float baseline, float alpha)
    {
        double s = _scale;
        float w = (float)(PillW * s), h = (float)(PillH * s);
        float left = cx - w / 2f, top = baseline - h;
        double breath = 0.5 + 0.5 * Math.Sin(_phase * 1.6);

        Color gc = _glow.ToColor();
        float glowR = (float)(w * 0.9 + (6 + 4 * breath) * s);
        DrawGlow(g, cx, top + h / 2f, glowR, Mul(gc, (float)(0.22 + 0.16 * breath) * alpha));

        using var path = RoundedRect(left, top, w, h, h / 2f);
        using var fill = new SolidBrush(Mul(Forest, alpha));
        g.FillPath(fill, path);
    }

    // ---- woken posture ----
    private void DrawOrb(Graphics g, float cx, float baseline, float alpha)
    {
        double s = _scale;
        float r = (float)(OrbDiameter * s / 2);
        float cy = baseline - r;
        double breath = 0.5 + 0.5 * Math.Sin(_phase * 2.4);

        Color gc = _glow.ToColor();
        float glowR = (float)(r + (18 + 8 * breath) * s);
        DrawGlow(g, cx, cy, glowR, Mul(gc, (0.32 + 0.30 * breath) * alpha));

        float satR = (float)(4 * s);
        float satX = (float)(r + SatelliteGap * s);
        FillCircle(g, cx - satX, cy, satR, Mul(Satellite, alpha));
        FillCircle(g, cx + satX, cy, satR, Mul(Satellite, alpha));

        FillCircle(g, cx, cy, r, Mul(Forest, alpha));
        using (var pen = new Pen(Mul(Rim, alpha), (float)(1.2 * s)))
            g.DrawEllipse(pen, cx - r, cy - r, r * 2, r * 2);

        DrawBird(g, cx, cy - (float)(1.5 * s), (float)(OrbDiameter * 0.74 * s), Mul(BirdDots, alpha));
    }

    // ---- dictating / hey-kivi posture ----
    private void DrawBox(Graphics g, float cx, float baseline, float alpha)
    {
        var state = _vm.State;
        bool isHeyKivi = _vm.IsRewriteCapture || state is RecordingState.RewritePending or RecordingState.RewriteReview;
        double s = _scale;

        string header = HeaderLabel(state);
        float desiredW = (float)(BoxW * s);
        if (isHeyKivi)
        {
            using var headerFont = MakeFont(11f, mono: true);
            var headerSize = g.MeasureString(header, headerFont);
            float contentW = headerSize.Width + (float)(40 * s);
            desiredW = Math.Max(desiredW, Math.Min(contentW, (float)(BoxMaxWidthHeyKivi * s)));
        }

        float bh = (float)(BoxH * s);
        float rad = (float)(BoxRadius * s);
        float sc = 0.96f + 0.04f * alpha;
        float bw = desiredW * sc; bh *= sc;
        float left = cx - bw / 2f;
        float top = baseline - bh;

        for (int i = 3; i >= 1; i--)
        {
            float e = i * (float)(3 * s);
            using var sh = RoundedRect(left - e * 0.3f, top + e * 0.6f, bw + e * 0.6f, bh + e, rad + e);
            using var sb = new SolidBrush(Color.FromArgb((int)(12 * alpha), 20, 20, 20));
            g.FillPath(sb, sh);
        }

        using (var path = RoundedRect(left, top, bw, bh, rad))
        {
            using var fill = new SolidBrush(Mul(Paper2, alpha));
            g.FillPath(fill, path);
            using var edge = new Pen(Mul(Border1, alpha), (float)s);
            g.DrawPath(edge, path);
        }

        float padX = (float)(20 * s);
        float headerY = top + (float)(16 * s);

        using (var headerFont = MakeFont(11f, mono: true))
        {
            var headerColor = isHeyKivi ? CProcessing : Fg3;
            using var hb = new SolidBrush(Mul(headerColor, alpha));
            g.DrawString(header, headerFont, hb, left + padX, headerY);

            if (!isHeyKivi)
            {
                using var chipFont = MakeFont(12f, mono: true);
                var chipSize = g.MeasureString(_languageLabel, chipFont);
                using var cb = new SolidBrush(Mul(Fg2, alpha));
                g.DrawString(_languageLabel, chipFont, cb, left + bw - padX - chipSize.Width, headerY);
            }
        }

        float bodyTop = headerY + (float)(22 * s);
        float bodyBottom = top + bh - (float)(12 * s) - (float)(18 * s);
        var bodyRect = new RectangleF(left + padX, bodyTop, bw - padX * 2, Math.Max(0, bodyBottom - bodyTop));

        if (state == RecordingState.RewriteReview && _vm.Diff is { Count: > 0 } diff)
        {
            DrawDiffText(g, bodyRect, diff, alpha);
        }
        else
        {
            var body = BodyText(state);
            if (body.Length > 0)
            {
                bool placeholder = state == RecordingState.Listening && !_vm.IsRewriteCapture && string.IsNullOrEmpty(_vm.PartialTranscript);
                using var bodyFont = MakeFont(15f);
                using var bb = new SolidBrush(Mul(placeholder ? Fg3 : Fg1, alpha));
                using var fmt = new StringFormat { Trimming = StringTrimming.EllipsisCharacter };
                g.DrawString(body, bodyFont, bb, bodyRect, fmt);
            }
        }

        var footer = FooterText(state);
        if (footer.Length > 0)
        {
            using var footerFont = MakeFont(12f, mono: true);
            using var fb = new SolidBrush(Mul(Fg2, alpha));
            g.DrawString(footer, footerFont, fb, left + padX, top + bh - (float)(12 * s) - (float)(14 * s));
        }
    }

    private void DrawDiffText(Graphics g, RectangleF bounds, IReadOnlyList<DiffToken> diff, float alpha)
    {
        using var font = MakeFont(15f);
        float lineHeight = (float)(15 * 1.65 * _scale);
        float x = bounds.Left, y = bounds.Top;

        foreach (var token in diff)
        {
            if (token.Text.Length == 0) continue;
            if (string.IsNullOrWhiteSpace(token.Text))
            {
                if (token.Text.Contains('\n')) { x = bounds.Left; y += lineHeight; }
                else x += g.MeasureString(token.Text, font).Width;
                continue;
            }

            var size = g.MeasureString(token.Text, font);
            if (x + size.Width > bounds.Right) { x = bounds.Left; y += lineHeight; }
            if (y + lineHeight > bounds.Bottom) break; // clip silently past the box's visible height

            switch (token.Op)
            {
                case DiffOp.Insert:
                    using (var bg = new SolidBrush(Mul(PositiveBg, alpha)))
                        g.FillRectangle(bg, x, y, size.Width, lineHeight * 0.82f);
                    using (var fg = new SolidBrush(Mul(Positive, alpha)))
                        g.DrawString(token.Text, font, fg, x, y);
                    break;
                case DiffOp.Delete:
                    using (var fg = new SolidBrush(Mul(Fg2, alpha)))
                        g.DrawString(token.Text, font, fg, x, y);
                    using (var pen = new Pen(Mul(Fg2, alpha), (float)(1 * _scale)))
                        g.DrawLine(pen, x, y + lineHeight * 0.5f, x + size.Width, y + lineHeight * 0.5f);
                    break;
                default:
                    using (var fg = new SolidBrush(Mul(Fg1, alpha)))
                        g.DrawString(token.Text, font, fg, x, y);
                    break;
            }
            x += size.Width;
        }
    }

    private string HeaderLabel(RecordingState s)
    {
        bool heyKivi = _vm.IsRewriteCapture || s is RecordingState.RewritePending or RecordingState.RewriteReview;
        if (heyKivi)
        {
            var instr = s == RecordingState.Listening ? _vm.PartialTranscript : _vm.Instruction;
            return string.IsNullOrWhiteSpace(instr) ? "HEY KIVI" : $"HEY KIVI · \"{instr}\"";
        }
        return s switch
        {
            RecordingState.Listening  => "LIVE",
            RecordingState.Processing => "POLISHING",
            RecordingState.Speaking   => "INSERTING",
            RecordingState.Waiting    => "COOLING DOWN",
            RecordingState.Done       => "DONE",
            RecordingState.Error      => "ERROR",
            _                         => "KIVI",
        };
    }

    private string BodyText(RecordingState s) => s switch
    {
        RecordingState.Listening      => string.IsNullOrEmpty(_vm.PartialTranscript)
            ? "Press right ctrl and speak — finished text appears here, in your style…"
            : _vm.PartialTranscript,
        RecordingState.Processing     => "Cleaning up your text…",
        RecordingState.Speaking       => "Pasting…",
        RecordingState.Waiting        => "Rate limited — retrying shortly…",
        RecordingState.Done           => "Done.",
        RecordingState.Error          => _vm.LastErrorMessage ?? "Couldn't catch that.",
        RecordingState.RewritePending => "Rewriting…",
        _                              => "",
    };

    private string FooterText(RecordingState s) => s switch
    {
        RecordingState.Listening     => _vm.IsRewriteCapture ? "release to rewrite · esc to discard" : "right ctrl to stop · esc to discard",
        RecordingState.RewriteReview => "⏎ paste · esc keep original",
        _                             => "",
    };

    private void DrawBird(Graphics g, float cx, float cy, float boxH, Color color)
    {
        if (_mask is null || _maskW == 0) return;
        const int cols = 14;
        float aspect = (float)_maskW / _maskH;
        float boxW = boxH * aspect;
        int rows = Math.Max(1, (int)Math.Round(boxH / boxW * cols));
        float cellW = boxW / cols, cellH = boxH / rows;
        float dot = Math.Min(cellW, cellH) * 0.82f;
        float left = cx - boxW / 2f, top = cy - boxH / 2f;
        using var brush = new SolidBrush(color);
        for (int row = 0; row < rows; row++)
            for (int col = 0; col < cols; col++)
            {
                int px = (int)((col + 0.5) / cols * _maskW);
                int py = (int)((row + 0.5) / rows * _maskH);
                int off = py * _maskStride + px * 4;
                byte a = (off + 3 < _mask.Length) ? _mask[off + 3] : (byte)0;
                if (a < 40) continue;
                g.FillEllipse(brush, left + col * cellW + (cellW - dot) / 2f, top + row * cellH + (cellH - dot) / 2f, dot, dot);
            }
    }

    // ---- primitives ----
    private static void DrawGlow(Graphics g, float cx, float cy, float radius, Color center)
    {
        if (radius <= 0 || center.A == 0) return;
        using var path = new GraphicsPath();
        path.AddEllipse(cx - radius, cy - radius, radius * 2, radius * 2);
        using var brush = new PathGradientBrush(path)
        {
            CenterPoint = new PointF(cx, cy),
            CenterColor = center,
            SurroundColors = new[] { Color.FromArgb(0, center) },
        };
        g.FillEllipse(brush, cx - radius, cy - radius, radius * 2, radius * 2);
    }

    private static void FillCircle(Graphics g, float cx, float cy, float r, Color c)
    {
        using var b = new SolidBrush(c);
        g.FillEllipse(b, cx - r, cy - r, r * 2, r * 2);
    }

    private static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2f);
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private Font MakeFont(float px, bool mono = false)
    {
        float size = px * (float)_scale;
        var family = mono ? _monoFamily : _interFamily;
        try { if (family != null) return new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel); }
        catch { }
        return new Font(mono ? "Consolas" : "Segoe UI", size, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    private static Color Mul(Color c, double a) => Color.FromArgb((int)Math.Clamp(c.A * a, 0, 255), c.R, c.G, c.B);
    private static double Approach(double v, double t, double step) => v < t ? Math.Min(t, v + step) : Math.Max(t, v - step);
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    private readonly struct ColorF
    {
        public readonly double R, G, B;
        private ColorF(double r, double g, double b) { R = r; G = g; B = b; }
        public static ColorF From(Color c) => new(c.R, c.G, c.B);
        public static ColorF Lerp(ColorF a, ColorF b, double t) => new(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);
        public Color ToColor() => Color.FromArgb(255, (int)Math.Clamp(R, 0, 255), (int)Math.Clamp(G, 0, 255), (int)Math.Clamp(B, 0, 255));
        public bool Near(ColorF o) => Math.Abs(R - o.R) + Math.Abs(G - o.G) + Math.Abs(B - o.B) < 3;
    }

    // ---- infra ----
    private void LoadMask()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Icons", "kivi-mask.png");
            using var src = new Bitmap(path);
            _maskW = src.Width; _maskH = src.Height;
            var data = src.LockBits(new Rectangle(0, 0, _maskW, _maskH), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            _mask = new byte[data.Stride * data.Height];
            System.Runtime.InteropServices.Marshal.Copy(data.Scan0, _mask, 0, _mask.Length);
            _maskStride = data.Stride;
            src.UnlockBits(data);
        }
        catch { _mask = null; }
    }

    private void LoadFonts()
    {
        try
        {
            _fonts = new PrivateFontCollection();
            var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts");
            foreach (var f in new[] { "Inter-Medium.ttf", "Inter-Regular.ttf", "SpaceMono-Regular.ttf" })
            {
                var p = System.IO.Path.Combine(dir, f);
                if (System.IO.File.Exists(p)) _fonts.AddFontFile(p);
            }
            foreach (var fam in _fonts.Families)
            {
                if (fam.Name.Contains("Mono", StringComparison.OrdinalIgnoreCase)) _monoFamily = fam;
                else _interFamily ??= fam;
            }
        }
        catch { _interFamily = null; _monoFamily = null; }
    }

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0) return;
        _wndProcKeepAlive = NativeMethods.DefWindowProcW;
        var wc = new NativeMethods.WNDCLASSEX
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
            lpfnWndProc = _wndProcKeepAlive,
            hInstance = NativeMethods.GetModuleHandleW(null),
            lpszClassName = ClassName,
        };
        _classAtom = NativeMethods.RegisterClassExW(ref wc);
    }

    private void PushLayered(Bitmap bmp, int w, int h)
    {
        nint mon = NativeMethods.MonitorFromWindow(_hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var mi = new NativeMethods.MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFO>() };
        int x, y;
        if (NativeMethods.GetMonitorInfoW(mon, ref mi))
        {
            x = mi.rcWork.Left + ((mi.rcWork.Right - mi.rcWork.Left) - w) / 2;
            y = mi.rcWork.Bottom - h - (int)Math.Round(14 * _scale);
        }
        else { x = 0; y = 0; }

        nint screenDC = NativeMethods.GetDC(0);
        nint memDC = NativeMethods.CreateCompatibleDC(screenDC);
        nint hbm = bmp.GetHbitmap(Color.FromArgb(0));
        nint old = NativeMethods.SelectObject(memDC, hbm);
        try
        {
            var ptDst = new NativeMethods.POINT(x, y);
            var size = new NativeMethods.SIZE(w, h);
            var ptSrc = new NativeMethods.POINT(0, 0);
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA,
            };
            NativeMethods.UpdateLayeredWindow(_hwnd, screenDC, ref ptDst, ref size, memDC, ref ptSrc, 0, ref blend, NativeMethods.ULW_ALPHA);
        }
        finally
        {
            NativeMethods.SelectObject(memDC, old);
            NativeMethods.DeleteObject(hbm);
            NativeMethods.DeleteDC(memDC);
            NativeMethods.ReleaseDC(0, screenDC);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _fonts?.Dispose();
        if (_hwnd != 0) NativeMethods.DestroyWindow(_hwnd);
    }
}
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Kivi.sln`
Expected: Build fails at this point — `OverlayWindow.xaml.cs` (Task 12) still calls the old `LayeredOrb(Color)` constructor and `SetState`. That's expected; Task 12 fixes it. Confirm the *error* is specifically in `OverlayWindow.xaml.cs`, not inside `LayeredOrb.cs` itself (no errors should be reported against `LayeredOrb.cs`).

- [ ] **Step 3: Commit**

```bash
git add Kivi.App/Controls/LayeredOrb.cs
git commit -m "feat(app): rewrite LayeredOrb for the four-posture design + diff rendering"
```

---

### Task 12: `OverlayWindow.xaml.cs` — wire the new `LayeredOrb` constructor

**Files:**
- Modify: `Kivi.App/Views/OverlayWindow.xaml.cs`

**Interfaces:**
- Consumes: `LayeredOrb(OverlayViewModel, Color, string)` (Task 11).
- Produces: `OverlayWindow(OverlayViewModel vm, Color accent, string languageLabel)` constructor — consumed by `App.xaml.cs` (Task 15).

- [ ] **Step 1: Implement**

Replace the whole file `Kivi.App/Views/OverlayWindow.xaml.cs`:

```csharp
using System.Drawing;
using Kivi.App.Controls;
using Kivi.App.ViewModels;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using WinRT.Interop;
using Windows.Graphics;

namespace Kivi.App.Views;

/// <summary>
/// Invisible lifetime-anchor window. A WinUI app exits when its last <see cref="Window"/>
/// closes, so this 1x1 off-screen window keeps the process alive while the actual, visible
/// desktop orb is drawn by a separate Win32 layered window (<see cref="LayeredOrb"/>) - WinUI
/// composites its own windows opaquely and cannot float a transparent, glowing orb.
/// </summary>
public sealed partial class OverlayWindow : Window
{
    private readonly OverlayViewModel _vm;
    private readonly LayeredOrb _orb;

    public OverlayWindow(OverlayViewModel vm, Color accent, string languageLabel)
    {
        InitializeComponent();
        _vm = vm;

        // Push this anchor window off-screen and shrink it so it is never seen.
        nint hwnd = WindowNative.GetWindowHandle(this);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        var presenter = OverlappedPresenter.Create();
        presenter.SetBorderAndTitleBar(false, false);
        presenter.IsMinimizable = false;
        presenter.IsMaximizable = false;
        appWindow.SetPresenter(presenter);
        appWindow.IsShownInSwitchers = false;
        appWindow.Resize(new SizeInt32(1, 1));
        appWindow.Move(new PointInt32(-32000, -32000));

        // The visible orb lives here, on the UI thread (its render timer needs the
        // DispatcherQueue) and reads state straight off _vm every frame.
        _orb = new LayeredOrb(vm, accent, languageLabel);

        Closed += (_, _) => _orb.Dispose();

        // Activate (still off-screen, so invisible) so the window counts as "open" and keeps
        // the app running after onboarding closes.
        Activate();
    }
}
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Kivi.sln`
Expected: Build fails at this point — `App.xaml.cs` (Task 15) still calls the old `OverlayWindow(OverlayViewModel, Color)` constructor. Confirm the error is specifically in `App.xaml.cs`.

- [ ] **Step 3: Commit**

```bash
git add Kivi.App/Views/OverlayWindow.xaml.cs
git commit -m "feat(app): wire OverlayWindow to the vm-driven LayeredOrb constructor"
```

---

### Task 13: `ConfigViewModel` + `HotkeyCaptureBox` — rewrite-hotkey support

**Files:**
- Modify: `Kivi.App/ViewModels/ConfigViewModel.cs`
- Modify: `Kivi.App/Controls/HotkeyCaptureBox.cs`

**Interfaces:**
- Consumes: `AppConfig.RewriteHotkeyVirtualKeyCode` (Task 2), `IHotkeyService.SetRewriteHotkey` (Task 7).
- Produces: `ConfigViewModel.RewriteHotkeyVk` (uint, bindable). Consumed by `ConfigPage.xaml.cs` (Task 14).

- [ ] **Step 1: Implement**

Replace the whole file `Kivi.App/ViewModels/ConfigViewModel.cs`:

```csharp
// Kivi.App/ViewModels/ConfigViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;

namespace Kivi.App.ViewModels;

/// <summary>
/// Bindable config state for the onboarding Config page. Property changes write straight
/// through to the shared AppConfig singleton (not yet persisted); Persist() flips
/// OnboardingCompleted and saves via IAppConfigStore.
/// </summary>
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
        RewriteHotkeyVk = config.RewriteHotkeyVirtualKeyCode;
        LaunchAtLogin = Services.StartupLauncher.IsEnabled();
    }

    [ObservableProperty] private string _orbAccentColor = "#41691E";
    [ObservableProperty] private string _transcriptionLanguage = "auto";
    [ObservableProperty] private bool _screenContextEnabled = true;
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private uint _hotkeyVk = 0xA3;
    [ObservableProperty] private uint _rewriteHotkeyVk = 0xA5;

    partial void OnOrbAccentColorChanged(string value) => _config.OrbAccentColor = value;

    partial void OnTranscriptionLanguageChanged(string value)
        => _config.TranscriptionLanguage = value == "auto" ? null : value;

    partial void OnScreenContextEnabledChanged(bool value) => _config.ScreenContextEnabled = value;

    partial void OnLaunchAtLoginChanged(bool value) => Services.StartupLauncher.SetEnabled(value);

    partial void OnHotkeyVkChanged(uint value)
    {
        _config.HotkeyVirtualKeyCode = value;
        _hotkey.SetHotkey(value);
    }

    partial void OnRewriteHotkeyVkChanged(uint value)
    {
        _config.RewriteHotkeyVirtualKeyCode = value;
        _hotkey.SetRewriteHotkey(value);
    }

    public void Persist()
    {
        _config.OnboardingCompleted = true;
        _store.Save(_config);
    }
}
```

In `Kivi.App/Controls/HotkeyCaptureBox.cs`, update the `Label` method's switch expression (replace it):

```csharp
    private static string Label(uint vk) => vk switch
    {
        0xA3 => "Right Ctrl",
        0xA2 => "Left Ctrl",
        0xA0 => "Left Shift",
        0xA1 => "Right Shift",
        0xA4 => "Left Alt",
        0xA5 => "Right Alt",
        _ => ((VirtualKey)vk).ToString()
    };
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Kivi.sln`
Expected: Build fails at this point — `ConfigPage.xaml.cs` (Task 14) doesn't reference `RewriteHotkeyVk` yet, but that's fine since it's an *additive* property; the pre-existing error carried over from Task 12 (`App.xaml.cs`) is still the only one expected. Confirm no NEW errors appear in `ConfigViewModel.cs` or `HotkeyCaptureBox.cs`.

- [ ] **Step 3: Commit**

```bash
git add Kivi.App/ViewModels/ConfigViewModel.cs Kivi.App/Controls/HotkeyCaptureBox.cs
git commit -m "feat(app): add rewrite-hotkey property to ConfigViewModel + Right Alt label"
```

---

### Task 14: `ConfigPage` — second hotkey capture control

**Files:**
- Modify: `Kivi.App/Views/Onboarding/ConfigPage.xaml`
- Modify: `Kivi.App/Views/Onboarding/ConfigPage.xaml.cs`

**Interfaces:**
- Consumes: `ConfigViewModel.RewriteHotkeyVk` (Task 13), `HotkeyCaptureBox` (existing control, reused as-is).

- [ ] **Step 1: Implement**

In `Kivi.App/Views/Onboarding/ConfigPage.xaml`, replace the "Hotkey card" `Border` block (the one containing `x:Name="HotkeyBox"`) with a two-row version that adds the rewrite hotkey below a divider, mirroring the existing "Behaviour card" pattern:

```xml
            <!-- Hotkey card -->
            <Border Style="{StaticResource KiviCardStyle}">
                <StackPanel Spacing="16">
                    <Grid ColumnSpacing="16">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0" Spacing="2" VerticalAlignment="Center">
                            <TextBlock Text="Dictation hotkey" FontFamily="{ThemeResource KiviFontFamily}"
                                       FontSize="{ThemeResource KiviFontSizeBody}"
                                       FontWeight="{ThemeResource KiviFontWeightMedium}"
                                       Foreground="{ThemeResource KiviTextPrimaryBrush}"/>
                            <TextBlock Text="hold this key to dictate" FontFamily="{ThemeResource KiviFontFamily}"
                                       FontSize="{ThemeResource KiviFontSizeCaption}"
                                       Foreground="{ThemeResource KiviTextSecondaryBrush}"/>
                        </StackPanel>
                        <controls:HotkeyCaptureBox x:Name="HotkeyBox" Grid.Column="1"
                                Background="{ThemeResource KiviSurfaceAltBrush}"
                                BorderBrush="{ThemeResource KiviStrokeBrush}" BorderThickness="1"
                                CornerRadius="{ThemeResource KiviRadiusSm}"
                                Foreground="{ThemeResource KiviTextPrimaryBrush}"
                                FontFamily="{ThemeResource KiviMonoFontFamily}"
                                FontSize="{ThemeResource KiviFontSizeBody}"
                                Padding="16,10"/>
                    </Grid>

                    <Rectangle Height="1" Fill="{ThemeResource KiviStrokeBrush}"/>

                    <Grid ColumnSpacing="16">
                        <Grid.ColumnDefinitions>
                            <ColumnDefinition Width="*"/>
                            <ColumnDefinition Width="Auto"/>
                        </Grid.ColumnDefinitions>
                        <StackPanel Grid.Column="0" Spacing="2" VerticalAlignment="Center">
                            <TextBlock Text="Hey-kivi rewrite hotkey" FontFamily="{ThemeResource KiviFontFamily}"
                                       FontSize="{ThemeResource KiviFontSizeBody}"
                                       FontWeight="{ThemeResource KiviFontWeightMedium}"
                                       Foreground="{ThemeResource KiviTextPrimaryBrush}"/>
                            <TextBlock Text="hold and speak an edit for what kivi last typed" FontFamily="{ThemeResource KiviFontFamily}"
                                       FontSize="{ThemeResource KiviFontSizeCaption}"
                                       Foreground="{ThemeResource KiviTextSecondaryBrush}"/>
                        </StackPanel>
                        <controls:HotkeyCaptureBox x:Name="RewriteHotkeyBox" Grid.Column="1"
                                Background="{ThemeResource KiviSurfaceAltBrush}"
                                BorderBrush="{ThemeResource KiviStrokeBrush}" BorderThickness="1"
                                CornerRadius="{ThemeResource KiviRadiusSm}"
                                Foreground="{ThemeResource KiviTextPrimaryBrush}"
                                FontFamily="{ThemeResource KiviMonoFontFamily}"
                                FontSize="{ThemeResource KiviFontSizeBody}"
                                Padding="16,10"/>
                    </Grid>
                </StackPanel>
            </Border>
```

In `Kivi.App/Views/Onboarding/ConfigPage.xaml.cs`, add two lines to the constructor right after the existing `HotkeyBox` wiring (after `HotkeyBox.HotkeyChanged += vk => ViewModel.HotkeyVk = vk;`):

```csharp
        RewriteHotkeyBox.SetInitial(ViewModel.RewriteHotkeyVk);
        RewriteHotkeyBox.HotkeyChanged += vk => ViewModel.RewriteHotkeyVk = vk;
```

- [ ] **Step 2: Verify the solution builds**

Run: `dotnet build Kivi.sln`
Expected: The only remaining error should be the one already known from Task 12 (`App.xaml.cs`'s stale `OverlayWindow` constructor call). Confirm no new errors in `ConfigPage.xaml`/`ConfigPage.xaml.cs`.

- [ ] **Step 3: Commit**

```bash
git add Kivi.App/Views/Onboarding/ConfigPage.xaml Kivi.App/Views/Onboarding/ConfigPage.xaml.cs
git commit -m "feat(app): add rewrite-hotkey capture control to the Config page"
```

---

### Task 15: `App.xaml.cs` — apply the rewrite hotkey + pass the language label

**Files:**
- Modify: `Kivi.App/App.xaml.cs`

**Interfaces:**
- Consumes: `IHotkeyService.SetRewriteHotkey` (Task 7), `AppConfig.RewriteHotkeyVirtualKeyCode` (Task 2), `OverlayWindow(OverlayViewModel, Color, string)` (Task 12).

- [ ] **Step 1: Implement**

In `Kivi.App/App.xaml.cs`, update the hotkey-application block (after line 107, the existing `hotkey.SetHotkey(appConfig.HotkeyVirtualKeyCode);` line) to also apply the rewrite hotkey:

```csharp
        // Re-apply the user's saved hotkeys on every launch.
        var hotkey = Services.GetRequiredService<IHotkeyService>();
        hotkey.SetHotkey(appConfig.HotkeyVirtualKeyCode);
        hotkey.SetRewriteHotkey(appConfig.RewriteHotkeyVirtualKeyCode);
```

Update the `ShowOrb` local function inside `RunStartupGateAsync` (originally at lines 123-127) to compute and pass a language label:

```csharp
        void ShowOrb()
        {
            var overlayVm = new ViewModels.OverlayViewModel(orchestrator, dispatcher);
            var languageLabel = string.IsNullOrWhiteSpace(appConfig.TranscriptionLanguage) ? "auto" : appConfig.TranscriptionLanguage!;
            _overlayWindow = new Views.OverlayWindow(overlayVm, GdiColorFromHex(appConfig.OrbAccentColor), languageLabel);
        }
```

- [ ] **Step 2: Verify the whole solution builds clean**

Run: `dotnet build Kivi.sln`
Expected: Build succeeded (0 errors) — this resolves the stale-constructor errors carried since Task 12.

- [ ] **Step 3: Commit**

```bash
git add Kivi.App/App.xaml.cs
git commit -m "feat(app): apply the saved rewrite hotkey and pass the language label to the orb"
```

---

### Task 16: Final full-solution verification

**Files:** none (verification only)

- [ ] **Step 1: Full solution build**

Run: `dotnet build Kivi.sln`
Expected: Build succeeded (0 errors, 0 warnings introduced by this work — pre-existing warnings, if any, are out of scope)

- [ ] **Step 2: Full Kivi.Core test suite**

Run: `dotnet test Kivi.Core.Tests`
Expected: PASS — every test from Tasks 1-9 plus the full pre-existing suite (`AppConfigStoreTests`, `AppConfigTests`, `FakeHttpMessageHandler`-based tests, `GroqPolishClientTests`, `GroqSttEngineTests`, `KiviMetricsTests`, `MacroTests`, `OpenAiCompatibleClientTests`, `OrchestratorTests`, `PolishPipelineTests`, `PromptsTests`, `WordDiffTests`)

- [ ] **Step 3: Manual smoke test (the only verification available for `Kivi.Platform`/`Kivi.App` — neither has an automated test harness, matching this repo's existing state)**

Run the app (`dotnet run --project Kivi.App`, or use the `run` skill if available) and manually confirm, in order:

1. **Rest**: at idle, the overlay shows a small breathing pill (not the old full-size round orb).
2. **Woken → Dictating**: hold the dictation hotkey (Right Ctrl by default) — the pill briefly grows into the round orb with satellites, then grows into the text box. Speak a short sentence; after ~1s a live partial transcript should appear in the box body, updating roughly once per second. Release the hotkey — the box shows "POLISHING" then "INSERTING" then "DONE" before returning to the rest pill.
3. **Hey kivi**: hold the rewrite hotkey (Right Alt by default) right after a successful dictation, and speak an instruction like "make it more formal." The header should read `HEY KIVI · "<your instruction>"`. On release, the box should briefly show "Rewriting…" then the diff (struck-through old words, green-highlighted new words) with the footer "⏎ paste · esc keep original."
4. **Accept**: press Enter while the diff is showing — confirm the target app's text is replaced with the rewritten version (the original dictation is undone, the new text pasted in its place).
5. **Reject**: repeat the hey-kivi flow, but press Esc instead — confirm the target app's text is unchanged.
6. **Nothing to rewrite**: restart the app (so there's no prior dictation this session) and hold the rewrite hotkey first — confirm the box shows an error state and returns to rest without pasting anything.
7. **Config page**: open onboarding's Config page (or trigger it per the existing onboarding-gate flow) and confirm a second hotkey-capture control for the rewrite hotkey is visible below the dictation one, defaults to showing "Right Alt", and can be rebound by clicking it and pressing a different key.

If any step fails, file it as a follow-up — do not silently patch behavior beyond this plan's scope during verification.

- [ ] **Step 4: Final commit (if the manual smoke test required no code changes, this step is a no-op — nothing to commit)**

If Step 3 required fixes, commit them with a message describing what the smoke test caught, e.g.:

```bash
git add -A
git commit -m "fix(app): address issues found in overlay-postures manual smoke test"
```

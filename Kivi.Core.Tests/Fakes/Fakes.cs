using Kivi.Core.Abstractions;

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
    public void SetEnabled(bool enabled) { }

    public void FireStart() => HoldStarted?.Invoke();
    public void FireEnd() => HoldEnded?.Invoke();
    public void FireRewriteStart() => RewriteHoldStarted?.Invoke();
    public void FireRewriteEnd() => RewriteHoldEnded?.Invoke();
    public void FireReviewAccepted() => ReviewAccepted?.Invoke();
    public void FireReviewCancelled() => ReviewCancelled?.Invoke();
}

public sealed class FakeAudio : IAudioCaptureService
{
    public event Action<string>? DeviceChanged;
    public byte[] Wav = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
    public Task StartRecordingAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<byte[]> StopRecordingAsync() => Task.FromResult(Wav);
    public byte[] SnapshotRecording() => Wav;
}

public sealed class FakeContext : IScreenContextProvider
{
    public Task<string> CaptureContextAsync(CancellationToken ct) => Task.FromResult("App: Notepad");
}

public sealed class SpyContext : IScreenContextProvider
{
    public int Calls;
    public Task<string> CaptureContextAsync(CancellationToken ct) { Calls++; return Task.FromResult("App: Notepad"); }
}

public sealed class SpyPaste : IPasteService
{
    public string? Pasted; public bool PressedEnter; public int UndoCalls;
    public List<string> CallOrder { get; } = new();
    public Task InjectTextAsync(string text, bool pressEnter)
    {
        Pasted = text; PressedEnter = pressEnter;
        CallOrder.Add("Inject:" + text);
        return Task.CompletedTask;
    }
    public Task UndoAsync() { UndoCalls++; CallOrder.Add("Undo"); return Task.CompletedTask; }
}

public sealed class StubStt : Kivi.Core.Stt.ISttEngine
{
    public string Result = "hello there";
    public Task<string> TranscribeAsync(byte[] wav, CancellationToken ct) => Task.FromResult(Result);
}

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

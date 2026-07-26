using Kivi.Core.Abstractions;

public sealed class FakeHotkey : IHotkeyService
{
    public event Action? HoldStarted;
    public event Action? HoldEnded;
    public event Action? EnglishHoldStarted;
    public event Action? EnglishHoldEnded;
    public event Action<bool>? HandsFreeToggled;

    public void Start() { } public void Stop() { }
    public void SetHotkey(uint virtualKeyCode) { }
    public void SetEnglishHotkey(uint virtualKeyCode) { }
    public void SetEnabled(bool enabled) { }

    public void FireStart() => HoldStarted?.Invoke();
    public void FireEnd() => HoldEnded?.Invoke();
    public void FireEnglishStart() => EnglishHoldStarted?.Invoke();
    public void FireEnglishEnd() => EnglishHoldEnded?.Invoke();
    public void FireHandsFree(bool isEnglish) => HandsFreeToggled?.Invoke(isEnglish);
}

public sealed class FakeAudio : IAudioCaptureService
{
    public event Action<string>? DeviceChanged;
    public byte[] Wav = { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
    public byte[] Pcm = { 1, 2, 3, 4 };
    private bool _pcmHandedOut;
    public Task StartRecordingAsync(CancellationToken ct) { _pcmHandedOut = false; return Task.CompletedTask; }
    public Task<byte[]> StopRecordingAsync() => Task.FromResult(Wav);
    public byte[] SnapshotRecording() => Wav;
    // Hand out PCM once (destructive read semantics), then empty -- enough to drive the pump.
    public byte[] ReadNewPcm() { if (_pcmHandedOut) return Array.Empty<byte>(); _pcmHandedOut = true; return Pcm; }
}

// Streaming STT test double. By default returns Result from FinishAsync (simulating a working
// stream). Set FailOnStart=true to simulate the socket never opening, which drives the batch
// fallback path in the orchestrator.
public sealed class FakeStreamingStt : Kivi.Core.Stt.IStreamingSttEngine
{
    public string Result = "hello there";
    public bool FailOnStart;
    public string? LastMode;
    public bool Started, Finished, Cancelled;
    public event Action<string>? PartialReceived;

    public Task StartAsync(string mode, CancellationToken ct)
    {
        LastMode = mode;
        if (FailOnStart) throw new InvalidOperationException("stream unavailable");
        Started = true;
        return Task.CompletedTask;
    }
    public Task SendAudioAsync(byte[] pcm, CancellationToken ct)
    {
        PartialReceived?.Invoke(Result); // surface a live partial as audio flows
        return Task.CompletedTask;
    }
    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<string> FinishAsync(CancellationToken ct) { Finished = true; return Task.FromResult(Result); }
    public Task CancelAsync() { Cancelled = true; return Task.CompletedTask; }
}

// A streaming stub that opens but returns nothing from FinishAsync -- exercises the
// "stream produced empty -> batch fallback" branch.
public sealed class EmptyStreamingStt : Kivi.Core.Stt.IStreamingSttEngine
{
    public event Action<string>? PartialReceived;
    public Task StartAsync(string mode, CancellationToken ct) => Task.CompletedTask;
    public Task SendAudioAsync(byte[] pcm, CancellationToken ct) => Task.CompletedTask;
    public Task FlushAsync(CancellationToken ct) => Task.CompletedTask;
    public Task<string> FinishAsync(CancellationToken ct) => Task.FromResult("");
    public Task CancelAsync() => Task.CompletedTask;
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
    public string? Pasted; public bool PressedEnter;
    public Task InjectTextAsync(string text, bool pressEnter)
    {
        Pasted = text; PressedEnter = pressEnter;
        return Task.CompletedTask;
    }
}

public sealed class StubStt : Kivi.Core.Stt.ISttEngine
{
    public string Result = "hello there";
    public string? LastMode;
    public Task<string> TranscribeAsync(byte[] wav, string mode, CancellationToken ct)
    {
        LastMode = mode;
        return Task.FromResult(Result);
    }
}

public sealed class StubPolish : Kivi.Core.Polish.IPolishClient
{
    public event Action<string>? EnteringCooldown;
    public Task<string> CleanupAsync(string transcript, string context, CancellationToken ct)
        => Task.FromResult("Hello there.");
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
}

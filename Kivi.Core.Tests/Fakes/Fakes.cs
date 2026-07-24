using Kivi.Core.Abstractions;

public sealed class FakeHotkey : IHotkeyService
{
    public event Action? HoldStarted;
    public event Action? HoldEnded;
    public event Action? EnglishHoldStarted;
    public event Action? EnglishHoldEnded;

    public void Start() { } public void Stop() { }
    public void SetHotkey(uint virtualKeyCode) { }
    public void SetEnglishHotkey(uint virtualKeyCode) { }
    public void SetEnabled(bool enabled) { }

    public void FireStart() => HoldStarted?.Invoke();
    public void FireEnd() => HoldEnded?.Invoke();
    public void FireEnglishStart() => EnglishHoldStarted?.Invoke();
    public void FireEnglishEnd() => EnglishHoldEnded?.Invoke();
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

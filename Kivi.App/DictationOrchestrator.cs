using Kivi.Core.Contracts;

namespace Kivi.App;

/// <summary>
/// PHASE P3 (M0) — SKELETON. The single owner of one take: hotkey → capture frontmost →
/// connect KiviServiceClient → mic START/STOP → accumulate interims → final → paste. Owns
/// generation guarding and drives the orb (FlowEngine) via forwarded events. (Was OrbHost in Swift /
/// DictationController in Electron.) For now it just holds the injected seams so DI is exercised.
/// </summary>
public sealed class DictationOrchestrator
{
    private readonly IHotkeyService _hotkey;
    private readonly IAudioCapture _audio;
    private readonly IPasteService _paste;
    private readonly IFrontmostApp _frontmost;

    public DictationOrchestrator(
        IHotkeyService hotkey,
        IAudioCapture audio,
        IPasteService paste,
        IFrontmostApp frontmost)
    {
        _hotkey = hotkey;
        _audio = audio;
        _paste = paste;
        _frontmost = frontmost;
    }

    /// <summary>P3: subscribe to hotkey edges, run the classifier, drive the loop. No-op for now.</summary>
    public void Start()
    {
        _hotkey.Start(); // stub — real wiring in P3
    }
}

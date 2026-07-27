namespace Kivi.Core.Wire;

// Ported from _reference/sarvam-kivi-electron/src/main/wire/budgets.ts
// (itself from KiviKit/Dictation/DictationBudgets.swift). See docs/maps/service-client-wire.md §4.6.
// Every value here is a byte-exact protocol contract — do NOT change without the map changing.

/// <summary>Timing / backpressure budgets for the dictation WebSocket lifecycle.</summary>
public static class DictationBudgets
{
    /// <summary>Handshake budget: no <c>ack</c> within this window ⇒ take fails.</summary>
    public const int AckTimeoutMs = 4000;

    /// <summary>Budget for <c>auth_refresh_ack</c> after a mid-session <c>auth_refresh</c>.</summary>
    public const int AuthRefreshTimeoutMs = 4000;

    /// <summary>App-level keepalive cadence (a <c>{"type":"ping"}</c> text frame every interval).</summary>
    public const int PingIntervalMs = 20000;

    /// <summary>
    /// Consecutive silent ping intervals ⇒ socket declared dead.
    /// GATED on ever having received a pong — a never-ponging server is never torn down.
    /// </summary>
    public const int PongMissLimit = 2;

    /// <summary>Send-queue cap (~5 s). Past it, drop the OLDEST pending frame (backpressure).</summary>
    public const int MaxPendingAudioFrames = 50;

    /// <summary>Client waits this long for <c>final</c> after EOS (extendable on <c>eos_ack</c>).</summary>
    public const int FinalTimeoutMs = 20000;

    /// <summary>Auth re-mint fires at TTL − this many seconds (~12 min into a 15-min JWT).</summary>
    public const int AuthRefreshLeadSeconds = 180;
}

/// <summary>
/// Canonical capture/wire audio format. 16 kHz, Int16, mono, little-endian, packed PCM.
/// One ~100 ms frame per binary WS message = 1600 samples = 3200 bytes.
/// </summary>
public static class DictationAudio
{
    /// <summary>Sample rate the server hardcodes upstream (Hz).</summary>
    public const int SampleRate = 16000;

    /// <summary>Samples per ~100 ms frame.</summary>
    public const int FrameSamples = 1600;

    /// <summary>Bytes per frame: 1600 samples × 2 bytes (Int16) = 3200.</summary>
    public const int FrameBytes = 3200;
}

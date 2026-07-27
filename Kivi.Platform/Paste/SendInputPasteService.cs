using Kivi.Core.Contracts;

namespace Kivi.Platform.Paste;

/// <summary>
/// PHASE P3 (M0/M1) — STUB. Real impl: clipboard write → ~30-50ms settle → synth Ctrl+V via
/// SendInput; release held modifiers first; terminal → Ctrl+Shift+V; paste WITHOUT re-foregrounding;
/// restore clipboard after confirmed paste. Newline = literal line break, never synth-submit.
/// Secure fields → SecureFieldBlocked.
/// </summary>
public sealed class SendInputPasteService : IPasteService
{
    public Task<PasteOutcome> InsertAsync(string text, PasteMeta meta)
        => Task.FromResult(PasteOutcome.Ok); // P3
}

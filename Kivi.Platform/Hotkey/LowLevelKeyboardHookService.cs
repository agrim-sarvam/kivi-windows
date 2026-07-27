using Kivi.Core.Contracts;

namespace Kivi.Platform.Hotkey;

/// <summary>
/// PHASE P3 (M0/M1) — STUB. Real impl: WH_KEYBOARD_LL on a dedicated native thread with its own
/// message pump (a busy thread makes Windows drop the hook). Feeds GestureEdge to the pure
/// GestureClassifier (420/450/600ms). Default trigger = rebindable chord (NOT fn). Rebuilt from
/// scratch per CLAUDE.md — do NOT lift legacy code.
/// </summary>
public sealed class LowLevelKeyboardHookService : IHotkeyService
{
    public event Action<GestureEdge>? Edge;
    public void Start() { /* P3 */ }
    public void Consume(bool on) { /* P3 */ }
}

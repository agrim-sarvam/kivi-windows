using Kivi.Core.Contracts;

namespace Kivi.Platform.Overlay;

/// <summary>
/// PHASE P4 (M3) — STUB. Real impl: native Win32 layered window (UpdateLayeredWindow, premultiplied
/// ARGB) with an invisible WPF host window for lifetime; always-on-top, WS_EX_NOACTIVATE +
/// WS_EX_TOPMOST + WS_EX_TOOLWINDOW; click-through toggled by hit-testing GetCursorPos against the
/// published interactive-region rect. A WPF transparent window cannot give true non-activation +
/// per-pixel alpha (see MASTER-PLAN §2.1 and the orb-is-a-chip memo).
/// </summary>
public sealed class LayeredOrbHost : IOverlayHost
{
    public void ApplyNonActivating() { /* P4 */ }
    public void SetClickThrough(bool clickThrough) { /* P4 */ }
}

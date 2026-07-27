using Kivi.Core.Contracts;

namespace Kivi.Platform.Frontmost;

/// <summary>
/// PHASE P3 (M0/M1) — STUB. Real impl: GetForegroundWindow + GetWindowThreadProcessId +
/// QueryFullProcessImageName (exe path → app key), captured at key-down; memo last non-Kivi app.
/// </summary>
public sealed class ForegroundAppResolver : IFrontmostApp
{
    public AppTarget? Current => null; // P3
}

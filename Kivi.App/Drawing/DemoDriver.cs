using System;
using Kivi.Core.Orb;

namespace Kivi.App.Drawing;

/// <summary>
/// Demo driver — ported from src/renderer/src/orb/demoDriver.ts. Drives the engine through a full
/// dictation take with NO live service/mic, so the orb animates rest→listening→processing→done→idle
/// with a streaming transcript + red→green diff. Reuses the engine's DEFAULT demo services
/// (DemoDictationService/DemoEditService, constructed when none are injected).
/// </summary>
public static class DemoDriver
{
    private const double Period = 16000;
    private const double DownAt = 600;
    private const double UpAt = 6600;
    private const double MicEnd = 6900;

    public static Action<double> Install(FlowEngine engine, FlowSettings? settings = null)
    {
        engine.Apply(settings ?? FlowSettings.Default());
        engine.SetExpanded(true); // box live → take streams into the transcript

        double cycleStart = -1;
        bool downFired = false, upFired = false;

        return now =>
        {
            if (cycleStart < 0) cycleStart = now;
            double t = now - cycleStart;
            if (t >= Period)
            {
                cycleStart = now; t = 0; downFired = false; upFired = false;
            }
            if (!downFired && t >= DownAt) { engine.OrbPointerDown(); downFired = true; }
            if (!upFired && t >= UpAt) { engine.PointerUp(); upFired = true; }

            bool listening = t >= DownAt && t < MicEnd;
            engine.MicLevel = listening
                ? 0.12 + 0.55 * (0.5 + 0.5 * Math.Sin(now / 210.0)) * (0.55 + 0.45 * Math.Sin(now / 1600.0))
                : 0;
        };
    }
}

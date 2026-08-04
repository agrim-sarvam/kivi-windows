using System;
using System.Collections.Generic;
using Kivi.Core.Observability;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>Verifies the kivi-logs summary text — the pure rendering the console command prints.</summary>
public class ObservationPrinterTests
{
    [Fact]
    public void Render_Null_ShowsEmptyGuidance()
    {
        var text = ObservationPrinter.Render(null);
        Assert.Contains("No observations yet", text);
        Assert.Contains("make sure the app is running", text);
    }

    [Fact]
    public void Render_Snapshot_IncludesAllFourMetricsAndTakes()
    {
        var start = new DateTime(2026, 8, 4, 8, 0, 0, DateTimeKind.Utc);
        var snap = new ObservationSnapshot(
            GeneratedUtc: start.AddMinutes(30),
            StartedUtc: start,
            CpuCurrentPercent: 3.2,
            CpuPeakPercent: 21.7,
            MemCurrentBytes: 189_000_000,
            MemPeakBytes: 245_000_000,
            TakeCount: 4,
            ErrorCount: 1,
            AvgTtftMs: 640,
            AvgLatencyMs: 2100,
            P50TtftMs: 600,
            P50LatencyMs: 1950,
            RecentTakes: new List<TakeObservation>
            {
                new(start.AddMinutes(29), 720, 2400, 12, "Code", null),
                new(start.AddMinutes(20), null, null, 0, "chrome", "SERVICE_BUSY"),
            });

        var text = ObservationPrinter.Render(snap);

        // The four requested observations are all present.
        Assert.Contains("CPU", text);
        Assert.Contains("21.7%", text);          // CPU peak
        Assert.Contains("Memory", text);
        Assert.Contains("MB", text);             // memory footprint formatted
        Assert.Contains("TTFT", text);
        Assert.Contains("Latency", text);
        // Counts + a take row + the error surfacing.
        Assert.Contains("Takes      4", text);
        Assert.Contains("Code", text);
        Assert.Contains("error: SERVICE_BUSY", text);
    }
}

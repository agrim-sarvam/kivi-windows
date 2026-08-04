using System;
using System.Collections.Generic;
using System.Linq;

namespace Kivi.Core.Observability;

/// <summary>
/// One dictation take's measured timings. TTFT = key-release → first interim/transcript word back
/// from the service. Latency = key-release → final formatted text pasted into the target app. Either
/// may be null if that stage never happened (e.g. an errored take that produced no interim).
/// </summary>
public sealed record TakeObservation(
    DateTime WhenUtc,
    double? TtftMs,
    double? LatencyMs,
    int WordCount,
    string? AppName,
    string? Error);

/// <summary>A periodic resource sample of the Kivi process.</summary>
public readonly record struct ResourceSample(double CpuPercent, long WorkingSetBytes);

/// <summary>
/// The computed observation snapshot the `kivi-logs` command prints. Pure data — produced by
/// <see cref="ObservationAggregator.Snapshot"/> from accumulated samples + takes.
/// </summary>
public sealed record ObservationSnapshot(
    DateTime GeneratedUtc,
    DateTime StartedUtc,
    // resources
    double CpuCurrentPercent,
    double CpuPeakPercent,
    long MemCurrentBytes,
    long MemPeakBytes,
    // dictation
    int TakeCount,
    int ErrorCount,
    double? AvgTtftMs,
    double? AvgLatencyMs,
    double? P50TtftMs,
    double? P50LatencyMs,
    IReadOnlyList<TakeObservation> RecentTakes);

/// <summary>
/// Pure, thread-agnostic accumulator behind the observation center. The App layer feeds it resource
/// samples (from a background timer) and completed-take observations (from the dictation loop); it
/// keeps the running peaks + a bounded recent-takes ring and can produce an <see cref="ObservationSnapshot"/>
/// at any time. All averaging/percentile math lives here so it can be unit-tested without any OS or
/// process dependency.
///
/// <para>Not internally locked — the recorder that owns it serializes access (single writer for
/// samples/takes, single reader for Snapshot). Kept simple on purpose.</para>
/// </summary>
public sealed class ObservationAggregator
{
    private const int MaxRecentTakes = 50;

    private readonly DateTime _startedUtc;
    private readonly Func<DateTime> _clock;
    private readonly List<TakeObservation> _takes = new();

    private double _cpuCurrent;
    private double _cpuPeak;
    private long _memCurrent;
    private long _memPeak;
    private int _errorCount;
    // Lifetime count of takes ever observed this session (the recent-takes list is a bounded ring, so
    // its Count under-reports once we've done more than MaxRecentTakes takes).
    private int _lifetimeTakeCount;

    /// <param name="startedUtc">When observation began (process start).</param>
    /// <param name="clock">Injectable "now" for deterministic tests; defaults to UTC now.</param>
    public ObservationAggregator(DateTime startedUtc, Func<DateTime>? clock = null)
    {
        _startedUtc = startedUtc;
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    public void AddSample(ResourceSample s)
    {
        _cpuCurrent = s.CpuPercent;
        if (s.CpuPercent > _cpuPeak) _cpuPeak = s.CpuPercent;
        _memCurrent = s.WorkingSetBytes;
        if (s.WorkingSetBytes > _memPeak) _memPeak = s.WorkingSetBytes;
    }

    public void AddTake(TakeObservation take)
    {
        _takes.Add(take);
        _lifetimeTakeCount++;
        if (!string.IsNullOrEmpty(take.Error)) _errorCount++;
        if (_takes.Count > MaxRecentTakes) _takes.RemoveRange(0, _takes.Count - MaxRecentTakes);
    }

    public ObservationSnapshot Snapshot()
    {
        var ttfts = _takes.Where(t => t.TtftMs.HasValue).Select(t => t.TtftMs!.Value).ToList();
        var lats = _takes.Where(t => t.LatencyMs.HasValue).Select(t => t.LatencyMs!.Value).ToList();

        // Newest-first for display.
        var recent = _takes.AsEnumerable().Reverse().ToList();

        return new ObservationSnapshot(
            GeneratedUtc: _clock(),
            StartedUtc: _startedUtc,
            CpuCurrentPercent: _cpuCurrent,
            CpuPeakPercent: _cpuPeak,
            MemCurrentBytes: _memCurrent,
            MemPeakBytes: _memPeak,
            TakeCount: _lifetimeTakeCount,
            ErrorCount: _errorCount,
            AvgTtftMs: Avg(ttfts),
            AvgLatencyMs: Avg(lats),
            P50TtftMs: Median(ttfts),
            P50LatencyMs: Median(lats),
            RecentTakes: recent);
    }

    private static double? Avg(List<double> xs) => xs.Count == 0 ? null : xs.Average();

    private static double? Median(List<double> xs)
    {
        if (xs.Count == 0) return null;
        var sorted = xs.OrderBy(x => x).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2.0;
    }
}

using System;
using Kivi.Core.Observability;
using Xunit;

namespace Kivi.Core.Tests;

public class ObservationAggregatorTests
{
    private static readonly DateTime Start = new(2026, 8, 4, 10, 0, 0, DateTimeKind.Utc);
    private static ObservationAggregator New() => new(Start, () => Start.AddMinutes(5));

    [Fact]
    public void EmptySnapshot_HasNoTakesAndNullAverages()
    {
        var snap = New().Snapshot();
        Assert.Equal(0, snap.TakeCount);
        Assert.Equal(0, snap.ErrorCount);
        Assert.Null(snap.AvgTtftMs);
        Assert.Null(snap.AvgLatencyMs);
        Assert.Null(snap.P50TtftMs);
        Assert.Empty(snap.RecentTakes);
        Assert.Equal(Start, snap.StartedUtc);
        Assert.Equal(Start.AddMinutes(5), snap.GeneratedUtc);
    }

    [Fact]
    public void Resource_TracksCurrentAndPeak()
    {
        var a = New();
        a.AddSample(new ResourceSample(10, 100_000));
        a.AddSample(new ResourceSample(40, 500_000));
        a.AddSample(new ResourceSample(15, 200_000)); // current drops, peak stays
        var s = a.Snapshot();
        Assert.Equal(15, s.CpuCurrentPercent);
        Assert.Equal(40, s.CpuPeakPercent);
        Assert.Equal(200_000, s.MemCurrentBytes);
        Assert.Equal(500_000, s.MemPeakBytes);
    }

    [Fact]
    public void Takes_AverageAndMedian_OverPresentValuesOnly()
    {
        var a = New();
        a.AddTake(new TakeObservation(Start, TtftMs: 100, LatencyMs: 1000, WordCount: 5, AppName: "notepad", Error: null));
        a.AddTake(new TakeObservation(Start, TtftMs: 200, LatencyMs: 3000, WordCount: 9, AppName: "code", Error: null));
        a.AddTake(new TakeObservation(Start, TtftMs: 300, LatencyMs: 2000, WordCount: 3, AppName: "chrome", Error: null));
        var s = a.Snapshot();
        Assert.Equal(3, s.TakeCount);
        Assert.Equal(200, s.AvgTtftMs);          // (100+200+300)/3
        Assert.Equal(2000, s.AvgLatencyMs);      // (1000+3000+2000)/3
        Assert.Equal(200, s.P50TtftMs);          // median of 100,200,300
        Assert.Equal(2000, s.P50LatencyMs);      // median of 1000,2000,3000
    }

    [Fact]
    public void Median_EvenCount_AveragesMiddleTwo()
    {
        var a = New();
        a.AddTake(new TakeObservation(Start, 100, null, 1, null, null));
        a.AddTake(new TakeObservation(Start, 300, null, 1, null, null));
        Assert.Equal(200, a.Snapshot().P50TtftMs); // (100+300)/2
    }

    [Fact]
    public void NullTimings_AreExcludedFromAverages()
    {
        var a = New();
        a.AddTake(new TakeObservation(Start, TtftMs: null, LatencyMs: null, WordCount: 0, AppName: null, Error: "SERVICE_BUSY"));
        a.AddTake(new TakeObservation(Start, TtftMs: 150, LatencyMs: 900, WordCount: 4, AppName: "x", Error: null));
        var s = a.Snapshot();
        Assert.Equal(2, s.TakeCount);
        Assert.Equal(1, s.ErrorCount);
        Assert.Equal(150, s.AvgTtftMs);   // only the one present value
        Assert.Equal(900, s.AvgLatencyMs);
    }

    [Fact]
    public void RecentTakes_AreNewestFirst()
    {
        var a = New();
        a.AddTake(new TakeObservation(Start, 1, 1, 1, "first", null));
        a.AddTake(new TakeObservation(Start, 2, 2, 2, "second", null));
        var recent = a.Snapshot().RecentTakes;
        Assert.Equal("second", recent[0].AppName);
        Assert.Equal("first", recent[1].AppName);
    }

    [Fact]
    public void RecentTakes_AreCappedAt50_KeepingLatest()
    {
        var a = New();
        for (int i = 0; i < 60; i++)
            a.AddTake(new TakeObservation(Start, i, i, i, "app" + i, null));
        var s = a.Snapshot();
        Assert.Equal(50, s.RecentTakes.Count);   // ring capped
        Assert.Equal(60, s.TakeCount);           // but total count still reflects all seen
        Assert.Equal("app59", s.RecentTakes[0].AppName); // newest kept
        Assert.Equal("app10", s.RecentTakes[^1].AppName); // oldest kept is #10 (0..9 dropped)
    }

    [Fact]
    public void ErrorCount_CountsOnlyErroredTakes()
    {
        var a = New();
        a.AddTake(new TakeObservation(Start, 1, 1, 1, "a", null));
        a.AddTake(new TakeObservation(Start, null, null, 0, "b", "TIMEOUT"));
        a.AddTake(new TakeObservation(Start, null, null, 0, "c", "SERVICE_BUSY"));
        Assert.Equal(2, a.Snapshot().ErrorCount);
    }
}

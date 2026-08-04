using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using Kivi.Core.Observability;

namespace Kivi.App.Services;

/// <summary>
/// The app-side observation recorder: owns a pure <see cref="ObservationAggregator"/>, samples THIS
/// process's CPU% and working-set memory on a background timer, records each completed dictation
/// take's TTFT + end-to-end latency, and writes a snapshot to
/// <c>%APPDATA%\Kivi\observations.json</c> which the <c>kivi-logs</c> command reads.
///
/// <para>CPU% is a delta measurement: (Δ total processor time / Δ wall-clock) / logical-core-count,
/// so a fully-busy single core on an 8-core box reads ~12.5%, and "100%" means all cores saturated —
/// the same convention Task Manager uses.</para>
///
/// <para>Thread-safety: the aggregator is guarded by <see cref="_gate"/>; the sampling timer, the
/// dictation thread (AddTake), and the writer all take it. Sampling + writes are best-effort — any
/// failure is swallowed so observation can never disturb the dictation loop.</para>
/// </summary>
public sealed class ObservationRecorder : IDisposable
{
    private readonly object _gate = new();
    private readonly ObservationAggregator _agg;
    private readonly string _filePath;
    private readonly Process _proc;
    private readonly int _cpuCount;
    private readonly System.Threading.Timer _timer;

    private TimeSpan _lastCpuTotal;
    private DateTime _lastCpuStamp;
    private volatile bool _disposed;

    public ObservationRecorder() : this(DefaultFilePath()) { }

    public ObservationRecorder(string filePath)
    {
        _filePath = filePath;
        _proc = Process.GetCurrentProcess();
        _cpuCount = Math.Max(1, Environment.ProcessorCount);
        _agg = new ObservationAggregator(DateTime.UtcNow);

        _lastCpuTotal = SafeTotalProcessorTime();
        _lastCpuStamp = DateTime.UtcNow;

        // Sample every 2s. First tick after 2s so the CPU delta has a real window.
        _timer = new System.Threading.Timer(_ => Sample(), null, dueTime: 2000, period: 2000);
    }

    private static string DefaultFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi");
        return Path.Combine(dir, "observations.json");
    }

    /// <summary>Record one completed take. Safe to call from any thread.</summary>
    public void RecordTake(TakeObservation take)
    {
        if (_disposed) return;
        lock (_gate) _agg.AddTake(take);
        Write(); // persist promptly so a `kivi-logs` right after a take reflects it
    }

    private void Sample()
    {
        if (_disposed) return;
        try
        {
            _proc.Refresh();
            var now = DateTime.UtcNow;
            var total = SafeTotalProcessorTime();

            double cpuPct = 0;
            var wallMs = (now - _lastCpuStamp).TotalMilliseconds;
            if (wallMs > 0)
            {
                var cpuMs = (total - _lastCpuTotal).TotalMilliseconds;
                cpuPct = Math.Max(0, Math.Min(100, (cpuMs / (wallMs * _cpuCount)) * 100.0));
            }
            _lastCpuTotal = total;
            _lastCpuStamp = now;

            long mem = _proc.WorkingSet64;

            lock (_gate) _agg.AddSample(new ResourceSample(cpuPct, mem));
            Write();
        }
        catch
        {
            // Sampling must never throw into the timer / disturb the app.
        }
    }

    private TimeSpan SafeTotalProcessorTime()
    {
        try { return _proc.TotalProcessorTime; }
        catch { return _lastCpuTotal; }
    }

    private void Write()
    {
        ObservationSnapshot snap;
        lock (_gate) snap = _agg.Snapshot();
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(snap, ObservationJson.Options);
            var tmp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
            else File.Move(tmp, _filePath);
        }
        catch { /* best-effort */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Dispose();
        try { Write(); } catch { }
        _proc.Dispose();
    }
}

/// <summary>Shared JSON options + a loader so the recorder (writer) and the --logs printer (reader)
/// agree on the on-disk shape of <see cref="ObservationSnapshot"/>.</summary>
public static class ObservationJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DefaultFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi");
        return Path.Combine(dir, "observations.json");
    }

    public static ObservationSnapshot? TryLoad(string? path = null)
    {
        try
        {
            path ??= DefaultFilePath();
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<ObservationSnapshot>(json, Options);
        }
        catch { return null; }
    }
}

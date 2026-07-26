using System.Diagnostics.Metrics;

namespace Kivi.Core.Diagnostics;

public sealed class KiviMetrics : IDisposable
{
    public const string MeterName = "Kivi";
    private readonly Meter _meter = new(MeterName);
    private readonly Histogram<double> _stage;
    private readonly Histogram<double> _total;

    public KiviMetrics()
    {
        _stage = _meter.CreateHistogram<double>("kivi.dictation.stage.duration", unit: "ms");
        _total = _meter.CreateHistogram<double>("kivi.dictation.total.duration", unit: "ms");
    }

    // Temporary: mirror stage timings into %APPDATA%\Kivi\stream-debug.log when the opt-in flag
    // file exists, so end-to-end latency can be broken down per stage. Remove with the streaming
    // debug logging once latency is dialed in.
    private static readonly bool DebugEnabled =
        File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi", "stream-debug.on"));
    private static readonly string DebugLogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi", "stream-debug.log");
    private static void Log(string line)
    {
        if (!DebugEnabled) return;
        try { File.AppendAllText(DebugLogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}\n"); } catch { }
    }

    public void RecordStage(string stage, double ms)
    {
        Log($"STAGE {stage} {ms:F0}ms");
        _stage.Record(ms, new KeyValuePair<string, object?>("stage", stage));
    }
    public void RecordTotal(double ms)
    {
        Log($"TOTAL {ms:F0}ms");
        _total.Record(ms);
    }
    public Meter Meter => _meter;
    public void Dispose() => _meter.Dispose();
}

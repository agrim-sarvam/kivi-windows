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

    public void RecordStage(string stage, double ms) => _stage.Record(ms, new KeyValuePair<string, object?>("stage", stage));
    public void RecordTotal(double ms) => _total.Record(ms);
    public Meter Meter => _meter;
    public void Dispose() => _meter.Dispose();
}

using System.Diagnostics.Metrics;
using Kivi.Core.Diagnostics;
using Xunit;

public class KiviMetricsTests
{
    [Fact]
    public void RecordStage_EmitsMeasurement_OnKiviMeter()
    {
        using var metrics = new KiviMetrics();
        double captured = -1;
        string? capturedStage = null;
        using var listener = new MeterListener();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter.Name == KiviMetrics.MeterName && inst.Name == "kivi.dictation.stage.duration")
                l.EnableMeasurementEvents(inst);
        };
        listener.SetMeasurementEventCallback<double>((inst, value, tags, state) =>
        {
            captured = value;
            foreach (var t in tags) if (t.Key == "stage") capturedStage = t.Value?.ToString();
        });
        listener.Start();

        metrics.RecordStage("stt", 620);

        Assert.Equal(620, captured);
        Assert.Equal("stt", capturedStage);
    }
}

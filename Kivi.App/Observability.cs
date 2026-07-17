using Kivi.Core.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace Kivi.App;

public static class Observability
{
    public static IDisposable? Start(bool enabled, KiviMetrics metrics)
    {
        if (!enabled) return null;
        var sampler = new ProcessSampler(metrics, TimeSpan.FromSeconds(2));
        var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(KiviMetrics.MeterName)
            .AddRuntimeInstrumentation()
            .AddConsoleExporter()
            .Build();
        return new CompositeDisposable(sampler, provider);
    }

    private sealed class CompositeDisposable(params IDisposable?[] items) : IDisposable
    { public void Dispose() { foreach (var i in items) i?.Dispose(); } }
}

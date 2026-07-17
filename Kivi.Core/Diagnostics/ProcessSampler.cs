using System.Diagnostics;

namespace Kivi.Core.Diagnostics;

public sealed class ProcessSampler : IDisposable
{
    private readonly Process _proc = Process.GetCurrentProcess();
    private DateTime _lastSample = DateTime.UtcNow;
    private TimeSpan _lastCpu;
    private double _rssMb;
    private double _cpuPercent;
    private readonly Timer _timer;

    public ProcessSampler(KiviMetrics metrics, TimeSpan interval)
    {
        _lastCpu = _proc.TotalProcessorTime;
        metrics.Meter.CreateObservableGauge("kivi.process.rss", () => _rssMb, unit: "MB");
        metrics.Meter.CreateObservableGauge("kivi.process.cpu", () => _cpuPercent, unit: "%");
        _timer = new Timer(_ => Sample(), null, TimeSpan.Zero, interval);
    }

    private void Sample()
    {
        _proc.Refresh();
        _rssMb = _proc.WorkingSet64 / (1024.0 * 1024.0);
        var now = DateTime.UtcNow;
        var cpu = _proc.TotalProcessorTime;
        var wall = (now - _lastSample).TotalMilliseconds;
        if (wall > 0)
            _cpuPercent = (cpu - _lastCpu).TotalMilliseconds / (wall * Environment.ProcessorCount) * 100.0;
        _lastSample = now;
        _lastCpu = cpu;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _proc.Dispose();
    }
}

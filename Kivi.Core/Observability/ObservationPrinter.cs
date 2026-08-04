using System;
using System.Globalization;
using System.Text;

namespace Kivi.Core.Observability;

/// <summary>
/// Formats an <see cref="ObservationSnapshot"/> into the readable text block the <c>kivi-logs</c>
/// command prints. Pure string-building — no console/IO — so it can be unit-tested and reused. Lives
/// in Kivi.Core (OS-free) alongside the snapshot type; the App layer only handles console plumbing.
/// </summary>
public static class ObservationPrinter
{
    public static string Render(ObservationSnapshot? snap)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("  ==================================================");
        sb.AppendLine("                KIVI - OBSERVATIONS");
        sb.AppendLine("  ==================================================");
        sb.AppendLine();

        if (snap is null)
        {
            sb.AppendLine("  No observations yet.");
            sb.AppendLine();
            sb.AppendLine("  Kivi hasn't recorded anything — make sure the app is running,");
            sb.AppendLine("  then do a dictation or two and run this again.");
            sb.AppendLine();
            return sb.ToString();
        }

        var genLocal = snap.GeneratedUtc.ToLocalTime();
        var startLocal = snap.StartedUtc.ToLocalTime();
        var uptime = snap.GeneratedUtc - snap.StartedUtc;

        sb.AppendLine($"  Snapshot at   {genLocal:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"  Session up    {FormatDuration(uptime)}  (since {startLocal:HH:mm:ss})");
        sb.AppendLine();

        sb.AppendLine("  RESOURCES (Kivi process)");
        sb.AppendLine($"    CPU        {snap.CpuCurrentPercent,6:0.0}%   (peak {snap.CpuPeakPercent:0.0}%)");
        sb.AppendLine($"    Memory     {FormatBytes(snap.MemCurrentBytes),8}   (peak {FormatBytes(snap.MemPeakBytes)})");
        sb.AppendLine();

        sb.AppendLine("  DICTATION");
        sb.AppendLine($"    Takes      {snap.TakeCount}   Errors {snap.ErrorCount}");
        sb.AppendLine($"    TTFT       avg {Ms(snap.AvgTtftMs)}   p50 {Ms(snap.P50TtftMs)}   (release -> first word)");
        sb.AppendLine($"    Latency    avg {Ms(snap.AvgLatencyMs)}   p50 {Ms(snap.P50LatencyMs)}   (release -> pasted)");
        sb.AppendLine();

        if (snap.RecentTakes.Count > 0)
        {
            sb.AppendLine("  RECENT TAKES (newest first)");
            sb.AppendLine("    time      app                 words   ttft     latency");
            sb.AppendLine("    --------  ------------------  -----  -------  --------");
            int shown = 0;
            foreach (var t in snap.RecentTakes)
            {
                if (shown++ >= 12) break;
                var when = t.WhenUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                var app = Trunc(t.AppName ?? "-", 18).PadRight(18);
                if (t.Error is not null)
                    sb.AppendLine($"    {when}  {app}      -   error: {t.Error}");
                else
                    sb.AppendLine($"    {when}  {app}  {t.WordCount,4}   {Ms(t.TtftMs),7}  {Ms(t.LatencyMs),8}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string Ms(double? ms) => ms is null ? "-" : $"{ms.Value:0} ms";

    private static string FormatBytes(long bytes)
    {
        double mb = bytes / (1024.0 * 1024.0);
        if (mb >= 1024) return $"{mb / 1024.0:0.00} GB";
        return $"{mb:0} MB";
    }

    private static string FormatDuration(TimeSpan t)
    {
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours}h {t.Minutes}m";
        if (t.TotalMinutes >= 1) return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "~";
}

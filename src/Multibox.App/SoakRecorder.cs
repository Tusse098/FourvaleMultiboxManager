using System.Globalization;
using System.IO;
using System.Text;
using Multibox.Core;
using Multibox.Hosting;

namespace Multibox.App;

/// <summary>
/// Appends one CSV row per slot at a fixed interval, as evidence for the Phase 3 soak test
/// (spec §15 criteria 2, 5, 8). Contains states and counts only: no names, no game data.
/// </summary>
public sealed class SoakRecorder : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly DateTimeOffset _startedAt;

    public SoakRecorder(string folder, DateTimeOffset startedAt)
    {
        Directory.CreateDirectory(folder);
        _startedAt = startedAt;
        FilePath = Path.Combine(folder, $"soak-{startedAt.ToLocalTime():yyyyMMdd-HHmmss}.csv");
        _writer = new StreamWriter(new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false))
        {
            AutoFlush = true,
        };
        _writer.WriteLine("time,uptime_min,slot,browser,connection,health,unexpected_disconnects,recoveries,decode_errors,updates_per_s," +
                          "class_fresh,level_fresh,isolation_violations,browser_processes,browser_mb,browser_private_mb,browser_cpu_pct");
    }

    public string FilePath { get; }

    public void Write(
        DateTimeOffset now,
        IEnumerable<(SlotState State, int Recoveries)> slots,
        FreshnessPolicy freshness,
        int isolationViolations,
        BrowserProcessMetrics.Snapshot? metrics)
    {
        foreach (var (state, recoveries) in slots)
        {
            var c = state.Character;
            _writer.WriteLine(string.Join(',',
                now.ToLocalTime().ToString("O", CultureInfo.InvariantCulture),
                ((now - _startedAt).TotalMinutes).ToString("0.0", CultureInfo.InvariantCulture),
                state.Id,
                state.Browser,
                state.Connection,
                state.Health,
                state.UnexpectedDisconnects,
                recoveries,
                state.DecodeErrors,
                state.UpdatesPerSecond.ToString("0.0", CultureInfo.InvariantCulture),
                freshness.IsFresh(c.Class, now) ? 1 : 0,
                freshness.IsFresh(c.Level, now) ? 1 : 0,
                isolationViolations,
                metrics?.Processes.ToString(CultureInfo.InvariantCulture) ?? "",
                metrics is null ? "" : (metrics.WorkingSetBytes / (1024.0 * 1024)).ToString("0", CultureInfo.InvariantCulture),
                metrics is null ? "" : (metrics.PrivateBytes / (1024.0 * 1024)).ToString("0", CultureInfo.InvariantCulture),
                metrics?.CpuPercent.ToString("0.0", CultureInfo.InvariantCulture) ?? ""));
        }
    }

    public void Dispose() => _writer.Dispose();
}

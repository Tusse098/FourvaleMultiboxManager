using System.Diagnostics;
using Microsoft.Web.WebView2.Core;

namespace Multibox.Hosting;

/// <summary>
/// Memory and CPU of the WebView2 browser process tree, for the resource measurements in spec §15 (criterion 8).
/// Call <see cref="Sample"/> periodically; CPU is averaged over the interval since the previous sample.
/// </summary>
public sealed class BrowserProcessMetrics(CoreWebView2Environment environment)
{
    private readonly Dictionary<int, TimeSpan> _lastCpu = [];
    private DateTimeOffset? _lastSample;

    /// <param name="WorkingSetBytes">Sum of working sets; overstates use because shared pages count once per process.</param>
    /// <param name="PrivateBytes">Sum of private (committed, unshared) memory: the figure to budget against.</param>
    public sealed record Snapshot(int Processes, long WorkingSetBytes, long PrivateBytes, double CpuPercent);

    public Snapshot Sample()
    {
        var now = DateTimeOffset.UtcNow;
        long workingSet = 0;
        long privateBytes = 0;
        var cpu = TimeSpan.Zero;
        var count = 0;
        var seen = new Dictionary<int, TimeSpan>();

        foreach (var info in environment.GetProcessInfos())
        {
            try
            {
                using var process = Process.GetProcessById(info.ProcessId);
                workingSet += process.WorkingSet64;
                privateBytes += process.PrivateMemorySize64;
                var total = process.TotalProcessorTime;
                seen[info.ProcessId] = total;
                cpu += total - _lastCpu.GetValueOrDefault(info.ProcessId, total);
                count++;
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // The process exited between listing and reading; skip it.
            }
        }

        var elapsed = _lastSample is { } last ? (now - last).TotalMilliseconds : 0;
        var percent = elapsed > 0 ? cpu.TotalMilliseconds / elapsed / Environment.ProcessorCount * 100 : 0;

        _lastCpu.Clear();
        foreach (var (id, total) in seen)
        {
            _lastCpu[id] = total;
        }

        _lastSample = now;
        return new Snapshot(count, workingSet, privateBytes, percent);
    }
}

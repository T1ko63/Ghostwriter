using System.Diagnostics;
using System.Windows.Threading;
using Ghostwriter.Core.Diagnostics;

namespace Ghostwriter.App.Diagnostics;

/// <summary>
/// Development aid, off unless GHOSTWRITER_DIAG=1: logs process memory and GC activity at an interval, so memory
/// behaviour can be judged from the log (is it a leak, or just a generous GC budget?).
/// </summary>
public static class MemoryDiagnostics
{
    private static DispatcherTimer? _timer;

    public static void StartIfRequested()
    {
        if (Environment.GetEnvironmentVariable("GHOSTWRITER_DIAG") != "1") return;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => Log();
        _timer.Start();
        Log();
    }

    private static void Log()
    {
        using var process = Process.GetCurrentProcess();
        var info = GC.GetGCMemoryInfo();
        AppLog.Info($"memory: working set {process.WorkingSet64 / 1048576.0:F0} MB, private {process.PrivateMemorySize64 / 1048576.0:F0} MB, "
            + $"managed heap {GC.GetTotalMemory(false) / 1048576.0:F1} MB (committed {info.TotalCommittedBytes / 1048576.0:F0} MB), "
            + $"GCs gen0/1/2 = {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, threads {process.Threads.Count}");
    }
}

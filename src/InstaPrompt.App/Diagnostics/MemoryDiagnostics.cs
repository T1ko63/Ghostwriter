using System.Diagnostics;
using System.Windows.Threading;
using InstaPrompt.Core.Diagnostics;

namespace InstaPrompt.App.Diagnostics;

/// <summary>
/// Development aid, off unless INSTAPROMPT_DIAG=1: logs process memory and GC activity at an interval, so memory
/// behaviour can be judged from the log (is it a leak, or just a generous GC budget?).
/// </summary>
public static class MemoryDiagnostics
{
    private static DispatcherTimer? _timer;

    public static void StartIfRequested()
    {
        if (Environment.GetEnvironmentVariable("INSTAPROMPT_DIAG") != "1") return;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += (_, _) => Log();
        _timer.Start();
        Log();
    }

    /// <summary>Forces a full collection and logs what is really still alive afterwards.</summary>
    public static void LogAfterCollect(string label)
    {
        GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        Log($"{label} (after full GC)");
    }

    private static void Log(string label = "memory")
    {
        using var process = Process.GetCurrentProcess();
        var info = GC.GetGCMemoryInfo();
        AppLog.Info($"{label}: working set {process.WorkingSet64 / 1048576.0:F0} MB, private {process.PrivateMemorySize64 / 1048576.0:F0} MB, "
            + $"managed heap {GC.GetTotalMemory(false) / 1048576.0:F1} MB (committed {info.TotalCommittedBytes / 1048576.0:F0} MB), "
            + $"GCs gen0/1/2 = {GC.CollectionCount(0)}/{GC.CollectionCount(1)}/{GC.CollectionCount(2)}, threads {process.Threads.Count}");
    }
}

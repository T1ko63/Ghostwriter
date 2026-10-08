using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

namespace Ghostwriter.Platform.Native;

/// <summary>
/// Gives back the pages of the working set that are not needed right now (they stay available and come back from
/// memory on demand). For a tool that sits idle most of the day this keeps its footprint in Task Manager small.
/// </summary>
public static class WorkingSetTrimmer
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(nint hProcess);

    /// <summary>Collects garbage, then trims. Returns working set before and after in MB.</summary>
    public static (double BeforeMb, double AfterMb) Trim()
    {
        using var process = Process.GetCurrentProcess();
        var before = process.WorkingSet64 / 1048576.0;

        // "Aggressive" also hands free heap regions back to Windows (a plain collection keeps them reserved).
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(2, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        EmptyWorkingSet(process.Handle);

        process.Refresh();
        return (before, process.WorkingSet64 / 1048576.0);
    }
}

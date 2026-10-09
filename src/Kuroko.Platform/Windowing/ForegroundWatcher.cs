using static Kuroko.Platform.Native.NativeMethods;

namespace Kuroko.Platform.Windowing;

/// <summary>
/// Reports when another window becomes the foreground window. A WinEvent hook that exists only between
/// <see cref="Start"/> and <see cref="Dispose"/> (while the result card is visible); nothing runs otherwise.
/// Out-of-context: the callback is delivered on the thread that called <see cref="Start"/> (the UI thread, which pumps messages).
/// </summary>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly WinEventProc _callback; // kept in a field so the delegate is not collected while the hook lives
    private readonly nint _baseline;
    private readonly Action _changed;
    private nint _hook;

    private ForegroundWatcher(nint baseline, Action changed)
    {
        _baseline = baseline;
        _changed = changed;
        _callback = OnEvent;
    }

    /// <summary>
    /// Starts watching. <paramref name="changed"/> is called (once per event) when the foreground window is no longer
    /// <paramref name="baseline"/>; it is also called right away if that already is the case when the hook is installed.
    /// Returns null if the hook could not be installed (the card then simply stays until closed).
    /// </summary>
    public static ForegroundWatcher? Start(nint baseline, Action changed)
    {
        var watcher = new ForegroundWatcher(baseline, changed);
        watcher._hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 0, watcher._callback, 0, 0, WINEVENT_OUTOFCONTEXT);
        if (watcher._hook == 0) return null;

        // The window may have changed between reading the text and installing the hook.
        var current = GetForegroundWindow();
        if (current != 0 && current != baseline) changed();
        return watcher;
    }

    private void OnEvent(nint hook, uint eventType, nint hwnd, int idObject, int idChild, uint thread, uint time)
    {
        // hwnd 0 happens while the foreground window is being destroyed; the next event names the new one.
        if (hwnd != 0 && hwnd != _baseline) _changed();
    }

    public void Dispose()
    {
        var hook = _hook;
        _hook = 0;
        if (hook != 0) UnhookWinEvent(hook);
    }
}

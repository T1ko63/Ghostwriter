namespace Kuroko.Platform.Native;

/// <summary>
/// Raises the system timer resolution to 1 ms so short Task.Delay/wait calls are not rounded up to ~15 ms. Only
/// needed while a hotkey is being handled; keeping it raised while the app idles in the tray would cost energy.
/// Call from the UI thread only.
/// </summary>
public static class NativeTimer
{
    private static bool _high;

    public static void SetHighResolution(bool on)
    {
        if (on == _high) return;
        _high = on;
        if (on) _ = NativeMethods.timeBeginPeriod(1); // best effort: without it waits are only coarser
        else _ = NativeMethods.timeEndPeriod(1);
    }
}

namespace Ghostwriter.Platform.Native;

/// <summary>Raises the system timer resolution to 1 ms so short Task.Delay/wait calls are not rounded up to ~15 ms.</summary>
public static class NativeTimer
{
    public static void BeginHighResolution() => NativeMethods.timeBeginPeriod(1);

    public static void EndHighResolution() => NativeMethods.timeEndPeriod(1);
}

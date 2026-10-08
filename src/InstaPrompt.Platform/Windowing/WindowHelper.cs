using System.Runtime.InteropServices;
using InstaPrompt.Platform.TextIntegration;
using static InstaPrompt.Platform.Native.NativeMethods;

namespace InstaPrompt.Platform.Windowing;

public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;
}

public readonly record struct MonitorArea(PxRect WorkArea, double Scale);

/// <summary>Win32 helpers for the overlay windows: styles, DWM look, monitor geometry, placement.</summary>
public static class WindowHelper
{
    private const int GWL_EXSTYLE = -20;
    private const nint WS_EX_NOACTIVATE = 0x08000000;
    private const nint WS_EX_TOOLWINDOW = 0x00000080;
    private const uint SWP_NOSIZE = 0x1, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    private const nint HWND_TOPMOST = -1;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_TRANSIENTWINDOW = 3;
    private const int DWMWCP_ROUND = 2;

    /// <summary>Keeps the window out of Alt+Tab and, optionally, makes it never take the focus (status pill).</summary>
    public static void SetToolWindow(nint hwnd, bool noActivate)
    {
        var style = GetWindowLongPtr(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW;
        if (noActivate) style |= WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, style);
    }

    /// <summary>The system backdrop attribute exists from Windows 11 22H2 (build 22621).</summary>
    public static bool IsBackdropSupported { get; } = Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// Win11 rounded corners, themed border and dark caption/shadow colours; with <paramref name="acrylic"/> also the
    /// transient (Acrylic) system backdrop behind the window.
    /// </summary>
    public static void ApplyLook(nint hwnd, bool dark, int borderColorRgb, bool acrylic = false)
    {
        var darkValue = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));
        if (acrylic)
        {
            var backdrop = DWMSBT_TRANSIENTWINDOW;
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        }

        var corner = DWMWCP_ROUND;
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        // COLORREF is 0x00BBGGRR.
        var colorRef = ((borderColorRgb & 0xFF) << 16) | (borderColorRgb & 0xFF00) | ((borderColorRgb >> 16) & 0xFF);
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
    }

    public static ScreenPoint GetCursor()
        => GetCursorPos(out var p) ? new ScreenPoint(p.X, p.Y) : new ScreenPoint(0, 0);

    public static MonitorArea GetMonitorAt(ScreenPoint point)
        => Describe(MonitorFromPoint(new POINT { X = point.X, Y = point.Y }, MONITOR_DEFAULTTONEAREST));

    public static MonitorArea GetMonitorOf(nint window) => Describe(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST));

    private static MonitorArea Describe(nint monitor)
    {
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(monitor, ref info);
        var scale = GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 ? dpiX / 96.0 : 1.0;
        var w = info.rcWork;
        return new MonitorArea(new PxRect(w.Left, w.Top, w.Right, w.Bottom), scale);
    }

    /// <summary>Moves a window (topmost, without activating it) to the given physical pixel position.</summary>
    public static void MoveTo(nint hwnd, int x, int y)
        => SetWindowPos(hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);

    /// <summary>
    /// Places a window of the given pixel size below (or, if that does not fit, above) an anchor point,
    /// kept inside the monitor's work area.
    /// </summary>
    public static (int X, int Y) PlaceNear(ScreenPoint anchor, int anchorHeight, int widthPx, int heightPx, MonitorArea monitor)
    {
        // Keep a small margin to the screen edge: a window sitting exactly on the edge looks cramped and,
        // as seen in testing, can go missing at the very edge of the monitor.
        var margin = (int)Math.Round(8 * monitor.Scale);
        var work = monitor.WorkArea;
        var minX = work.Left + margin;
        var maxX = Math.Max(minX, work.Right - widthPx - margin);
        var minY = work.Top + margin;
        var maxY = Math.Max(minY, work.Bottom - heightPx - margin);

        var x = Math.Clamp(anchor.X, minX, maxX);
        var y = anchor.Y + anchorHeight;
        if (y + heightPx > work.Bottom - margin) y = anchor.Y - heightPx;
        y = Math.Clamp(y, minY, maxY);
        return (x, y);
    }

    /// <summary>Centered horizontally, in the upper third vertically (where launcher-style UIs usually sit).</summary>
    public static (int X, int Y) PlaceCentered(int widthPx, int heightPx, MonitorArea monitor)
    {
        var work = monitor.WorkArea;
        return (work.Left + (work.Width - widthPx) / 2, work.Top + (work.Height - heightPx) / 3);
    }

    /// <summary>Brings one of our own windows to the foreground, with the AttachThreadInput fallback for focus-stealing rules.</summary>
    public static bool Activate(nint hwnd) => ForceForeground(hwnd);

    internal static bool ForceForeground(nint hwnd)
    {
        if (GetForegroundWindow() == hwnd) return true;
        if (SetForegroundWindow(hwnd)) return true;

        // Windows refuses SetForegroundWindow from a process that is not (or no longer) the foreground one.
        // Attaching to the foreground thread's input queue lifts that restriction for the call.
        var foreground = GetForegroundWindow();
        var foregroundThread = foreground == 0 ? 0 : GetWindowThreadProcessId(foreground, out _);
        var ourThread = GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != ourThread && AttachThreadInput(ourThread, foregroundThread, true);
        try
        {
            return SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(ourThread, foregroundThread, false);
        }
    }

    /// <summary>
    /// Gives the focus back to the window (and control) the hotkey was pressed in. Returns when Windows
    /// reports it as the foreground window, so the next keystrokes we send really land there.
    /// </summary>
    public static async Task<bool> RestoreFocusAsync(TargetInfo target, int timeoutMs = 400)
    {
        if (!IsWindow(target.Window)) return false;

        ForceForeground(target.Window);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (GetForegroundWindow() != target.Window)
        {
            if (Environment.TickCount64 > deadline) return false;
            await Task.Delay(1);
        }

        // Normally Windows restores the previously focused control by itself; make sure.
        if (target.FocusWindow != 0 && IsWindow(target.FocusWindow))
        {
            var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
            if (GetGUIThreadInfo(target.ThreadId, ref info) && info.hwndFocus != target.FocusWindow)
            {
                var ourThread = GetCurrentThreadId();
                if (AttachThreadInput(ourThread, target.ThreadId, true))
                {
                    SetFocus(target.FocusWindow);
                    AttachThreadInput(ourThread, target.ThreadId, false);
                }
            }
        }

        return true;
    }
}

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

/// <summary>
/// The material behind a window. Acrylic, Mica and MicaAlt are DWM system backdrops (Windows 11 22H2+); Blur is the
/// older "blur behind" accent (a plain Gaussian blur without noise or system tint).
/// </summary>
public enum WindowBackdrop
{
    None,
    Acrylic,
    Mica,
    MicaAlt,
    Blur,
}

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
    private const int DWMSBT_NONE = 1;
    private const int DWMSBT_MAINWINDOW = 2;
    private const int DWMSBT_TRANSIENTWINDOW = 3;
    private const int DWMSBT_TABBEDWINDOW = 4;
    private const int WCA_ACCENT_POLICY = 19;
    private const int ACCENT_DISABLED = 0;
    private const int ACCENT_ENABLE_BLURBEHIND = 3;
    private const int DWMWCP_DONOTROUND = 1;
    private const int DWMWCP_ROUND = 2;
    private const int DWMWCP_ROUNDSMALL = 3;
    private const int DWMWA_COLOR_NONE = unchecked((int)0xFFFFFFFE);

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
    /// The dark/light mode of the window frame and, for a non-layered window, the system material behind it
    /// (null for a layered window, which cannot have one). The 1 px border is drawn by the window itself, so DWM's own
    /// border is switched off.
    /// </summary>
    /// <param name="nativeCornerPx">
    /// Corner size DWM cuts the window (and its material) to: 0, 4 or 8. DWM offers nothing in between, and it draws
    /// the material in the whole window rectangle whatever the window region says, so these three are all that
    /// Acrylic and Mica can do. With 0 the window shapes itself (its panel is rounded; see the radius setting).
    /// </param>
    public static void ApplyLook(nint hwnd, bool dark, WindowBackdrop? backdrop, int nativeCornerPx)
    {
        var darkValue = dark ? 1 : 0;
        DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));
        if (backdrop is { } material)
        {
            var type = BackdropType(material);
            DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
            SetBlurBehind(hwnd, material == WindowBackdrop.Blur);
        }

        var corner = nativeCornerPx switch
        {
            >= 8 => DWMWCP_ROUND,
            >= 4 => DWMWCP_ROUNDSMALL,
            _ => DWMWCP_DONOTROUND,
        };
        DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        var noBorder = DWMWA_COLOR_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref noBorder, sizeof(int));
    }

    // Blur uses the accent API instead of a DWM backdrop, so the DWM backdrop is switched off for it (and for None).
    private static int BackdropType(WindowBackdrop material) => material switch
    {
        WindowBackdrop.Acrylic => DWMSBT_TRANSIENTWINDOW,
        WindowBackdrop.Mica => DWMSBT_MAINWINDOW,
        WindowBackdrop.MicaAlt => DWMSBT_TABBEDWINDOW,
        _ => DWMSBT_NONE,
    };

    /// <summary>
    /// Sets the material again on a window that has just been activated. A window shown without activation gets its
    /// material from DWM in the flat fallback state (no blur, a solid grey), and setting the same value again changes
    /// nothing; switching it off and on once while the window is active makes DWM build the real material.
    /// </summary>
    public static void RefreshBackdrop(nint hwnd, WindowBackdrop backdrop)
    {
        if (backdrop == WindowBackdrop.None) return;
        if (backdrop == WindowBackdrop.Blur)
        {
            SetBlurBehind(hwnd, false);
            SetBlurBehind(hwnd, true);
            return;
        }

        var off = DWMSBT_NONE;
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref off, sizeof(int));
        var type = BackdropType(backdrop);
        DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref type, sizeof(int));
    }

    /// <summary>
    /// The "blur behind" accent: a Gaussian blur of what is behind the window, with no tint of its own (the window's
    /// surface colour provides that). Undocumented but long-standing user32 API; the blur always fills the whole window.
    /// </summary>
    private static void SetBlurBehind(nint hwnd, bool enabled)
    {
        var accent = new ACCENT_POLICY { AccentState = enabled ? ACCENT_ENABLE_BLURBEHIND : ACCENT_DISABLED };
        var size = Marshal.SizeOf<ACCENT_POLICY>();
        var data = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, data, fDeleteOld: false);
            var attribute = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = WCA_ACCENT_POLICY, Data = data, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref attribute);
        }
        finally
        {
            Marshal.FreeHGlobal(data);
        }
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

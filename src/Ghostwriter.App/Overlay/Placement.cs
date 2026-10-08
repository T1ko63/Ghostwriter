using System.Windows;
using Ghostwriter.Platform.TextIntegration;
using Ghostwriter.Platform.Windowing;

namespace Ghostwriter.App.Overlay;

public enum OverlayPosition
{
    Caret,
    Mouse,
    Center,
}

/// <summary>Where the overlay and the status pill appear for one run: resolved once at hotkey time.</summary>
public readonly record struct Anchor(ScreenPoint Point, int Height, MonitorArea Monitor, bool Centered)
{
    public static Anchor Resolve(TargetInfo? target, OverlayPosition position)
    {
        var cursor = WindowHelper.GetCursor();

        if (position == OverlayPosition.Center)
        {
            var monitor = target is null ? WindowHelper.GetMonitorAt(cursor) : WindowHelper.GetMonitorOf(target.Window);
            return new Anchor(cursor, 0, monitor, Centered: true);
        }

        // Caret position is only known for classic Win32 carets; browsers and Electron fall back to the mouse.
        if (position == OverlayPosition.Caret && target?.CaretScreenPos is { } caret)
        {
            return new Anchor(caret, 6, WindowHelper.GetMonitorAt(caret), Centered: false);
        }

        return new Anchor(cursor, 22, WindowHelper.GetMonitorAt(cursor), Centered: false);
    }

    /// <summary>Anchor right below a text cursor whose screen position is known (e.g. from UI Automation).</summary>
    public static Anchor AtCaret(ScreenPoint caret) => new(caret, 6, WindowHelper.GetMonitorAt(caret), Centered: false);

    /// <summary>Top-left position in physical pixels for a window of the given size in device-independent units.</summary>
    public (int X, int Y) PlaceWindow(Size sizeDip)
    {
        var widthPx = (int)Math.Ceiling(sizeDip.Width * Monitor.Scale);
        var heightPx = (int)Math.Ceiling(sizeDip.Height * Monitor.Scale);
        return Centered
            ? WindowHelper.PlaceCentered(widthPx, heightPx, Monitor)
            : WindowHelper.PlaceNear(Point, Height, widthPx, heightPx, Monitor);
    }
}

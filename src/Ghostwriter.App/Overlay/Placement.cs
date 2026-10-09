using System.Windows;
using Ghostwriter.Platform.TextIntegration;
using Ghostwriter.Platform.Windowing;

namespace Ghostwriter.App.Overlay;

public enum OverlayPosition
{
    Caret,
    Mouse,

    /// <summary>A fixed place on the monitor (overlay_fixed_position); not handled by <see cref="Anchor.Resolve"/> but by <see cref="Anchor.ResolveSpot"/>.</summary>
    Fixed,
}

/// <summary>A fixed place for the result card on the monitor (result_fixed_position).</summary>
public enum CardSpot
{
    /// <summary>Horizontally centered, in the middle of the upper third.</summary>
    TopThird,
    Center,

    /// <summary>Horizontally centered, in the middle of the lower third.</summary>
    BottomThird,
    TopLeft,
    BottomLeft,
    TopRight,
    BottomRight,

    /// <summary>Left edge, vertically centered.</summary>
    Left,

    /// <summary>Right edge, vertically centered.</summary>
    Right,

    /// <summary>Top edge, horizontally centered.</summary>
    Top,

    /// <summary>Bottom edge, horizontally centered.</summary>
    Bottom,
}

/// <summary>Which way a card grows when it gets taller: always away from the screen edge it sits at.</summary>
public enum CardGrowth
{
    /// <summary>The top edge stays, the bottom moves down.</summary>
    Down,

    /// <summary>The bottom edge stays, the top moves up.</summary>
    Up,

    /// <summary>The vertical middle stays (cards in the middle of the screen).</summary>
    Both,
}

/// <summary>X and the fixed vertical reference of a card (its top, bottom or middle, see <see cref="CardGrowth"/>), in physical pixels.</summary>
public readonly record struct CardPlacement(int X, int Edge, CardGrowth Growth)
{
    /// <summary>Top of the window for the given height.</summary>
    public int TopFor(int heightPx) => Growth switch
    {
        CardGrowth.Down => Edge,
        CardGrowth.Up => Edge - heightPx,
        _ => Edge - heightPx / 2,
    };
}

/// <summary>The rules for fixed places: pure geometry, no WPF or Win32 calls.</summary>
public static class CardSpots
{
    /// <summary>
    /// Where a card of the given size goes. <paramref name="heightPx"/> should be the card's maximum height: the place is decided
    /// once for that size, so that the card can then grow without moving its fixed edge and without leaving the screen.
    /// Cards at the top (or in the upper third) grow down, cards at the bottom (or in the lower third) grow up, cards in the
    /// middle grow in both directions.
    /// </summary>
    public static CardPlacement Place(CardSpot spot, MonitorArea monitor, int widthPx, int heightPx, int marginPx)
    {
        var work = monitor.WorkArea;

        var x = spot switch
        {
            CardSpot.TopLeft or CardSpot.BottomLeft or CardSpot.Left => work.Left + marginPx,
            CardSpot.TopRight or CardSpot.BottomRight or CardSpot.Right => work.Right - widthPx - marginPx,
            _ => work.Left + (work.Width - widthPx) / 2,
        };
        x = Math.Max(work.Left, Math.Min(x, work.Right - widthPx));

        var (growth, edge) = spot switch
        {
            CardSpot.Top or CardSpot.TopLeft or CardSpot.TopRight => (CardGrowth.Down, work.Top + marginPx),
            CardSpot.Bottom or CardSpot.BottomLeft or CardSpot.BottomRight => (CardGrowth.Up, work.Bottom - marginPx),
            CardSpot.TopThird => (CardGrowth.Down, work.Top + work.Height / 6),
            CardSpot.BottomThird => (CardGrowth.Up, work.Top + work.Height * 5 / 6),
            _ => (CardGrowth.Both, work.Top + work.Height / 2),
        };

        // Keep the whole card (at the given height) between the margins.
        var (low, high) = growth switch
        {
            CardGrowth.Down => (work.Top + marginPx, work.Bottom - marginPx - heightPx),
            CardGrowth.Up => (work.Top + marginPx + heightPx, work.Bottom - marginPx),
            _ => (work.Top + marginPx + heightPx / 2, work.Bottom - marginPx - heightPx / 2),
        };
        edge = Math.Max(low, Math.Min(edge, Math.Max(low, high)));
        return new CardPlacement(x, edge, growth);
    }
}

/// <summary>Where the overlay and the status pill appear for one run: resolved once at hotkey time.</summary>
/// <param name="Spot">Set for the result card with a fixed place: the window goes there (see <see cref="CardSpots"/>), not near <paramref name="Point"/>.</param>
/// <param name="MarginDip">Distance kept to the screen edge (result_screen_margin for the card).</param>
public readonly record struct Anchor(
    ScreenPoint Point, int Height, MonitorArea Monitor, CardSpot? Spot = null, double MarginDip = Anchor.DefaultMarginDip)
{
    public const double DefaultMarginDip = 8;

    public static Anchor Resolve(TargetInfo? target, OverlayPosition position)
    {
        var cursor = WindowHelper.GetCursor();

        // Caret position is only known for classic Win32 carets; browsers and Electron fall back to the mouse.
        if (position == OverlayPosition.Caret && target?.CaretScreenPos is { } caret)
        {
            return new Anchor(caret, 6, WindowHelper.GetMonitorAt(caret));
        }

        return new Anchor(cursor, 22, WindowHelper.GetMonitorAt(cursor));
    }

    /// <summary>A fixed place on the monitor of the target window (or, without a target, of the mouse).</summary>
    public static Anchor ResolveSpot(TargetInfo? target, CardSpot spot, double marginDip)
    {
        var cursor = WindowHelper.GetCursor();
        var monitor = target is null ? WindowHelper.GetMonitorAt(cursor) : WindowHelper.GetMonitorOf(target.Window);
        return new Anchor(cursor, 0, monitor, spot, marginDip);
    }

    /// <summary>Anchor right below a text cursor whose screen position is known (e.g. from UI Automation).</summary>
    public static Anchor AtCaret(ScreenPoint caret) => new(caret, 6, WindowHelper.GetMonitorAt(caret));

    /// <summary>Top-left position in physical pixels for a window of the given size in device-independent units.</summary>
    public (int X, int Y) PlaceWindow(Size sizeDip)
    {
        var widthPx = (int)Math.Ceiling(sizeDip.Width * Monitor.Scale);
        var heightPx = (int)Math.Ceiling(sizeDip.Height * Monitor.Scale);
        var marginPx = (int)Math.Round(MarginDip * Monitor.Scale);

        if (Spot is { } spot)
        {
            var placement = CardSpots.Place(spot, Monitor, widthPx, heightPx, marginPx);
            return (placement.X, placement.TopFor(heightPx));
        }

        return WindowHelper.PlaceNear(Point, Height, widthPx, heightPx, Monitor, MarginDip);
    }
}

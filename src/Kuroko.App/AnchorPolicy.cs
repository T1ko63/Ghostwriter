using Kuroko.App.Overlay;
using Kuroko.Core.Config;
using Kuroko.Platform.TextIntegration;

namespace Kuroko.App;

/// <summary>
/// Where the prompt picker, the pills and the result card go for a target, following the overlay_* and result_* placement
/// settings. Holds no state besides those settings.
/// </summary>
internal sealed class AnchorPolicy
{
    public OverlayPosition Position { get; set; } = OverlayPosition.Caret;

    /// <summary>overlay_fixed_position: used when <see cref="Position"/> is <see cref="OverlayPosition.Fixed"/>.</summary>
    public CardSpot OverlaySpot { get; set; } = CardSpot.TopThird;

    /// <summary>overlay_screen_margin: used in every mode.</summary>
    public double OverlayScreenMargin { get; set; } = AppSettings.DefaultOverlayScreenMargin;

    /// <summary>result_position: where the card opens.</summary>
    public ResultPlacement ResultPlacement { get; set; } = ResultPlacement.Fixed;

    /// <summary>result_fixed_position.</summary>
    public CardSpot ResultSpot { get; set; } = CardSpot.BottomThird;

    /// <summary>result_screen_margin: distance of the card to the screen edge, in device-independent pixels.</summary>
    public double ResultScreenMargin { get; set; } = AppSettings.DefaultResultScreenMargin;

    /// <summary>Where the overlay and the pills go for the current target: a fixed place, or at the text cursor / mouse.</summary>
    public Anchor ForTarget(TargetInfo? target)
        => Position == OverlayPosition.Fixed
            ? Anchor.ResolveSpot(target, OverlaySpot, OverlayScreenMargin)
            : Anchor.Resolve(target, Position) with { MarginDip = OverlayScreenMargin };

    /// <summary>Moves a mouse-based anchor to the text cursor if UI Automation found it.</summary>
    public Anchor Refine(Anchor anchor, TargetInfo? target, FocusProbe probe, OverlayPosition? position = null)
        => (position ?? Position) == OverlayPosition.Caret && target?.CaretScreenPos is null && probe.Info?.CaretPoint is { } caret
            ? Anchor.AtCaret(caret)
            : anchor;

    /// <summary>Where the card opens: a fixed place, like the picker (follow), at the text cursor, or at the mouse.</summary>
    public Anchor ForResult(TargetInfo target, FocusProbe probe, Anchor pickerAnchor) => ResultPlacement switch
    {
        ResultPlacement.Fixed => Anchor.ResolveSpot(target, ResultSpot, ResultScreenMargin),
        ResultPlacement.Caret => Refine(Anchor.Resolve(target, OverlayPosition.Caret), target, probe, OverlayPosition.Caret) with { MarginDip = ResultScreenMargin },
        ResultPlacement.Mouse => Anchor.Resolve(target, OverlayPosition.Mouse) with { MarginDip = ResultScreenMargin },
        _ => Refine(pickerAnchor, target, probe) with { MarginDip = ResultScreenMargin },
    };
}

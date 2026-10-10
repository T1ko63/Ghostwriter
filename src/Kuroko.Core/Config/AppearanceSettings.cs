namespace Kuroko.Core.Config;

public enum BlurMode
{
    Acrylic,
    Mica,
    MicaAlt,
    Blur,
    None,
}

/// <summary>The three colours of one theme. Background and foreground are opaque; selection may carry its own alpha.</summary>
public sealed record ThemeColors(Rgba Background, Rgba Foreground, Rgba Selection);

/// <summary>The [appearance] block of settings.toml, validated, with defaults for everything that is missing or invalid.</summary>
public sealed record AppearanceSettings(
    int Transparency, int Radius, BlurMode Blur, ThemeColors Dark, ThemeColors Light, bool Border = true, bool CustomCorners = false)
{
    public const int DefaultTransparency = 50;
    public const int DefaultRadius = 10;
    public const BlurMode DefaultBlur = BlurMode.Acrylic;
    public const int MaxRadius = 100;
    public const bool DefaultBorder = true;

    /// <summary>
    /// custom_corners: the corner radius is meant to be applied exactly, also on a system material (Acrylic, Mica). Windows itself only
    /// offers 0, 4 and 8 px there; the Windhawk mod "Kuroko Blur" (corners) makes the window manager use the real radius.
    /// </summary>
    public const bool DefaultCustomCorners = false;

    public static readonly ThemeColors DefaultDark = new(
        Rgba.Opaque(0x00, 0x00, 0x00), Rgba.Opaque(0xF0, 0xF0, 0xF0), new Rgba(0x40, 0xFF, 0xFF, 0xFF));

    public static readonly ThemeColors DefaultLight = new(
        Rgba.Opaque(0xFF, 0xFF, 0xFF), Rgba.Opaque(0x1A, 0x1A, 0x1A), new Rgba(0x40, 0x00, 0x00, 0x00));

    public static AppearanceSettings Default { get; } = new(DefaultTransparency, DefaultRadius, DefaultBlur, DefaultDark, DefaultLight, DefaultBorder);

    /// <summary>How much of the window surface is visible: 100 - transparency, in percent.</summary>
    public int OpacityPercent => 100 - Transparency;

    /// <summary>Alpha (0..255) of the window surface colour.</summary>
    public byte SurfaceAlpha => OpacityToAlpha(OpacityPercent);

    /// <summary>Corner radius of the highlighted row: a bit smaller than the window's.</summary>
    public int RowRadius => Math.Min(MaxRowRadius, (int)Math.Round(Radius * 0.6, MidpointRounding.AwayFromZero));

    /// <summary>A row is 38 px high; a larger corner radius would only make it a pill, so the rows stop at half of that.</summary>
    public const int MaxRowRadius = 19;

    public ThemeColors For(bool dark) => dark ? Dark : Light;

    /// <summary>
    /// The corner size a window on a Windows 11 system material (Acrylic, Mica) really gets: DWM only offers 0, 4 and
    /// 8 px, so the configured radius snaps to the nearest of these.
    /// </summary>
    public static int NativeCornerRadius(int radius) => radius switch
    {
        < 2 => 0,
        < 6 => 4,
        _ => 8,
    };

    public static byte OpacityToAlpha(int percent)
        => (byte)Math.Round(Math.Clamp(percent, 0, 100) * 255 / 100.0, MidpointRounding.AwayFromZero);
}

/// <summary>The colours the UI needs, all derived from the three configured ones. The text parts only differ in the opacity of the foreground.</summary>
public sealed record AppearancePalette(
    Rgba Surface, Rgba Edge, Rgba Text, Rgba Muted, Rgba Subtle, Rgba Placeholder, Rgba Separator,
    Rgba RowSelected, Rgba RowHover, Rgba ScrollThumb, Rgba Progress, Rgba Caret, Rgba TextSelection,
    bool BackgroundIsDark)
{
    // Opacity of the foreground colour for each secondary use.
    public const double MutedOpacity = 0.62;
    public const double SubtleOpacity = 0.42;
    public const double PlaceholderOpacity = 0.5;
    public const double SeparatorOpacity = 0.12;
    public const double ScrollThumbOpacity = 0.45;
    public const double ProgressOpacity = 0.7;
    public const double TextSelectionOpacity = 0.3;
    public const double EdgeOpacity = 0.16;

    public static AppearancePalette Derive(AppearanceSettings appearance, bool dark)
    {
        var colors = appearance.For(dark);
        var fg = colors.Foreground;
        var bg = colors.Background;
        return new AppearancePalette(
            Surface: bg.WithAlpha(appearance.SurfaceAlpha),
            Edge: fg.Scaled(EdgeOpacity).Over(bg),
            Text: fg,
            Muted: fg.Scaled(MutedOpacity),
            Subtle: fg.Scaled(SubtleOpacity),
            Placeholder: fg.Scaled(PlaceholderOpacity),
            Separator: fg.Scaled(SeparatorOpacity),
            RowSelected: colors.Selection,
            RowHover: colors.Selection.Scaled(0.5),
            ScrollThumb: fg.Scaled(ScrollThumbOpacity),
            Progress: fg.Scaled(ProgressOpacity),
            Caret: fg,
            TextSelection: fg.Scaled(TextSelectionOpacity),
            BackgroundIsDark: bg.Luminance < 0.5);
    }
}

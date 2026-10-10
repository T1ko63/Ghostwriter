using Kuroko.Core.Providers;

namespace Kuroko.Core.Config;

/// <summary>Everything from settings.toml, validated and with defaults filled in.</summary>
public sealed record AppSettings(
    string OverlayHotkey,
    string MarkerStart,
    string MarkerEnd,
    string Theme,
    bool Autostart,
    string OverlayPosition,
    string Language,
    int IdleTrimSeconds,
    string? DefaultProvider,
    IReadOnlyDictionary<string, ProviderSettings> Providers,
    string Accent = "none",
    string UndoHotkey = "Ctrl+Alt+Z",
    int UndoHistory = 10,
    string ResultCopyHotkey = AppSettings.DefaultResultCopyHotkey,
    string ResultPosition = AppSettings.DefaultResultPosition,
    double ResultFontSize = AppSettings.DefaultResultFontSize,
    double ResultWidth = AppSettings.DefaultResultWidth,
    double ResultMaxHeight = AppSettings.DefaultResultMaxHeight,
    double ResultMinHeight = AppSettings.DefaultResultMinHeight,
    string ResultFixedPosition = AppSettings.DefaultResultFixedPosition,
    double ResultScreenMargin = AppSettings.DefaultResultScreenMargin,
    string OverlayFixedPosition = AppSettings.DefaultOverlayFixedPosition,
    double OverlayScreenMargin = AppSettings.DefaultOverlayScreenMargin,
    double OverlayWidth = AppSettings.DefaultOverlayWidth,
    double OverlayMinHeight = AppSettings.DefaultOverlayMinHeight,
    double OverlayMaxHeight = AppSettings.DefaultOverlayMaxHeight,
    double OverlayFontSize = AppSettings.DefaultOverlayFontSize)
{
    /// <summary>Look of the windows ([appearance] in settings.toml). Defaults when the block is missing.</summary>
    public AppearanceSettings Appearance { get; init; } = AppearanceSettings.Default;

    public const string DefaultOverlayHotkey = "Ctrl+Shift+Space";
    public const string DefaultUndoHotkey = "Ctrl+Alt+Z";

    /// <summary>Copies the text of the result card (overlay output) while the card is visible.</summary>
    public const string DefaultResultCopyHotkey = "Ctrl+Alt+C";

    /// <summary>Where the result card appears: "fixed" (at result_fixed_position on the target window's monitor), "follow" (where the prompt picker opens, see overlay_position), "caret" (at the text cursor, otherwise at the mouse) or "mouse".</summary>
    public const string DefaultResultPosition = "fixed";

    /// <summary>The fixed place of the card (result_fixed_position).</summary>
    public const string DefaultResultFixedPosition = "bottom-third";

    /// <summary>Distance of the card to the screen edge in device-independent pixels (result_screen_margin).</summary>
    public const double DefaultResultScreenMargin = 24;
    public const double MinResultScreenMargin = 0;
    public const double MaxResultScreenMargin = 200;
    /// <summary>The prompt picker: fixed place (overlay_fixed_position), distance to the screen edge, size and font (all in device-independent pixels).</summary>
    public const string DefaultOverlayFixedPosition = "top-third";
    public const double DefaultOverlayScreenMargin = 8;
    public const double DefaultOverlayWidth = 520;
    public const double MinOverlayWidth = 240;
    public const double MaxOverlayWidth = 1600;
    public const double DefaultOverlayMinHeight = 0;
    public const double DefaultOverlayMaxHeight = 393; // search line, separator, eight rows and the border
    public const double MinOverlayMaxHeight = 120;
    public const double MaxOverlayHeight = 2000;
    public const double DefaultOverlayFontSize = 14.5;

    public const double DefaultResultMinHeight = 0;
    public const double MinResultMinHeight = 0;
    public const double MaxResultMinHeight = 2000;

    /// <summary>Font size of the result card text in device-independent pixels.</summary>
    public const double DefaultResultFontSize = 15;
    public const double MinResultFontSize = 8;
    public const double MaxResultFontSize = 32;

    /// <summary>Width and maximum height of the result card in device-independent pixels (it is never larger than the monitor).</summary>
    public const double DefaultResultWidth = 480;
    public const double MinResultWidth = 240;
    public const double MaxResultWidth = 1600;
    public const double DefaultResultMaxHeight = 360;
    public const double MinResultMaxHeight = 120;
    public const double MaxResultMaxHeight = 2000;
    public const int DefaultUndoHistory = 10;
    public const int MaxUndoHistory = 100;
    /// <summary>After this many idle seconds the app gives back unused memory (0 = never).</summary>
    public const int DefaultIdleTrimSeconds = 90;

    /// <summary>Used when settings.toml cannot be read at all: the app still starts, AI calls report "no provider".</summary>
    public static AppSettings Fallback { get; } = new(
        DefaultOverlayHotkey, "<<", ">>", "system", false, "caret", "auto", DefaultIdleTrimSeconds, null,
        new Dictionary<string, ProviderSettings>());
}

/// <summary>
/// A problem found while reading a config file. Errors keep the previous configuration active; warnings do not.
/// A warning with <c>Show</c> set is also shown to the user (other warnings only go to the log).
/// </summary>
public sealed record ConfigIssue(string File, int? Line, string Message, bool IsError, bool Show = false)
{
    public override string ToString() => Line is { } line ? $"{File}, line {line}: {Message}" : $"{File}: {Message}";
}

public sealed record SettingsLoadResult(AppSettings? Settings, IReadOnlyList<ConfigIssue> Issues)
{
    public bool Ok => Settings is not null;
}

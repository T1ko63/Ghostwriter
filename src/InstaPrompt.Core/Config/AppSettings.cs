using InstaPrompt.Core.Providers;

namespace InstaPrompt.Core.Config;

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
    int UndoHistory = 10)
{
    /// <summary>Look of the windows ([appearance] in settings.toml). Defaults when the block is missing.</summary>
    public AppearanceSettings Appearance { get; init; } = AppearanceSettings.Default;

    public const string DefaultOverlayHotkey = "Ctrl+Shift+Space";
    public const string DefaultUndoHotkey = "Ctrl+Alt+Z";
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

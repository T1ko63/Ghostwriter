using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace InstaPrompt.App.Themes;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Turns the Light.* or Dark.* colours of Design.xaml into the *Brush resources the windows bind to, follows the
/// Windows setting in System mode, and applies the optional accent ("none", "system" or a hex colour).
/// Without an accent the whole UI is neutral.
/// </summary>
public sealed class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";

    private static readonly string[] Tokens =
    [
        "Edge", "Text", "Muted", "Subtle", "Placeholder", "Separator", "RowHover", "RowSelected", "ScrollThumb",
        "Progress", "Caret", "TextSelection", "Error",
    ];

    private readonly Application _app;
    private AppTheme _mode = AppTheme.System;
    private string _accent = "none";
    private string _prefix = "Dark";

    public ThemeService(Application app)
    {
        _app = app;
        // Windows switches theme or accent colour: follow along (only matters for "system" values).
        SystemEvents.UserPreferenceChanged += (_, _) => _app.Dispatcher.BeginInvoke(() =>
        {
            if (_mode == AppTheme.System || _accent == "system") Apply();
        });
    }

    public AppTheme Mode => _mode;

    public bool IsDark { get; private set; }

    /// <summary>Border colour (0xRRGGBB) for the DWM window frame of the current theme.</summary>
    public int BorderColorRgb
    {
        get
        {
            var c = Resolve($"{_prefix}.Edge");
            return (c.R << 16) | (c.G << 8) | c.B;
        }
    }

    public event Action? Changed;

    /// <summary>Sets theme and accent in one go (one repaint). Unknown accent text counts as "none".</summary>
    public void Set(AppTheme mode, string accent)
    {
        _mode = mode;
        _accent = accent.Trim().ToLowerInvariant();
        Apply();
    }

    private void Apply()
    {
        IsDark = _mode == AppTheme.Dark || (_mode == AppTheme.System && !SystemUsesLightApps());
        _prefix = IsDark ? "Dark" : "Light";

        foreach (var token in Tokens) Publish(token, Resolve($"{_prefix}.{token}"));
        Publish("Surface", Resolve($"{_prefix}.{(WindowSkin.BackdropEnabled ? "SurfaceBackdrop" : "Surface")}"));

        if (ResolveAccent() is { } accent)
        {
            Publish("RowSelected", WithAlpha(accent, Resolve($"{_prefix}.AccentTintRow").A));
            Publish("TextSelection", WithAlpha(accent, Resolve($"{_prefix}.AccentTintText").A));
            Publish("Progress", accent);
            Publish("Caret", accent);
        }

        Changed?.Invoke();
    }

    private Color Resolve(string key) => (Color)_app.FindResource(key);

    private void Publish(string token, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _app.Resources[token + "Brush"] = brush;
    }

    private static Color WithAlpha(Color c, byte alpha) => System.Windows.Media.Color.FromArgb(alpha, c.R, c.G, c.B);

    private Color? ResolveAccent()
    {
        if (_accent == "system") return ReadSystemAccent();
        if (_accent.StartsWith('#') && TryParseHex(_accent, out var color)) return color;
        return null;
    }

    /// <summary>#RGB, #RRGGBB or #AARRGGBB (the alpha is ignored: the tints bring their own).</summary>
    internal static bool TryParseHex(string text, out Color color)
    {
        color = default;
        var hex = text.TrimStart('#');
        if (hex.Length == 3) hex = string.Concat(hex.Select(ch => new string(ch, 2)));
        if (hex.Length == 8) hex = hex[2..];
        if (hex.Length != 6 || !int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb)) return false;
        color = System.Windows.Media.Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    private static bool SystemUsesLightApps()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is not int value || value != 0;
    }

    private static Color ReadSystemAccent()
    {
        using var key = Registry.CurrentUser.OpenSubKey(DwmKey);
        if (key?.GetValue("AccentColor") is int abgr)
        {
            // Stored as 0xAABBGGRR.
            return System.Windows.Media.Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
        }

        return System.Windows.Media.Color.FromRgb(0x00, 0x78, 0xD4);
    }
}

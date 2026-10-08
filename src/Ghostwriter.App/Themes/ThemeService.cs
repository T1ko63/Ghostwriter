using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Ghostwriter.Core.Config;
using Ghostwriter.Platform.Windowing;
using Microsoft.Win32;

namespace Ghostwriter.App.Themes;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Turns the [appearance] settings into the resources the windows bind to with DynamicResource: the *Brush colours
/// (all derived from background, foreground and selection of the active theme, see <see cref="AppearancePalette"/>)
/// and the corner radii. Follows the Windows setting in System mode and applies the optional accent
/// ("none", "system" or a hex colour) to cursor, text selection and progress line. Everything is computed once per
/// configuration change, never when a window is shown.
/// </summary>
public sealed class ThemeService
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string DwmKey = @"Software\Microsoft\Windows\DWM";
    private const byte AccentTextSelectionAlpha = 0x66;

    private readonly Application _app;
    private AppTheme _mode = AppTheme.System;
    private string _accent = "none";
    private AppearanceSettings _appearance = AppearanceSettings.Default;

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

    /// <summary>True when Acrylic or Mica is really in use (Windows 11): DWM then decides the corner size.</summary>
    public bool UsesSystemMaterial => WindowSkin.BackdropEnabled && _appearance.Blur != BlurMode.None;

    /// <summary>
    /// Window corner radius in device-independent pixels. Exactly the configured radius, except on a system material,
    /// where only the DWM sizes 0, 4 and 8 exist.
    /// </summary>
    public int WindowRadius => UsesSystemMaterial ? AppearanceSettings.NativeCornerRadius(_appearance.Radius) : _appearance.Radius;

    /// <summary>The corner size to ask DWM for: the native size on a system material, otherwise 0 (the panel does the rounding).</summary>
    public int NativeCornerPx => UsesSystemMaterial ? WindowRadius : 0;

    /// <summary>The system material for the configured blur (only used where windows can have one).</summary>
    public WindowBackdrop Backdrop => _appearance.Blur switch
    {
        BlurMode.Acrylic => WindowBackdrop.Acrylic,
        BlurMode.Mica => WindowBackdrop.Mica,
        BlurMode.MicaAlt => WindowBackdrop.MicaAlt,
        BlurMode.Blur => WindowBackdrop.Blur,
        _ => WindowBackdrop.None,
    };

    public event Action? Changed;

    /// <summary>Sets theme, accent and appearance in one go (one repaint). Unknown accent text counts as "none".</summary>
    public void Set(AppTheme mode, string accent, AppearanceSettings appearance)
    {
        _mode = mode;
        _accent = accent.Trim().ToLowerInvariant();
        _appearance = appearance;
        Apply();
    }

    private void Apply()
    {
        IsDark = _mode == AppTheme.Dark || (_mode == AppTheme.System && !SystemUsesLightApps());
        var palette = AppearancePalette.Derive(_appearance, IsDark);

        // A completely clear surface (transparency 100) would let mouse clicks fall through a layered window, so it
        // keeps an alpha of 1/255: invisible, but still part of the window.
        Publish("Surface", Convert(palette.Surface.A == 0 ? palette.Surface.WithAlpha(1) : palette.Surface));
        Publish("Edge", Convert(palette.Edge));
        Publish("Text", Convert(palette.Text));
        Publish("Muted", Convert(palette.Muted));
        Publish("Subtle", Convert(palette.Subtle));
        Publish("Placeholder", Convert(palette.Placeholder));
        Publish("Separator", Convert(palette.Separator));
        Publish("RowHover", Convert(palette.RowHover));
        Publish("RowSelected", Convert(palette.RowSelected));
        Publish("ScrollThumb", Convert(palette.ScrollThumb));
        Publish("Progress", Convert(palette.Progress));
        Publish("Caret", Convert(palette.Caret));
        Publish("TextSelection", Convert(palette.TextSelection));
        Publish("Error", Resolve(palette.BackgroundIsDark ? "Dark.Error" : "Light.Error"));

        if (ResolveAccent() is { } accent)
        {
            Publish("Progress", accent);
            Publish("Caret", accent);
            Publish("TextSelection", WithAlpha(accent, AccentTextSelectionAlpha));
        }

        _app.Resources["Window.Radius"] = new CornerRadius(WindowRadius);
        _app.Resources["Row.Radius"] = new CornerRadius(_appearance.RowRadius);
        _app.Resources["Window.BorderThickness"] = new Thickness(_appearance.Border ? 1 : 0);

        Changed?.Invoke();
    }

    private static Color Convert(Rgba c) => Color.FromArgb(c.A, c.R, c.G, c.B);

    private Color Resolve(string key) => (Color)_app.FindResource(key);

    private void Publish(string token, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        _app.Resources[token + "Brush"] = brush;
    }

    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

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
        color = Color.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
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
            return Color.FromRgb((byte)(abgr & 0xFF), (byte)((abgr >> 8) & 0xFF), (byte)((abgr >> 16) & 0xFF));
        }

        return Color.FromRgb(0x00, 0x78, 0xD4);
    }
}

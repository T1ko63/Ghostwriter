using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Media;
using Ghostwriter.Platform.Windowing;

namespace Ghostwriter.App.Themes;

/// <summary>
/// The window frame look shared by the overlay and the status pill.
/// <para>With the Windows 11 backdrop (build 22621+) the window is a normal, non-layered window whose client area is
/// made transparent so the DWM material (Acrylic, Mica or none) shows through. DWM cuts the window to its own corner sizes
/// (0, 4 or 8 px: a window region does not clip the material), so the radius is exact only for blur = "none"; the
/// 1 px border is drawn by the panel itself.
/// Everywhere else (older Windows, or GHOSTWRITER_BACKDROP=off) the window is a layered transparent window and the
/// panel draws a semi-transparent surface with rounded corners, the border and a soft shadow itself; there is no blur.</para>
/// </summary>
internal static class WindowSkin
{
    public static bool BackdropEnabled { get; } = WindowHelper.IsBackdropSupported
        && !string.Equals(Environment.GetEnvironmentVariable("GHOSTWRITER_BACKDROP"), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extra margin around the panel in which the self-drawn shadow lives (0 with the backdrop).</summary>
    public static double ShadowMargin => BackdropEnabled ? 0 : (double)Application.Current.FindResource("Window.ShadowMargin");

    /// <summary>Call in the constructor, after InitializeComponent and before the window is first shown.</summary>
    public static void Prepare(Window window, Border panel)
    {
        window.Background = Brushes.Transparent;

        // Radius and edge colour come from [appearance] and can change while the app runs.
        panel.SetResourceReference(Border.BorderThicknessProperty, "Window.BorderThickness");
        panel.SetResourceReference(Border.CornerRadiusProperty, "Window.Radius");
        panel.SetResourceReference(Border.BorderBrushProperty, "EdgeBrush");

        if (BackdropEnabled)
        {
            WindowChrome.SetWindowChrome(window, new WindowChrome
            {
                CaptionHeight = 0,
                ResizeBorderThickness = new Thickness(0),
                GlassFrameThickness = new Thickness(-1),
                CornerRadius = new CornerRadius(0),
                UseAeroCaptionButtons = false,
            });
            window.SourceInitialized += (_, _) =>
            {
                // Pixels WPF leaves transparent must stay transparent for DWM, or the backdrop would be painted black.
                if (HwndSource.FromHwnd(new WindowInteropHelper(window).Handle) is { CompositionTarget: { } target })
                {
                    target.BackgroundColor = Colors.Transparent;
                }
            };
            return;
        }

        window.AllowsTransparency = true;
        panel.Margin = new Thickness(ShadowMargin);
        panel.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.28, Color = Colors.Black };
    }

    /// <summary>
    /// Applies dark/light mode, the system material and the corner size DWM should use. Call when the configuration
    /// changes (not when the window is shown). Layered windows (the fallback) get no material and are shaped by the panel.
    /// </summary>
    public static void ApplyLook(nint hwnd, bool dark, WindowBackdrop backdrop, int nativeCornerPx)
    {
        _backdrop = backdrop;
        if (hwnd == 0) return;
        WindowHelper.ApplyLook(hwnd, dark, BackdropEnabled ? backdrop : null, BackdropEnabled ? nativeCornerPx : 0);
    }

    private static WindowBackdrop _backdrop = WindowBackdrop.None;

    /// <summary>
    /// Call right after a window has been shown: DWM draws the material of a window that was shown without activation
    /// as a flat grey until it is set again (see <see cref="WindowHelper.RefreshBackdrop"/>).
    /// </summary>
    public static void RefreshBackdrop(nint hwnd)
    {
        if (BackdropEnabled && hwnd != 0) WindowHelper.RefreshBackdrop(hwnd, _backdrop);
    }
}

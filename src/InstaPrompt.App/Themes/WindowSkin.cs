using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using System.Windows.Media.Effects;
using System.Windows.Interop;
using System.Windows.Media;
using InstaPrompt.Platform.Windowing;

namespace InstaPrompt.App.Themes;

/// <summary>
/// The window frame look shared by the overlay and the status pill.
/// <para>With the Windows 11 backdrop (build 22621+) the window is a normal, non-layered window whose client area is
/// made transparent so DWM's Acrylic shows through; Windows draws the rounded corners (~8 px), border and shadow.
/// Everywhere else (older Windows, or INSTAPROMPT_BACKDROP=off) the window is a layered transparent window and we
/// draw a semi-transparent panel with 14 px corners, a 1 px border and a soft shadow ourselves.</para>
/// </summary>
internal static class WindowSkin
{
    public static bool BackdropEnabled { get; } = WindowHelper.IsBackdropSupported
        && !string.Equals(Environment.GetEnvironmentVariable("INSTAPROMPT_BACKDROP"), "off", StringComparison.OrdinalIgnoreCase);

    /// <summary>Extra margin around the panel in which the self-drawn shadow lives (0 with the backdrop).</summary>
    public static double ShadowMargin => BackdropEnabled ? 0 : (double)Application.Current.FindResource("Window.ShadowMargin");

    /// <summary>Call in the constructor, after InitializeComponent and before the window is first shown.</summary>
    public static void Prepare(Window window, Border panel)
    {
        window.Background = Brushes.Transparent;

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
            panel.CornerRadius = new CornerRadius(0);
            panel.BorderThickness = new Thickness(0);
            return;
        }

        window.AllowsTransparency = true;
        panel.Margin = new Thickness(ShadowMargin);
        panel.CornerRadius = (CornerRadius)Application.Current.FindResource("Window.Radius");
        panel.BorderThickness = new Thickness(1);
        panel.SetResourceReference(Border.BorderBrushProperty, "EdgeBrush");
        panel.Effect = new DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Direction = 270, Opacity = 0.28, Color = Colors.Black };
    }
}

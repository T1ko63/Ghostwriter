using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Ghostwriter.App.Themes;
using System.Windows.Threading;
using Ghostwriter.Platform.Windowing;

namespace Ghostwriter.App.Overlay;

/// <summary>
/// Small non-activating pill: a thin progress line while the AI works, or a short error line.
/// It never takes the focus, so the user keeps typing in the target app.
/// </summary>
public partial class StatusWindow : Window
{
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InfoDuration = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _autoHide;
    private nint _hwnd;
    private bool _isProgress;

    public StatusWindow()
    {
        InitializeComponent();
        WindowSkin.Prepare(this, Panel);
        MinWidth = (double)FindResource("Status.MinWidth") + 2 * WindowSkin.ShadowMargin;
        MaxWidth = (double)FindResource("Status.MaxWidth") + 2 * WindowSkin.ShadowMargin;
        _autoHide = new DispatcherTimer { Interval = ErrorDuration };
        _autoHide.Tick += (_, _) => HideStatus();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowHelper.SetToolWindow(_hwnd, noActivate: true);
        };
    }

    /// <summary>The user clicked the pill while it was showing progress.</summary>
    public event Action? CancelRequested;

    public void ApplyLook(bool dark, WindowBackdrop backdrop, int nativeCornerPx)
    {
        WindowSkin.ApplyLook(_hwnd, dark, backdrop, nativeCornerPx);
    }

    public void Warmup()
    {
        Left = -32000;
        Top = -32000;
        Opacity = 0;
        Show();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        Hide();
        Opacity = 1;
    }

    public void ShowProgress(string text, Anchor anchor)
    {
        _isProgress = true;
        _autoHide.Interval = ErrorDuration;
        _autoHide.Stop();
        Present(text, anchor, showBar: true);
    }

    public void ShowError(string text, Anchor anchor)
    {
        _isProgress = false;
        Present(text, anchor, showBar: false, isError: true);
        _autoHide.Interval = ErrorDuration;
        _autoHide.Stop();
        _autoHide.Start();
    }

    /// <summary>A short, quiet confirmation (muted text, gone after two seconds), e.g. "Undone".</summary>
    public void ShowInfo(string text, Anchor anchor)
    {
        _isProgress = false;
        Present(text, anchor, showBar: false);
        _autoHide.Stop();
        _autoHide.Interval = InfoDuration;
        _autoHide.Start();
    }

    public void HideStatus()
    {
        _autoHide.Stop();
        _isProgress = false;
        ChunkShift.BeginAnimation(TranslateTransform.XProperty, null);
        Hide();
    }

    private void Present(string text, Anchor anchor, bool showBar, bool isError = false)
    {
        Message.Text = text;
        Message.SetResourceReference(TextBlock.ForegroundProperty, isError ? "ErrorBrush" : "MutedBrush");
        Line.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;

        // The width is set explicitly from the content (SizeToContent would keep the previous, narrower width
        // for a moment and clip a longer message); WPF still computes the height.
        var shadow = WindowSkin.ShadowMargin;
        var padding = Message.Margin.Left + Message.Margin.Right;
        Message.MaxWidth = MaxWidth - 2 * shadow - padding;
        Root.Measure(new Size(MaxWidth - 2 * shadow, double.PositiveInfinity));
        Width = Math.Max(MinWidth, Math.Min(MaxWidth, Root.DesiredSize.Width + 2 * shadow));
        UpdateLayout();

        if (showBar) StartLine(Width - 2 * shadow - Line.Margin.Left - Line.Margin.Right);
        else ChunkShift.BeginAnimation(TranslateTransform.XProperty, null);

        var (x, y) = anchor.PlaceWindow(new Size(Width, ActualHeight > 0 ? ActualHeight : Root.DesiredSize.Height + 2 * shadow));
        WindowHelper.MoveTo(_hwnd, x, y);
        if (!IsVisible)
        {
            Show();
            WindowSkin.RefreshBackdrop(_hwnd);
        }
    }

    private void StartLine(double trackWidth)
    {
        Chunk.Width = Math.Max(24, trackWidth * 0.3);
        ChunkShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-Chunk.Width, trackWidth, TimeSpan.FromMilliseconds(1100))
        {
            RepeatBehavior = RepeatBehavior.Forever,
        });
    }

    private void OnClick(object sender, MouseButtonEventArgs e)
    {
        if (_isProgress) CancelRequested?.Invoke();
        else HideStatus();
    }
}

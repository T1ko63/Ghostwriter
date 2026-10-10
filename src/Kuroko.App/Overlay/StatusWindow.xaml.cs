using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Kuroko.App.Themes;
using System.Windows.Threading;
using Kuroko.Platform.Windowing;

namespace Kuroko.App.Overlay;

/// <summary>
/// Small non-activating pill: a thin progress line while the AI works, or a short error line.
/// It never takes the focus, so the user keeps typing in the target app.
/// <para>In result mode (<see cref="BeginResult"/>) the same window becomes a text card for overlay output: it starts as
/// the progress pill, the first piece of the answer replaces the label, and the card grows with the text up to a
/// maximum height, then scrolls. The progress line runs until <see cref="EndResult"/>.</para>
/// </summary>
public partial class StatusWindow : Window
{
    private static readonly TimeSpan ErrorDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InfoDuration = TimeSpan.FromSeconds(2);

    private readonly DispatcherTimer _autoHide;
    private readonly double _pillMaxWidth;
    private nint _hwnd;
    private bool _isProgress;

    // ---- result card ----

    private enum RowKind
    {
        Text,
        Bullet,
        Gap,
    }

    private sealed record ResultRow(RowKind Kind, FrameworkElement Element, TextBlock? Body);

    private readonly StringBuilder _raw = new();
    private readonly List<ResultRow> _rows = [];
    private bool _resultMode;
    private bool _cardShown;
    private bool _flushQueued;
    private bool _userScrolled;
    private bool _wheelPending;
    private CardGrowth _growth;
    private int _fixedXPx;
    private int _fixedEdgePx;
    private double _cardInnerWidth;
    private double _cardMaxHeight;
    private Anchor _anchor;
    private double _resultWidth;
    private double _resultMaxHeight;
    private double _resultMinHeight;

    public StatusWindow()
    {
        InitializeComponent();
        WindowSkin.Prepare(this, Panel);
        MinWidth = (double)FindResource("Status.MinWidth") + 2 * WindowSkin.ShadowMargin;
        _pillMaxWidth = (double)FindResource("Status.MaxWidth") + 2 * WindowSkin.ShadowMargin;
        MaxWidth = Math.Max(_pillMaxWidth, (double)FindResource("Result.Width") + 2 * WindowSkin.ShadowMargin);
        System.Windows.Documents.TextElement.SetFontSize(ResultLines, (double)FindResource("Font.Result"));
        _resultWidth = (double)FindResource("Result.Width");
        _resultMaxHeight = (double)FindResource("Result.MaxHeight");
        _autoHide = new DispatcherTimer { Interval = ErrorDuration };
        _autoHide.Tick += (_, _) => HideStatus();
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowHelper.SetToolWindow(_hwnd, noActivate: true);
        };
    }

    /// <summary>The user clicked the pill (or card) while the AI was still working.</summary>
    public event Action? CancelRequested;

    /// <summary>The window left result mode: the card was hidden, or replaced by an error, an info or a new progress pill.</summary>
    public event Action? ResultClosed;

    /// <summary>Font size of the card text (result_font_size). Takes effect at once, also for a card that is on screen.</summary>
    public double ResultFontSize
    {
        get => System.Windows.Documents.TextElement.GetFontSize(ResultLines);
        set
        {
            if (System.Windows.Documents.TextElement.GetFontSize(ResultLines) == value) return;
            System.Windows.Documents.TextElement.SetFontSize(ResultLines, value);
            if (_cardShown && IsVisible) Flush(); // re-measure and re-place the card that is up
        }
    }

    /// <summary>Width, minimum and maximum height of the card (result_width, result_min_height, result_max_height). Used for the next card; a card on screen keeps its size.</summary>
    public void SetResultSize(double width, double minHeight, double maxHeight)
    {
        _resultWidth = width;
        _resultMinHeight = minHeight;
        _resultMaxHeight = maxHeight;
        MaxWidth = Math.Max(_pillMaxWidth, width + 2 * WindowSkin.ShadowMargin); // a wider card must not be clipped by the window
    }

    /// <summary>True from <see cref="BeginResult"/> until the card is gone.</summary>
    public bool IsResultMode => _resultMode;

    public void ApplyLook(bool dark, WindowBackdrop backdrop, int nativeCornerPx, int customRadius = 0)
    {
        WindowSkin.ApplyLook(_hwnd, dark, backdrop, nativeCornerPx, customRadius);

        // A visible window (card on screen while the configuration changes) gets its new material drawn right away.
        if (IsVisible) WindowSkin.RefreshBackdrop(_hwnd);
    }

    public void Warmup()
    {
        Left = -32000;
        Top = -32000;
        Opacity = 0;
        Show();
        Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        WarmupResult();
        Hide();
        Opacity = 1;
    }

    /// <summary>Builds and lays out a few card rows once (template, fonts, JIT) so the first real card does not pay for it.</summary>
    private void WarmupResult()
    {
        UpdateRows("- warm up\nwarm up\n\n- warm up");
        Message.Visibility = Visibility.Collapsed;
        ResultScroll.Visibility = Visibility.Visible;
        UpdateLayout();
        ResetResult();
    }

    public void ShowProgress(string text, Anchor anchor)
    {
        LeaveResultMode();
        _isProgress = true;
        _autoHide.Interval = ErrorDuration;
        _autoHide.Stop();
        Present(text, anchor, showBar: true);
    }

    public void ShowError(string text, Anchor anchor)
    {
        LeaveResultMode();
        _isProgress = false;
        Present(text, anchor, showBar: false, isError: true);
        _autoHide.Interval = ErrorDuration;
        _autoHide.Stop();
        _autoHide.Start();
    }

    /// <summary>A short, quiet confirmation (muted text, gone after two seconds unless <paramref name="longer"/>), e.g. "Undone".</summary>
    public void ShowInfo(string text, Anchor anchor, bool longer = false)
    {
        LeaveResultMode();
        _isProgress = false;
        Present(text, anchor, showBar: false);
        _autoHide.Stop();
        _autoHide.Interval = longer ? ErrorDuration : InfoDuration;
        _autoHide.Start();
    }

    public void HideStatus()
    {
        _autoHide.Stop();
        _isProgress = false;
        ChunkShift.BeginAnimation(TranslateTransform.XProperty, null);
        Hide();
        LeaveResultMode();
    }

    // ---- result mode ----

    /// <summary>
    /// Shows the progress pill for an answer that will arrive as a text card. The card's position is decided here, once,
    /// for its maximum size: it then grows towards the side with room (down, or up when it sits above the anchor), so it
    /// neither jumps nor leaves the screen.
    /// </summary>
    public void BeginResult(string label, Anchor anchor)
    {
        LeaveResultMode();
        _resultMode = true;
        _anchor = anchor;
        _isProgress = true;
        _autoHide.Stop();
        PlanCard(anchor);
        Present(label, anchor, showBar: true);
    }

    /// <summary>Appends a piece of the answer. The first piece turns the pill into the card; later ones are drawn once per frame.</summary>
    public void AppendResult(string piece)
    {
        if (!_resultMode || piece.Length == 0) return;
        _raw.Append(piece);

        if (!_cardShown)
        {
            Flush(); // the first text is shown immediately
            return;
        }

        if (_flushQueued) return;
        _flushQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, Flush);
    }

    /// <summary>The answer is complete: draws the rest and removes the progress line (the signal that it is done).</summary>
    public void EndResult()
    {
        if (!_resultMode) return;
        _isProgress = false;
        Flush();
        ChunkShift.BeginAnimation(TranslateTransform.XProperty, null);
        Line.Visibility = Visibility.Collapsed;
    }

    private void LeaveResultMode()
    {
        if (!_resultMode) return;
        ResetResult();
        ResultClosed?.Invoke();
    }

    private void ResetResult()
    {
        _resultMode = false;
        _cardShown = false;
        _flushQueued = false;
        _userScrolled = false;
        _raw.Clear();
        _rows.Clear();
        ResultLines.Children.Clear();
        ResultScroll.Visibility = Visibility.Collapsed;
        ResultScroll.ScrollToTop();
        Message.Visibility = Visibility.Visible;
    }

    /// <summary>Decides width, maximum height, position and growth direction of the card for this anchor.</summary>
    private void PlanCard(Anchor anchor)
    {
        var shadow = WindowSkin.ShadowMargin;
        var scale = anchor.Monitor.Scale;
        var work = anchor.Monitor.WorkArea;
        _cardInnerWidth = Math.Min(_resultWidth, work.Width / scale - 2 * anchor.MarginDip - 2 * shadow);
        _cardMaxHeight = Math.Min(_resultMaxHeight + 2 * shadow, work.Height / scale - 2 * anchor.MarginDip);
        var chrome = 2 * shadow + Panel.BorderThickness.Top + Panel.BorderThickness.Bottom;
        ResultScroll.MaxHeight = Math.Max(40, _cardMaxHeight - chrome);
        ResultScroll.MinHeight = Math.Max(0, Math.Min(_resultMinHeight + 2 * shadow, _cardMaxHeight) - chrome);

        var widthPx = (int)Math.Ceiling((_cardInnerWidth + 2 * shadow) * scale);
        var maxHeightPx = (int)Math.Ceiling(_cardMaxHeight * scale);

        if (anchor.Spot is { } spot)
        {
            // A fixed place: the card grows away from the screen edge it sits at.
            var placement = CardSpots.Place(spot, anchor.Monitor, widthPx, maxHeightPx, (int)Math.Round(anchor.MarginDip * scale));
            (_fixedXPx, _fixedEdgePx, _growth) = (placement.X, placement.Edge, placement.Growth);
            return;
        }

        // Following the text: placed above the anchor (it did not fit below) means keep the bottom edge and grow upwards.
        var (x, y) = anchor.PlaceWindow(new Size(_cardInnerWidth + 2 * shadow, _cardMaxHeight));
        var growUp = y + maxHeightPx <= anchor.Point.Y + 1;
        _growth = growUp ? CardGrowth.Up : CardGrowth.Down;
        _fixedXPx = x;
        _fixedEdgePx = growUp ? y + maxHeightPx : y;
    }

    /// <summary>Top-left position in physical pixels for the card at the given height.</summary>
    private (int X, int Y) CardPosition(double heightDip)
    {
        var heightPx = (int)Math.Ceiling(heightDip * _anchor.Monitor.Scale);
        return (_fixedXPx, new CardPlacement(_fixedXPx, _fixedEdgePx, _growth).TopFor(heightPx));
    }

    private void Flush()
    {
        _flushQueued = false;
        if (!_resultMode) return;

        UpdateRows(_raw.ToString());
        var switching = !_cardShown;
        if (switching)
        {
            _cardShown = true;
            Message.Visibility = Visibility.Collapsed;
            ResultScroll.Visibility = Visibility.Visible;
            Width = _cardInnerWidth + 2 * WindowSkin.ShadowMargin;
        }

        UpdateLayout();
        if (switching) StartLine(Width - 2 * WindowSkin.ShadowMargin - Line.Margin.Left - Line.Margin.Right);

        if (switching || _growth != CardGrowth.Down)
        {
            var (x, y) = CardPosition(ActualHeight);
            WindowHelper.MoveTo(_hwnd, x, y);
        }

        if (!_userScrolled) ResultScroll.ScrollToEnd();
    }

    /// <summary>Turns the raw text into rows: "- " lines become bullets with a hanging indent, empty lines a small gap.</summary>
    private void UpdateRows(string text)
    {
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            var last = i == lines.Length - 1;
            RowKind kind;
            string body;
            if (line.Length == 0)
            {
                (kind, body) = (RowKind.Gap, string.Empty);
            }
            else if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                (kind, body) = (RowKind.Bullet, line[2..]);
            }
            else if (last && line == "-")
            {
                (kind, body) = (RowKind.Bullet, string.Empty); // the space of "- " is still on its way
            }
            else
            {
                (kind, body) = (RowKind.Text, line);
            }

            if (i < _rows.Count && _rows[i].Kind == kind)
            {
                if (_rows[i].Body is { } existing && existing.Text != body) existing.Text = body;
                continue;
            }

            var row = CreateRow(kind, body, first: i == 0);
            if (i < _rows.Count)
            {
                ResultLines.Children[i] = row.Element;
                _rows[i] = row;
            }
            else
            {
                ResultLines.Children.Add(row.Element);
                _rows.Add(row);
            }
        }
    }

    private ResultRow CreateRow(RowKind kind, string body, bool first)
    {
        var gap = (double)FindResource("Result.LineGap");
        var margin = new Thickness(0, first ? 0 : gap, 0, 0);
        if (kind == RowKind.Gap)
        {
            return new ResultRow(kind, new Border { Height = (double)FindResource("Result.ParagraphGap") - gap, Margin = margin }, null);
        }

        var text = NewText(body);
        if (kind == RowKind.Text)
        {
            text.Margin = margin;
            return new ResultRow(kind, text, text);
        }

        var bullet = NewText("•");
        bullet.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
        var grid = new Grid { Margin = margin };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)FindResource("Result.BulletIndent")) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(text, 1);
        grid.Children.Add(bullet);
        grid.Children.Add(text);
        return new ResultRow(kind, grid, text);
    }

    private static TextBlock NewText(string text)
    {
        // No FontSize here: the text inherits it from ResultLines, so ResultFontSize changes the whole card at once.
        var block = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return block;
    }

    // Auto-scroll follows the text while it streams, until the user scrolls away from the end; scrolling back down resumes it.

    private void OnResultWheel(object sender, MouseWheelEventArgs e) => _wheelPending = true;

    private void OnResultScrolled(object sender, ScrollChangedEventArgs e)
    {
        var byUser = _wheelPending || Mouse.Captured is ScrollBar or Thumb or RepeatButton;
        _wheelPending = false;
        if (byUser && e.VerticalChange != 0)
        {
            _userScrolled = ResultScroll.VerticalOffset < ResultScroll.ScrollableHeight - 2;
        }
    }

    // ---- pill ----

    private void Present(string text, Anchor anchor, bool showBar, bool isError = false)
    {
        Message.Text = text;
        Message.SetResourceReference(TextBlock.ForegroundProperty, isError ? "ErrorBrush" : "MutedBrush");
        Line.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;

        // The width is set explicitly from the content (SizeToContent would keep the previous, narrower width
        // for a moment and clip a longer message); WPF still computes the height.
        var shadow = WindowSkin.ShadowMargin;
        var padding = Message.Margin.Left + Message.Margin.Right;
        Message.MaxWidth = _pillMaxWidth - 2 * shadow - padding;
        Root.Measure(new Size(_pillMaxWidth - 2 * shadow, double.PositiveInfinity));
        Width = Math.Max(MinWidth, Math.Min(_pillMaxWidth, Root.DesiredSize.Width + 2 * shadow));
        UpdateLayout();

        if (showBar) StartLine(Width - 2 * shadow - Line.Margin.Left - Line.Margin.Right);
        else ChunkShift.BeginAnimation(TranslateTransform.XProperty, null);

        var height = ActualHeight > 0 ? ActualHeight : Root.DesiredSize.Height + 2 * shadow;
        var (x, y) = _resultMode ? CardPosition(height) : anchor.PlaceWindow(new Size(Width, height));
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
        // Dragging the scrollbar of a card must not close it.
        if (_resultMode && e.OriginalSource is DependencyObject source && IsInsideScrollBar(source)) return;

        if (_isProgress) CancelRequested?.Invoke();
        else HideStatus();
    }

    private static bool IsInsideScrollBar(DependencyObject node)
    {
        for (; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
        {
            if (node is ScrollBar) return true;
        }

        return false;
    }
}

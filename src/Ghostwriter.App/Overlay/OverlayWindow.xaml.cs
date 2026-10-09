using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Ghostwriter.App.Themes;
using Ghostwriter.Core.Localization;
using Ghostwriter.Core.Prompts;
using Ghostwriter.Core.Search;
using Ghostwriter.Platform.Windowing;

namespace Ghostwriter.App.Overlay;

public sealed record PromptRow(PromptDefinition Prompt, string Number)
{
    public string Name => Prompt.Name;

    /// <summary>The prompt's own hotkey, shown right-aligned as subtle text; empty when it has none.</summary>
    public string Hotkey => Prompt.Hotkey ?? string.Empty;
}

/// <summary>
/// The prompt picker. Created once at start-up and only shown/hidden afterwards. It is shown without
/// taking the focus and activated separately (see <see cref="ActivateForInput"/>), so the hotkey handler
/// can finish reading the target field before the focus moves.
/// </summary>
public partial class OverlayWindow : Window
{
    private IReadOnlyList<PromptDefinition> _all = [];
    private List<PromptRow> _rows = [];
    private nint _hwnd;
    private bool _activated;
    private bool _hiding;

    // Size and font from settings.toml (overlay_width, overlay_min_height, overlay_max_height, overlay_font_size).
    private double _width;
    private double _minHeight;
    private double _maxHeight;
    private readonly double _baseFont, _baseSearch, _baseHotkey, _baseNumber, _baseRowHeight, _baseSearchHeight;

    // Set when the picker has a fixed place: it then keeps its fixed edge while the list gets shorter or longer.
    private bool _spotted;
    private CardPlacement _placement;
    private double _placementScale = 1;

    public OverlayWindow()
    {
        InitializeComponent();
        Placeholder.Text = Loc.Get("search_placeholder");
        NoResults.Text = Loc.Get("no_results");
        WindowSkin.Prepare(this, Panel);
        _baseFont = (double)FindResource("Font.Row");
        _baseSearch = (double)FindResource("Font.Search");
        _baseHotkey = (double)FindResource("Font.Hotkey");
        _baseNumber = (double)FindResource("Font.Number");
        _baseRowHeight = (double)FindResource("Row.Height");
        _baseSearchHeight = (double)FindResource("Search.Height");
        _width = (double)FindResource("Overlay.Width");
        _maxHeight = (double)FindResource("List.MaxHeight") + _baseSearchHeight + 1 + 14 + 2;
        Width += 2 * WindowSkin.ShadowMargin;
        SizeChanged += OnSizeChanged;
        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            WindowHelper.SetToolWindow(_hwnd, noActivate: false);
        };

        // The scrollbar only shows while the list moves and fades out (without animation) shortly after.
        _scrollBarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _scrollBarTimer.Tick += (_, _) =>
        {
            _scrollBarTimer.Stop();
            if (_scrollBar is not null) _scrollBar.Opacity = 0;
        };
        List.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(OnListScrolled));
    }

    private readonly DispatcherTimer _scrollBarTimer;
    private ScrollBar? _scrollBar;
    private long _quietUntil;

    private void OnListScrolled(object sender, ScrollChangedEventArgs e)
    {
        // Resetting the list while the overlay opens is not "scrolling" the user did.
        if (e.VerticalChange == 0 || Environment.TickCount64 < _quietUntil) return;
        if (_scrollBar is null && List.Template.FindName("Scroller", List) is ScrollViewer viewer)
        {
            _scrollBar = viewer.Template.FindName("PART_VerticalScrollBar", viewer) as ScrollBar;
        }

        if (_scrollBar is null) return;
        _scrollBar.Opacity = 1;
        _scrollBarTimer.Stop();
        _scrollBarTimer.Start();
    }

    /// <summary>The user picked a prompt (Enter, click or digit).</summary>
    public event Action<PromptDefinition>? PromptChosen;

    /// <summary>The user pressed Esc.</summary>
    public event Action? Cancelled;

    /// <summary>The overlay lost the focus because the user clicked or switched elsewhere.</summary>
    public event Action? Dismissed;

    public bool IsShown => IsVisible && !_hiding;

    /// <summary>Creates the native window and renders the first frame off-screen, so the first real show is instant.</summary>
    public void Warmup(IReadOnlyList<PromptDefinition> prompts)
    {
        Present(prompts);
        Left = -32000;
        Top = -32000;
        Opacity = 0;
        Show();
        Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
        Hide();
        Opacity = 1;
    }

    /// <summary>Resets the filter and loads the prompt list. Call before <see cref="ShowAt"/>.</summary>
    public void Present(IReadOnlyList<PromptDefinition> prompts)
    {
        _quietUntil = Environment.TickCount64 + 250;
        _all = prompts;
        Search.Text = string.Empty;
        Refresh();
    }

    public void ApplyLook(bool dark, WindowBackdrop backdrop, int nativeCornerPx, int customRadius = 0)
    {
        WindowSkin.ApplyLook(_hwnd, dark, backdrop, nativeCornerPx, customRadius);
    }

    /// <summary>
    /// Width, height limits and font size of the picker (overlay_width, overlay_min_height, overlay_max_height, overlay_font_size).
    /// The font size scales the whole picker (rows, search line, numbers, hotkeys) in proportion. Applies at once to the resources;
    /// the size takes effect the next time the picker opens.
    /// </summary>
    public void SetLayout(double width, double minHeight, double maxHeight, double fontSize)
    {
        _width = width;
        _minHeight = minHeight;
        _maxHeight = maxHeight;

        var factor = fontSize / _baseFont;
        var resources = Application.Current.Resources;
        resources["Font.Row"] = fontSize;
        resources["Font.Search"] = _baseSearch * factor;
        resources["Font.Hotkey"] = _baseHotkey * factor;
        resources["Font.Number"] = _baseNumber * factor;
        resources["Row.Height"] = Math.Round(_baseRowHeight * factor);
        resources["Search.Height"] = Math.Round(_baseSearchHeight * factor);
    }

    /// <summary>
    /// Sizes the picker for the monitor of the anchor (never larger than the monitor minus the margin), places it and shows it
    /// without activating it. With a fixed place the fixed edge is decided once for the maximum height, and the picker grows away
    /// from the screen edge when the list gets longer; otherwise it opens near the text cursor or the mouse as before.
    /// </summary>
    public void ShowAnchored(Anchor anchor)
    {
        var shadow = WindowSkin.ShadowMargin;
        var scale = anchor.Monitor.Scale;
        var work = anchor.Monitor.WorkArea;
        var innerWidth = Math.Min(_width, work.Width / scale - 2 * anchor.MarginDip - 2 * shadow);
        var totalMax = Math.Min(_maxHeight + 2 * shadow, work.Height / scale - 2 * anchor.MarginDip);
        var totalMin = Math.Min(_minHeight + 2 * shadow, totalMax);

        // What is not the list: shadow, border, search line, separator, list padding.
        var listPadding = (Thickness)FindResource("List.Padding");
        var chrome = 2 * shadow + Panel.BorderThickness.Top + Panel.BorderThickness.Bottom
            + (double)FindResource("Search.Height") + 1 + listPadding.Top + listPadding.Bottom;
        List.MaxHeight = Math.Max((double)FindResource("Row.Height") + 2, totalMax - chrome);
        List.MinHeight = Math.Max(0, totalMin - chrome);
        Width = innerWidth + 2 * shadow;

        var size = MeasureDesired();
        _placementScale = scale;
        if (anchor.Spot is { } spot)
        {
            var maxPx = (int)Math.Ceiling(totalMax * scale);
            _placement = CardSpots.Place(spot, anchor.Monitor, (int)Math.Ceiling(Width * scale), maxPx, (int)Math.Round(anchor.MarginDip * scale));
            _spotted = true;
            ShowAt(_placement.X, _placement.TopFor((int)Math.Ceiling(size.Height * scale)));
            return;
        }

        _spotted = false;
        var (x, y) = anchor.PlaceWindow(size);
        ShowAt(x, y);
    }

    // The list gets shorter or longer while the user types; a picker with a fixed edge stays attached to it.
    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_spotted || !IsVisible || _placement.Growth == CardGrowth.Down || e.HeightChanged is false) return;
        WindowHelper.MoveTo(_hwnd, _placement.X, _placement.TopFor((int)Math.Ceiling(e.NewSize.Height * _placementScale)));
    }

    /// <summary>Desired size in device-independent units, for placement before the window is visible.</summary>
    public Size MeasureDesired()
    {
        Measure(new Size(Width, double.PositiveInfinity));
        return DesiredSize;
    }

    /// <summary>Shows the overlay at physical pixel coordinates without activating it.</summary>
    public void ShowAt(int x, int y)
    {
        _hiding = false;
        _activated = false;
        _quietUntil = Environment.TickCount64 + 250;
        WindowHelper.MoveTo(_hwnd, x, y);
        Show();
    }

    /// <summary>Takes the keyboard focus. Call once the target field has been read.</summary>
    public void ActivateForInput()
    {
        if (!IsShown) return;
        WindowHelper.Activate(_hwnd);
        WindowSkin.RefreshBackdrop(_hwnd);
        Search.Focus();
        Keyboard.Focus(Search);
        _activated = true;
    }

    public void HideOverlay()
    {
        _hiding = true;
        Hide();
        _activated = false;
    }

    // What the list currently shows; opening the overlay again with the same prompts must not rebuild it.
    private IReadOnlyList<PromptDefinition>? _renderedAll;
    private string? _renderedQuery;

    private void Refresh()
    {
        var query = Search.Text.Trim();
        Placeholder.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (ReferenceEquals(_all, _renderedAll) && query == _renderedQuery)
        {
            // Same prompts, same filter (e.g. the overlay is opened again): only reset the selection.
            List.SelectedIndex = _rows.Count > 0 ? 0 : -1;
            if (_rows.Count > 0) List.ScrollIntoView(_rows[0]);
            return;
        }

        var matches = FuzzyMatcher.Filter(_all, query, p => p.Name);
        _rows = matches.Select((p, i) => new PromptRow(p, query.Length == 0 && i < 9 ? (i + 1).ToString() : string.Empty)).ToList();
        _renderedAll = _all;
        _renderedQuery = query;
        List.ItemsSource = _rows;
        List.SelectedIndex = _rows.Count > 0 ? 0 : -1;
        NoResults.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

    private void Move(int delta)
    {
        if (_rows.Count == 0) return;
        var index = Math.Clamp(List.SelectedIndex + delta, 0, _rows.Count - 1);
        List.SelectedIndex = index;
        List.ScrollIntoView(_rows[index]);
    }

    private void Choose(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        PromptChosen?.Invoke(_rows[index].Prompt);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        switch (e.Key)
        {
            case Key.Escape:
                Cancelled?.Invoke();
                e.Handled = true;
                return;
            case Key.Down:
            case Key.Tab when modifiers == ModifierKeys.None:
                Move(1);
                e.Handled = true;
                return;
            case Key.Up:
                Move(-1);
                e.Handled = true;
                return;
            case Key.PageDown:
                Move(5);
                e.Handled = true;
                return;
            case Key.PageUp:
                Move(-5);
                e.Handled = true;
                return;
            case Key.Enter:
                Choose(List.SelectedIndex);
                e.Handled = true;
                return;
        }

        // Digits are quick-select only while nothing has been typed; afterwards they belong to the search text.
        if (Search.Text.Length == 0 && modifiers == ModifierKeys.None)
        {
            var digit = e.Key is >= Key.D1 and <= Key.D9 ? e.Key - Key.D1
                : e.Key is >= Key.NumPad1 and <= Key.NumPad9 ? e.Key - Key.NumPad1
                : -1;
            if (digit >= 0)
            {
                Choose(digit);
                e.Handled = true;
            }
        }
    }

    private void OnListClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && ItemsControlContainer(source) is { DataContext: PromptRow row })
        {
            PromptChosen?.Invoke(row.Prompt);
            e.Handled = true;
        }
    }

    private static System.Windows.Controls.ListBoxItem? ItemsControlContainer(DependencyObject source)
    {
        for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is System.Windows.Controls.ListBoxItem item) return item;
        }

        return null;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Only a window that really was active counts: the non-activating show must not look like a dismissal.
        if (_activated && !_hiding && IsVisible) Dismissed?.Invoke();
    }
}

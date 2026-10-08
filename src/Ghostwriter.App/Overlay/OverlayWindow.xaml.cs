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

    public OverlayWindow()
    {
        InitializeComponent();
        Placeholder.Text = Loc.Get("search_placeholder");
        NoResults.Text = Loc.Get("no_results");
        WindowSkin.Prepare(this, Panel);
        Width += 2 * WindowSkin.ShadowMargin;
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

    public nint Handle => _hwnd;

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

    public void ApplyLook(bool dark, WindowBackdrop backdrop, int nativeCornerPx)
    {
        WindowSkin.ApplyLook(_hwnd, dark, backdrop, nativeCornerPx);
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

    private System.Windows.Controls.ListBoxItem? ItemsControlContainer(DependencyObject source)
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

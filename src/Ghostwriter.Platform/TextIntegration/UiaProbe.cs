using System.Windows.Automation;
using System.Windows.Automation.Text;
using Ghostwriter.Core.Diagnostics;

namespace Ghostwriter.Platform.TextIntegration;

/// <summary>What UI Automation knows about the focused element.</summary>
public sealed record UiaFocusInfo(
    string ControlType,
    bool IsPassword,
    bool HasTextPattern,
    bool SelectionKnown,
    string? SelectedText,
    string? WholeText,
    bool IsEditable,
    bool IsReadOnly,
    bool TooLong,
    ScreenPoint? CaretPoint = null)
{
    /// <summary>The focused element itself, kept so the field can be re-selected and verified before pasting.</summary>
    internal AutomationElement? Element { get; init; }

    /// <summary>
    /// Chromium/Gecko-based UI (browsers, Electron apps such as VS Code/VSCodium). Their "Edit" element is often a
    /// hidden helper textarea (Monaco) whose text and selection do not describe the real document.
    /// </summary>
    public bool IsWebEngine { get; init; }

    /// <summary>Diagnostics only: lets the log show which kind of element a fast/slow or wrong capture came from (never its name or text).</summary>
    public string? ElementClass { get; init; }

    public string? FrameworkId { get; init; }

    // Controls where Ctrl+A selects items, not text.
    private static readonly HashSet<string> NonTextControls = new(StringComparer.Ordinal)
    {
        "ControlType.ListItem", "ControlType.TreeItem", "ControlType.DataItem", "ControlType.Button",
        "ControlType.MenuItem", "ControlType.TabItem", "ControlType.Image", "ControlType.CheckBox",
        "ControlType.RadioButton", "ControlType.Hyperlink", "ControlType.SplitButton",
    };

    public bool IsNonTextControl => NonTextControls.Contains(ControlType);
}

/// <summary>
/// Reads the focused element through UI Automation. Where a text pattern exists and reports a
/// selection, the text is obtained without touching the clipboard. UIA calls can hang in misbehaving
/// apps, so they run on the thread pool under a hard time budget.
/// </summary>
internal static class UiaProbe
{
    /// <summary>Texts longer than this are rejected; also bounds how much UIA is asked to copy.</summary>
    public const int MaxInputChars = 50_000;

    public static async Task<UiaFocusInfo?> InspectAsync(TargetInfo target, TimeSpan budget)
    {
        var work = Task.Run(() => Inspect(target));
        var finished = await Task.WhenAny(work, Task.Delay(budget));
        if (finished != work)
        {
            AppLog.Warn($"UIA probe timed out after {budget.TotalMilliseconds:F0} ms ({target.ProcessName}).");
            ObserveAbandoned(work); // the abandoned call is left to finish on its own
            return null;
        }

        return await work;
    }

    /// <summary>Selects the whole content through UIA (no keystroke) and checks that the selection equals <paramref name="expected"/>.</summary>
    public static Task<bool> SelectAllVerifiedAsync(AutomationElement element, string expected, TimeSpan budget)
        => WithBudget(() =>
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return false;
            var text = (TextPattern)pattern;
            text.DocumentRange.Select();
            return SelectionEquals(text, expected);
        }, budget);

    /// <summary>Polls until the element's selection equals <paramref name="expected"/> (e.g. after a Ctrl+A keystroke).</summary>
    public static Task<bool> WaitForSelectionAsync(AutomationElement element, string expected, TimeSpan budget)
        => WithBudget(() =>
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return false;
            var text = (TextPattern)pattern;
            var deadline = Environment.TickCount64 + (long)budget.TotalMilliseconds;
            while (true)
            {
                if (SelectionEquals(text, expected)) return true;
                if (Environment.TickCount64 >= deadline) return false;
                Thread.Sleep(10);
            }
        }, budget + TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// Where the text cursor (or the end of the selection) is on screen: bottom-left of its last rectangle, in
    /// physical pixels. Browsers and Electron apps have no Win32 caret, but their text ranges know their geometry.
    /// </summary>
    private static ScreenPoint? CaretOf(TextPatternRange range)
    {
        try
        {
            var rects = range.GetBoundingRectangles();
            if (rects.Length == 0)
            {
                // A collapsed range (just a caret) often reports no rectangle: use the character around it.
                var clone = range.Clone();
                clone.ExpandToEnclosingUnit(TextUnit.Character);
                rects = clone.GetBoundingRectangles();
            }

            if (rects.Length == 0) return null;
            var rect = rects[^1];
            if (rect.IsEmpty || double.IsInfinity(rect.Left) || double.IsInfinity(rect.Bottom)) return null;
            return new ScreenPoint((int)Math.Round(rect.Left), (int)Math.Round(rect.Bottom));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException
            or ElementNotAvailableException or ArgumentException)
        {
            return null; // the position is a nicety; never let it break reading the text
        }
    }

    private static bool SelectionEquals(TextPattern text, string expected)
    {
        var ranges = text.GetSelection();
        if (ranges.Length == 0) return false;

        var selected = string.Concat(ranges.Select(r => r.GetText(MaxInputChars + 1)));
        if (selected == expected) return true;

        // Controls disagree on line endings and a trailing break between "document" and "selection"; that is
        // still the whole field. Anything else (a shorter selection) is not.
        if (Normalize(selected) == Normalize(expected)) return true;

        AppLog.Info($"UIA select-all mismatch: selection has {selected.Length} chars, field has {expected.Length}.");
        return false;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n', ' ');

    /// <summary>
    /// Whether <paramref name="element"/> still has the keyboard focus. Null when UIA cannot tell in time
    /// (the caller then keeps its previous behaviour instead of guessing).
    /// </summary>
    public static Task<bool?> HasFocusAsync(AutomationElement element, TimeSpan budget)
        => WithBudgetOrUnknown(() => AutomationElement.FocusedElement is { } focused && Automation.Compare(focused, element), budget);

    /// <summary>Whether the element's selection is still <paramref name="expected"/>. Null when UIA cannot tell in time.</summary>
    public static Task<bool?> SelectionStillAsync(AutomationElement element, string expected, TimeSpan budget)
        => WithBudgetOrUnknown(() =>
            element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern) ? SelectionEquals((TextPattern)pattern, expected) : null, budget);

    private static async Task<bool> WithBudget(Func<bool> work, TimeSpan budget)
        => await WithBudgetOrUnknown(() => work(), budget) == true;

    private static async Task<bool?> WithBudgetOrUnknown(Func<bool?> work, TimeSpan budget)
    {
        var task = Task.Run(() =>
        {
            try
            {
                return work();
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException
                or System.Runtime.InteropServices.COMException or TimeoutException)
            {
                AppLog.Warn($"UIA check failed: {ex.GetType().Name}");
                return null;
            }
        });

        if (await Task.WhenAny(task, Task.Delay(budget)) == task) return await task;
        ObserveAbandoned(task);
        return null;
    }

    /// <summary>A call that ran out of budget keeps running; if it fails later, log it instead of leaving an unobserved exception.</summary>
    private static void ObserveAbandoned(Task task)
        => task.ContinueWith(
            t => AppLog.Warn($"Abandoned UIA call failed later: {t.Exception?.GetBaseException().GetType().Name}"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <summary>Loads the UIA assemblies and COM server once, in the background, so the first hotkey is not slow.</summary>
    public static void Warmup() => Task.Run(() =>
    {
        try
        {
            _ = AutomationElement.RootElement;
            _ = AutomationElement.FocusedElement;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"UIA warmup failed: {ex.Message}");
        }
    });

    private static UiaFocusInfo? Inspect(TargetInfo target)
    {
        try
        {
            var element = AutomationElement.FocusedElement;
            if (element is null) return null;

            var current = element.Current;
            // A field is "editable" if it has a writable value (browser inputs, Win32 edits), is a plain Edit
            // control, or is a native Document (RichEdit, Word, WinForms multiline). In web engines a Document is
            // the read-only page itself, so it does not count.
            var isReadOnly = false;
            var hasWritableValue = false;
            if (element.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePattern))
            {
                isReadOnly = ((ValuePattern)valuePattern).Current.IsReadOnly;
                hasWritableValue = !isReadOnly;
            }

            var controlType = current.ControlType.ProgrammaticName;
            var isWebEngine = current.FrameworkId is "Chrome" or "Gecko" or "Mozilla";
            var isEditable = hasWritableValue
                || (controlType == "ControlType.Edit" && !isReadOnly)
                || (controlType == "ControlType.Document" && !isWebEngine && !isReadOnly);

            string? selected = null;
            string? whole = null;
            var hasText = false;
            var selectionKnown = false;
            var tooLong = false;
            ScreenPoint? caretPoint = null;
            if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
            {
                hasText = true;
                var textPattern = (TextPattern)pattern;
                var ranges = textPattern.GetSelection();
                if (ranges.Length > 0) caretPoint = CaretOf(ranges[0]);

                // Zero ranges means the app does not report selection at all: that is "unknown", not "empty".
                selectionKnown = ranges.Length > 0;
                if (selectionKnown)
                {
                    selected = string.Concat(ranges.Select(r => r.GetText(MaxInputChars + 1)));
                    if (selected.Length > MaxInputChars)
                    {
                        tooLong = true;
                        selected = null;
                    }
                    else if (selected.Length == 0 && isEditable)
                    {
                        // Caret only: the whole content is the input.
                        var all = textPattern.DocumentRange.GetText(MaxInputChars + 1);
                        tooLong = all.Length > MaxInputChars;
                        whole = tooLong ? null : all;
                    }
                }
            }

            return new UiaFocusInfo(controlType, current.IsPassword, hasText, selectionKnown, selected, whole, isEditable, isReadOnly, tooLong, caretPoint)
            {
                Element = element,
                IsWebEngine = isWebEngine,
                ElementClass = current.ClassName,
                FrameworkId = current.FrameworkId,
            };
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException
            or System.Runtime.InteropServices.COMException or TimeoutException)
        {
            AppLog.Warn($"UIA probe failed for {target.ProcessName}: {ex.GetType().Name}");
            return null;
        }
    }
}

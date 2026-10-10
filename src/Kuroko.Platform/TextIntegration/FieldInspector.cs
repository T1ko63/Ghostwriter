using System.Runtime.InteropServices;
using System.Windows.Automation;
using static Kuroko.Platform.Native.NativeMethods;

namespace Kuroko.Platform.TextIntegration;

/// <summary>
/// An opaque handle on a UI Automation element (the focused field at hotkey time). Only the inspector that handed it out
/// can do anything with it; everything else just passes it along.
/// </summary>
public class FieldElement
{
    internal FieldElement()
    {
    }
}

/// <summary>A <see cref="FieldElement"/> backed by a real UI Automation element.</summary>
internal sealed class UiaFieldElement(AutomationElement element) : FieldElement
{
    public AutomationElement Element { get; } = element;
}

/// <summary>
/// What can be asked about the target window and its focused field right now: UI Automation and the Win32 focus.
/// <see cref="UiaFieldInspector"/> asks Windows; tests script the answers. Every call that needs UIA runs under a time
/// budget, and a null answer means "could not tell in time".
/// </summary>
public interface IFieldInspector
{
    /// <summary>What UI Automation knows about the focused element; null when UIA could not tell in time.</summary>
    Task<UiaFocusInfo?> InspectAsync(TargetInfo target, TimeSpan budget);

    /// <summary>Selects the whole content without a key press and checks that the selection equals <paramref name="expected"/>.</summary>
    Task<bool> SelectAllVerifiedAsync(FieldElement element, string expected, TimeSpan budget);

    /// <summary>Waits until the selection equals <paramref name="expected"/> (e.g. after a Ctrl+A keystroke).</summary>
    Task<bool> WaitForSelectionAsync(FieldElement element, string expected, TimeSpan budget);

    /// <summary>Whether <paramref name="element"/> still has the keyboard focus.</summary>
    Task<bool?> HasFocusAsync(FieldElement element, TimeSpan budget);

    /// <summary>Whether the element's selection is still <paramref name="expected"/>.</summary>
    Task<bool?> SelectionStillAsync(FieldElement element, string expected, TimeSpan budget);

    /// <summary>Whether <paramref name="window"/> is the foreground window.</summary>
    bool IsForeground(nint window);

    /// <summary>Whether the focused child window is still the one from hotkey time; null when that cannot be told.</summary>
    bool? FocusWindowMatches(TargetInfo target);
}

/// <summary>The real <see cref="IFieldInspector"/>: UI Automation through <see cref="UiaProbe"/>, focus through Win32.</summary>
public sealed class UiaFieldInspector : IFieldInspector
{
    public Task<UiaFocusInfo?> InspectAsync(TargetInfo target, TimeSpan budget) => UiaProbe.InspectAsync(target, budget);

    public Task<bool> SelectAllVerifiedAsync(FieldElement element, string expected, TimeSpan budget)
        => UiaProbe.SelectAllVerifiedAsync(Unwrap(element), expected, budget);

    public Task<bool> WaitForSelectionAsync(FieldElement element, string expected, TimeSpan budget)
        => UiaProbe.WaitForSelectionAsync(Unwrap(element), expected, budget);

    public Task<bool?> HasFocusAsync(FieldElement element, TimeSpan budget) => UiaProbe.HasFocusAsync(Unwrap(element), budget);

    public Task<bool?> SelectionStillAsync(FieldElement element, string expected, TimeSpan budget)
        => UiaProbe.SelectionStillAsync(Unwrap(element), expected, budget);

    public bool IsForeground(nint window) => GetForegroundWindow() == window;

    public bool? FocusWindowMatches(TargetInfo target)
    {
        if (target.FocusWindow == 0) return null;
        var info = new GUITHREADINFO { cbSize = (uint)Marshal.SizeOf<GUITHREADINFO>() };
        if (!GetGUIThreadInfo(target.ThreadId, ref info) || info.hwndFocus == 0) return null;
        return info.hwndFocus == target.FocusWindow;
    }

    private static AutomationElement Unwrap(FieldElement element) => ((UiaFieldElement)element).Element;
}

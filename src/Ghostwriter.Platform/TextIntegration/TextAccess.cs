namespace Ghostwriter.Platform.TextIntegration;

[Flags]
public enum ReadStrategy
{
    None = 0,
    Uia = 1,
    Clipboard = 2,
    Auto = Uia | Clipboard,
}

public enum TextOrigin
{
    /// <summary>The user's selection.</summary>
    Selection,

    /// <summary>Nothing was selected, so the whole field content was taken.</summary>
    WholeField,
}

/// <summary>What the captured text is for. Decides which reading steps and checks are allowed (see <see cref="CapturePolicy"/>).</summary>
public enum CaptureMode
{
    /// <summary>The result will replace the text: the field must be writable, and with nothing selected the whole field may be selected (Ctrl+A).</summary>
    Replace,

    /// <summary>The result is only shown (overlay output): read-only targets are fine, and nothing is ever selected by the app.</summary>
    Display,
}

/// <summary>The rules that differ between <see cref="CaptureMode"/>s, kept apart from the Win32 code so they can be tested.</summary>
public static class CapturePolicy
{
    /// <summary>A read-only field cannot take a replacement, but can be read.</summary>
    public static bool RejectsReadOnly(CaptureMode mode) => mode == CaptureMode.Replace;

    /// <summary>
    /// Ctrl+A is only ever sent to replace a whole field. For display it would select the whole web page, or leave a field
    /// fully selected so that the next key press overwrites it.
    /// </summary>
    public static bool AllowsSelectAll(CaptureMode mode, bool isItemView, bool isNonTextControl)
        => mode == CaptureMode.Replace && !isItemView && !isNonTextControl;
}

public enum CaptureFailure
{
    None,
    NoTarget,
    ElevatedTarget,
    PasswordField,
    UnsupportedApp,
    NoText,
    TooLong,
    ReadOnlyField,
    ClipboardBusy,
    InputBlocked,
}

public enum ReplaceFailure
{
    None,
    TargetChanged,
    ClipboardBusy,
    InputBlocked,
    PasteNotAcknowledged,

    /// <summary>The whole field could not be selected (and verified), so nothing was pasted.</summary>
    SelectAllFailed,
}

/// <summary>Outcome of a UIA probe taken at hotkey time.</summary>
public sealed record FocusProbe(UiaFocusInfo? Info);

/// <summary>Text taken from a target app plus everything needed to write a result back to it.</summary>
public sealed record TextCapture(
    string Text,
    TextOrigin Origin,
    ReadStrategy UsedStrategy,
    TargetInfo Target,
    TimeSpan Duration)
{
    /// <summary>Set when the text was read through UIA; lets the replace step re-select and verify the field.</summary>
    internal System.Windows.Automation.AutomationElement? Element { get; init; }

    /// <summary>
    /// The element that had the focus at hotkey time. In browsers, Electron, WPF or WinUI every field lives in the
    /// same window handle, so only UIA can tell whether the focus moved to another field before pasting.
    /// </summary>
    internal System.Windows.Automation.AutomationElement? FocusElement { get; init; }
}

public sealed record CaptureResult(TextCapture? Capture, CaptureFailure Failure, string? Detail = null)
{
    public bool Success => Capture is not null;

    public static CaptureResult Ok(TextCapture capture) => new(capture, CaptureFailure.None);

    public static CaptureResult Fail(CaptureFailure failure, string? detail = null) => new(null, failure, detail);
}

public sealed record ReplaceResult(ReplaceFailure Failure, TimeSpan Duration)
{
    public bool Success => Failure == ReplaceFailure.None;
}

/// <summary>
/// Reads text from and writes text into the focused field of another application. Isolated behind
/// an interface so the rest of the app (and tests) never touch Win32 directly.
/// </summary>
public interface ITextAccess
{
    /// <summary>
    /// Reads what UI Automation knows about the focused element. Must be started at hotkey time, while the
    /// target still has the focus; the result can be handed to <see cref="CaptureAsync"/> later, after our
    /// own UI took the focus. A null <see cref="FocusProbe.Info"/> means "UIA could not tell".
    /// </summary>
    Task<FocusProbe> ProbeAsync(TargetInfo target);

    /// <summary>Cheap checks that make the whole run pointless (admin window, terminal, password field).</summary>
    CaptureResult? PreCheck(TargetInfo? target, FocusProbe? probe, CaptureMode mode = CaptureMode.Replace);

    /// <summary>
    /// Returns the text to work on. Without a probe it probes itself, which only works while the target has the
    /// focus. The clipboard strategy also needs the target to be the foreground window.
    /// With <see cref="CaptureMode.Display"/> the app never selects anything (no Ctrl+A): no selection means no text,
    /// except in a field UIA proves to be editable, whose whole text is read without any key press.
    /// </summary>
    Task<CaptureResult> CaptureAsync(
        TargetInfo? target, ReadStrategy allowed, FocusProbe? probe = null, CaptureMode mode = CaptureMode.Replace, CancellationToken ct = default);

    Task<ReplaceResult> ReplaceAsync(TextCapture capture, string newText, CancellationToken ct = default);
}

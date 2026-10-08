namespace InstaPrompt.Platform.TextIntegration;

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
}

public sealed record CaptureResult(TextCapture? Capture, CaptureFailure Failure, string? Detail = null)
{
    public bool Success => Capture is not null;

    public static CaptureResult Ok(TextCapture capture) => new(capture, CaptureFailure.None);

    public static CaptureResult Fail(CaptureFailure failure, string? detail = null) => new(null, failure, detail);
}

public sealed record ReplaceResult(ReplaceFailure Failure, TimeSpan Duration, string? Detail = null)
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
    CaptureResult? PreCheck(TargetInfo? target, FocusProbe? probe);

    /// <summary>
    /// Returns the text to work on. Without a probe it probes itself, which only works while the target has the
    /// focus. The clipboard strategy also needs the target to be the foreground window.
    /// </summary>
    Task<CaptureResult> CaptureAsync(TargetInfo? target, ReadStrategy allowed, FocusProbe? probe = null, CancellationToken ct = default);

    Task<ReplaceResult> ReplaceAsync(TextCapture capture, string newText, CancellationToken ct = default);
}

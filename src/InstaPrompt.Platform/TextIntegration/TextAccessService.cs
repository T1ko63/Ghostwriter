using System.Diagnostics;
using InstaPrompt.Core.Diagnostics;
using static InstaPrompt.Platform.Native.NativeMethods;

namespace InstaPrompt.Platform.TextIntegration;

/// <summary>
/// Staged strategy:
/// read  = (1) UI Automation selection, if the app exposes it; (2) Ctrl+C with a sentinel on the clipboard;
///         (3) if nothing is selected, Ctrl+A then Ctrl+C (whole field).
/// write = delayed-render clipboard + Ctrl+V, then the user's clipboard is restored. Replacing always goes
///         through paste because UIA cannot replace a selection and ValuePattern would destroy formatting/undo.
/// The user's text is only touched in the final paste; any failure before that leaves it untouched.
/// </summary>
public sealed class TextAccessService : ITextAccess
{
    private static readonly TimeSpan UiaBudget = TimeSpan.FromMilliseconds(150);
    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMilliseconds(450);
    private static readonly TimeSpan CopyFollowUp = TimeSpan.FromMilliseconds(120);
    private static readonly TimeSpan PasteTimeout = TimeSpan.FromMilliseconds(2000);
    private static readonly TimeSpan PasteGrace = TimeSpan.FromMilliseconds(40);
    private static readonly TimeSpan PasteGraceRemote = TimeSpan.FromMilliseconds(900);

    private readonly ClipboardService _clipboard;

    public TextAccessService(ClipboardService clipboard) => _clipboard = clipboard;

    /// <summary>Pre-loads UI Automation in the background so the first hotkey is not slowed down by JIT/COM setup.</summary>
    public static void Warmup() => UiaProbe.Warmup();

    public async Task<FocusProbe> ProbeAsync(TargetInfo target)
    {
        var started = Stopwatch.GetTimestamp();
        var info = await UiaProbe.InspectAsync(target, UiaBudget);
        AppLog.Info(info is null
            ? $"UIA probe [{target.ProcessName}]: no answer ({Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms)"
            : $"UIA probe [{target.ProcessName}]: {info.ControlType}, textPattern={info.HasTextPattern}, selectionKnown={info.SelectionKnown}, "
              + $"selected={info.SelectedText?.Length ?? -1}, whole={info.WholeText?.Length ?? -1}, editable={info.IsEditable}, "
              + $"readOnly={info.IsReadOnly}, tooLong={info.TooLong}, framework={info.FrameworkId}, class='{info.ElementClass}', "
              + $"name='{info.ElementName}' ({Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms)");
        return new FocusProbe(info);
    }

    public CaptureResult? PreCheck(TargetInfo? target, FocusProbe? probe)
    {
        if (target is null) return CaptureResult.Fail(CaptureFailure.NoTarget);
        if (target.IsElevated && !TargetInfo.SelfIsElevated)
        {
            return CaptureResult.Fail(CaptureFailure.ElevatedTarget, target.ProcessName);
        }

        if (target.IsTerminal) return CaptureResult.Fail(CaptureFailure.UnsupportedApp, target.ProcessName);
        if (target.IsWin32PasswordEdit || probe?.Info?.IsPassword == true) return CaptureResult.Fail(CaptureFailure.PasswordField);
        if (target.IsWin32ReadOnlyEdit || probe?.Info?.IsReadOnly == true) return CaptureResult.Fail(CaptureFailure.ReadOnlyField);
        return null;
    }

    public async Task<CaptureResult> CaptureAsync(
        TargetInfo? target, ReadStrategy allowed, FocusProbe? probe = null, CancellationToken ct = default)
    {
        var rejected = PreCheck(target, probe);
        if (rejected is not null) return rejected;
        if (target is null) return CaptureResult.Fail(CaptureFailure.NoTarget); // unreachable: PreCheck rejects null

        var stopwatch = Stopwatch.StartNew();
        UiaFocusInfo? uia = null;
        if (allowed.HasFlag(ReadStrategy.Uia))
        {
            probe ??= await ProbeAsync(target);
            uia = probe.Info;
            if (uia?.IsPassword == true) return CaptureResult.Fail(CaptureFailure.PasswordField);

            if (uia?.IsReadOnly == true) return CaptureResult.Fail(CaptureFailure.ReadOnlyField);
            if (uia?.TooLong == true) return CaptureResult.Fail(CaptureFailure.TooLong);

            // A positive UIA answer is trusted; an empty one is not (many apps report "no selection" wrongly).
            if (!string.IsNullOrEmpty(uia?.SelectedText))
            {
                return CaptureResult.Ok(new TextCapture(
                    uia.SelectedText, TextOrigin.Selection, ReadStrategy.Uia, target, stopwatch.Elapsed));
            }

            // UIA says "caret, no selection" in a field it can prove to be editable: trust that negative and read
            // the field directly. This avoids a Ctrl+C probe that would wait out its full timeout for a copy that
            // never comes. Fields that are not provably editable (documents, web pages) go through the clipboard.
            // Not in web engines: there the focused "Edit" can be a hidden helper textarea (VS Code/VSCodium's
            // Monaco) holding a fragment of the document, so "whole" would silently be only a few characters.
            if (uia is { SelectionKnown: true, SelectedText.Length: 0, IsEditable: true, IsWebEngine: false })
            {
                return string.IsNullOrEmpty(uia.WholeText)
                    ? CaptureResult.Fail(CaptureFailure.NoText)
                    : CaptureResult.Ok(new TextCapture(
                        uia.WholeText, TextOrigin.WholeField, ReadStrategy.Uia, target, stopwatch.Elapsed)
                    {
                        Element = uia.Element,
                    });
            }
        }

        if (!allowed.HasFlag(ReadStrategy.Clipboard)) return CaptureResult.Fail(CaptureFailure.NoText);

        var canSelectAll = !target.IsItemView && uia?.IsNonTextControl != true;

        // Web-engine editor with a known empty selection: UIA cannot be trusted for the content, and a plain Ctrl+C
        // without selection would copy just the current line in VS Code. Select all first, then copy.
        var selectAllFirst = canSelectAll
            && uia is { IsWebEngine: true, IsEditable: true, SelectionKnown: true, SelectedText.Length: 0 };
        return await CaptureViaClipboardAsync(target, canSelectAll, selectAllFirst, stopwatch, ct);
    }

    private async Task<CaptureResult> CaptureViaClipboardAsync(
        TargetInfo target, bool canSelectAll, bool selectAllFirst, Stopwatch stopwatch, CancellationToken ct)
    {
        if (!_clipboard.TrySnapshot(out var snapshot)) return CaptureResult.Fail(CaptureFailure.ClipboardBusy);
        var clipboardStart = Stopwatch.GetTimestamp();
        try
        {
            var origin = TextOrigin.Selection;
            if (selectAllFirst)
            {
                if (!InputSimulator.CtrlChord(InputSimulator.VK_A)) return CaptureResult.Fail(CaptureFailure.InputBlocked);
                origin = TextOrigin.WholeField;
            }

            var outcome = await CopyOnceAsync(ct);
            if (outcome.InputFailed) return CaptureResult.Fail(CaptureFailure.InputBlocked);

            var text = outcome.Text;
            if (text is null && !selectAllFirst)
            {
                if (!canSelectAll) return CaptureResult.Fail(CaptureFailure.NoText);

                if (!InputSimulator.CtrlChord(InputSimulator.VK_A)) return CaptureResult.Fail(CaptureFailure.InputBlocked);
                outcome = await CopyOnceAsync(ct);
                if (outcome.InputFailed) return CaptureResult.Fail(CaptureFailure.InputBlocked);
                text = outcome.Text;
                origin = TextOrigin.WholeField;
            }

            AppLog.Info($"clipboard read [{target.ProcessName}]: {Stopwatch.GetElapsedTime(clipboardStart).TotalMilliseconds:F0} ms, "
                + $"selectAllFirst={selectAllFirst}, origin={origin}, chars={text?.Length ?? -1}");

            if (text is { Length: > UiaProbe.MaxInputChars }) return CaptureResult.Fail(CaptureFailure.TooLong);

            return string.IsNullOrEmpty(text)
                ? CaptureResult.Fail(CaptureFailure.NoText)
                : CaptureResult.Ok(new TextCapture(text, origin, ReadStrategy.Clipboard, target, stopwatch.Elapsed));
        }
        finally
        {
            // Restore always, also on failure, so the user's clipboard never keeps our sentinel.
            if (!_clipboard.TryRestore(snapshot)) AppLog.Warn("Could not restore the clipboard after reading.");
        }
    }

    private readonly record struct CopyOutcome(string? Text, bool InputFailed);

    /// <summary>
    /// Puts a unique sentinel on the clipboard, sends Ctrl+C and waits for the clipboard to change.
    /// Still the sentinel (or no change) means nothing was copied, i.e. nothing is selected.
    /// </summary>
    private async Task<CopyOutcome> CopyOnceAsync(CancellationToken ct)
    {
        var sentinel = "​" + Guid.NewGuid().ToString("N");
        if (!_clipboard.TrySetText(sentinel, hidden: true)) return new CopyOutcome(null, InputFailed: true);

        var sequence = _clipboard.SequenceNumber;
        if (!InputSimulator.CtrlChord(InputSimulator.VK_C)) return new CopyOutcome(null, InputFailed: true);

        var deadline = Environment.TickCount64 + (long)CopyTimeout.TotalMilliseconds;
        while (true)
        {
            var remaining = TimeSpan.FromMilliseconds(Math.Max(0, deadline - Environment.TickCount64));
            if (!await _clipboard.WaitForChangeAsync(sequence, remaining, ct)) return new CopyOutcome(null, false);

            sequence = _clipboard.SequenceNumber;
            var text = _clipboard.TryGetText();
            if (!string.IsNullOrEmpty(text) && text != sentinel) return new CopyOutcome(text, false);

            // Some apps clear first and fill in later (several updates); give them a short follow-up window.
            deadline = Math.Min(deadline, Environment.TickCount64 + (long)CopyFollowUp.TotalMilliseconds);
        }
    }

    private static readonly TimeSpan SelectBudget = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Selects the whole field and proves it where possible: first through UIA (no keystroke), then with Ctrl+A.
    /// Clipboard-read captures have no UIA element; there the Ctrl+A at read time already proved it worked.
    /// </summary>
    private static async Task<ReplaceFailure> SelectWholeFieldAsync(TextCapture capture)
    {
        var element = capture.Element;
        if (element is not null)
        {
            if (await UiaProbe.SelectAllVerifiedAsync(element, capture.Text, SelectBudget)) return ReplaceFailure.None;

            // UIA could not select (or not prove it): fall back to the keyboard and check again.
            if (!InputSimulator.CtrlChord(InputSimulator.VK_A)) return ReplaceFailure.InputBlocked;
            return await UiaProbe.WaitForSelectionAsync(element, capture.Text, SelectBudget)
                ? ReplaceFailure.None
                : ReplaceFailure.SelectAllFailed;
        }

        return InputSimulator.CtrlChord(InputSimulator.VK_A) ? ReplaceFailure.None : ReplaceFailure.InputBlocked;
    }

    public async Task<ReplaceResult> ReplaceAsync(TextCapture capture, string newText, CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var target = capture.Target;

        // The AI call can take seconds. Never paste into a different window than the one the text came from.
        if (GetForegroundWindow() != target.Window)
        {
            return new ReplaceResult(ReplaceFailure.TargetChanged, stopwatch.Elapsed);
        }

        // Fresh snapshot: the user may have copied something while the AI was working.
        if (!_clipboard.TrySnapshot(out var snapshot)) return new ReplaceResult(ReplaceFailure.ClipboardBusy, stopwatch.Elapsed);
        var snapshotMs = stopwatch.ElapsedMilliseconds;
        long pasteSentMs = 0, renderedMs = 0;
        try
        {
            var offer = _clipboard.TryOfferDelayed(newText);
            if (offer is null) return new ReplaceResult(ReplaceFailure.ClipboardBusy, stopwatch.Elapsed);

            // For a whole-field capture, make sure everything is selected before pasting over it. If that
            // cannot be proven, nothing is pasted: otherwise the result would be inserted next to the old text.
            if (capture.Origin == TextOrigin.WholeField)
            {
                var selected = await SelectWholeFieldAsync(capture);
                if (selected == ReplaceFailure.None) { /* proceed */ }
                else return new ReplaceResult(selected, stopwatch.Elapsed);
            }

            if (!InputSimulator.CtrlChord(InputSimulator.VK_V))
            {
                return new ReplaceResult(ReplaceFailure.InputBlocked, stopwatch.Elapsed);
            }

            pasteSentMs = stopwatch.ElapsedMilliseconds;
            var finished = await Task.WhenAny(offer.Rendered, Task.Delay(PasteTimeout, ct));
            renderedMs = stopwatch.ElapsedMilliseconds;
            if (finished != offer.Rendered)
            {
                return new ReplaceResult(ReplaceFailure.PasteNotAcknowledged, stopwatch.Elapsed);
            }

            // The target has asked for the data; give it a moment to finish reading before the clipboard changes again.
            await Task.Delay(target.IsRemoteClient ? PasteGraceRemote : PasteGrace, CancellationToken.None);
            return new ReplaceResult(ReplaceFailure.None, stopwatch.Elapsed);
        }
        finally
        {
            var beforeRestore = stopwatch.ElapsedMilliseconds;
            if (!_clipboard.TryRestore(snapshot)) AppLog.Warn("Could not restore the clipboard after pasting.");
            AppLog.Info($"replace timing: snapshot {snapshotMs} ms ({snapshot.FormatCount} formats, {snapshot.TotalBytes} B), "
                + $"paste sent at {pasteSentMs} ms, render requested at {renderedMs} ms, restore {stopwatch.ElapsedMilliseconds - beforeRestore} ms");
        }
    }
}

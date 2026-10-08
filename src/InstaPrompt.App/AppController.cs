using System.Diagnostics;
using System.Windows.Media;
using InstaPrompt.App.Overlay;
using InstaPrompt.Core.Diagnostics;
using InstaPrompt.Core.Localization;
using InstaPrompt.Core.Prompts;
using InstaPrompt.Core.Providers;
using InstaPrompt.Core.Undo;
using InstaPrompt.Platform.TextIntegration;
using InstaPrompt.Platform.Windowing;

namespace InstaPrompt.App;

/// <summary>
/// Ties the pieces together: hotkey -> (overlay) -> read text -> run prompt -> replace text.
/// All entry points run on the UI thread. Only one run is active at a time.
/// </summary>
public sealed class AppController
{
    private sealed record Session(TargetInfo? Target, Task<FocusProbe> Probe, Anchor Anchor);

    private readonly ITextAccess _text;
    private readonly OverlayWindow _overlay;
    private readonly StatusWindow _status;
    private readonly IPromptRunner _runner;
    private readonly Func<IReadOnlyList<PromptDefinition>> _prompts;

    private Session? _session;
    private CancellationTokenSource? _runCts;
    private bool _busy;

    private readonly Action? _warmUp;

    /// <param name="prompts">Read on every use, so a reloaded prompts.toml is picked up without rebuilding anything.</param>
    public AppController(
        ITextAccess text, OverlayWindow overlay, StatusWindow status, IPromptRunner runner,
        Func<IReadOnlyList<PromptDefinition>> prompts, Action? warmUp = null)
    {
        _text = text;
        _overlay = overlay;
        _status = status;
        _runner = runner;
        _prompts = prompts;
        _warmUp = warmUp;

        _overlay.PromptChosen += prompt => _ = OnPromptChosenAsync(prompt);
        _overlay.Cancelled += () => _ = CloseOverlayAsync(restoreFocus: true);
        _overlay.Dismissed += () => _ = CloseOverlayAsync(restoreFocus: false);
        _status.CancelRequested += () => _runCts?.Cancel();
    }

    public OverlayPosition Position { get; set; } = OverlayPosition.Caret;

    /// <summary>Environment.TickCount64 of the last hotkey or run; used to find out when the app has been idle for a while.</summary>
    public long LastActivityTick { get; private set; } = Environment.TickCount64;

    public bool IsBusy => _busy || _overlay.IsShown;

    private void MarkActivity() => LastActivityTick = Environment.TickCount64;

    // ---- entry points ----

    /// <summary>
    /// Shows a short message (config errors, "configuration loaded") in the status pill near the mouse. Windows
    /// notifications can be silenced by Focus Assist; this one always shows.
    /// </summary>
    public void ShowNotice(string message)
    {
        if (_busy)
        {
            AppLog.Info("Notice skipped: a run is active.");
            return; // never cover the progress of a running prompt
        }

        AppLog.Info($"Notice: {message}");
        _status.ShowError(message, Anchor.Resolve(null, OverlayPosition.Mouse));
    }

    /// <summary>Global overlay hotkey.</summary>
    public void OnOverlayHotkey(long hotkeyTimestamp)
    {
        MarkActivity();
        AppLog.Info("hotkey: overlay");
        if (_overlay.IsShown)
        {
            _ = CloseOverlayAsync(restoreFocus: true);
            return;
        }

        if (_busy)
        {
            AppLog.Info("Overlay hotkey ignored: a run is active.");
            return;
        }

        var target = TargetInfo.Capture();
        var anchor = Anchor.Resolve(target, Position);
        if (Reject(_text.PreCheck(target, null), target, anchor)) return;

        _busy = true;

        // Start reading the focused element now; the overlay only takes the focus once this is done.
        var probe = _text.ProbeAsync(target!);
        _ = ShowOverlayAsync(new Session(target, probe, anchor), hotkeyTimestamp);
    }

    /// <summary>How long the overlay may wait for UI Automation to say where the text cursor is (browsers, Electron).</summary>
    private static readonly TimeSpan CaretWaitBudget = TimeSpan.FromMilliseconds(30);

    private async Task ShowOverlayAsync(Session session, long hotkeyTimestamp)
    {
        // Classic Win32 carets are known instantly. Otherwise UIA usually answers within a few milliseconds, which is
        // worth waiting for so the overlay appears at the text cursor instead of at the mouse; if it is slower, the
        // overlay does not wait any longer.
        var anchorKind = Position != OverlayPosition.Caret ? Position.ToString().ToLowerInvariant()
            : session.Target?.CaretScreenPos is not null ? "win32 caret" : "mouse";
        if (Position == OverlayPosition.Caret && session.Target?.CaretScreenPos is null)
        {
            if (await Task.WhenAny(session.Probe, Task.Delay(CaretWaitBudget)) == session.Probe)
            {
                var refined = Refine(session.Anchor, session.Target, await session.Probe);
                if (!refined.Equals(session.Anchor)) anchorKind = "uia caret";
                session = session with { Anchor = refined };
            }
        }

        _session = session;
        _overlay.Present(_prompts());
        var (x, y) = session.Anchor.PlaceWindow(_overlay.MeasureDesired());
        _overlay.ShowAt(x, y);
        LogFrameLatency(hotkeyTimestamp, session.Target!.ProcessName, anchorKind);

        // The time the user needs to pick a prompt is used to have the connection to the AI provider ready.
        _warmUp?.Invoke();

        _ = ActivateAfterProbeAsync(session);
    }

    /// <summary>Moves a mouse-based anchor to the text cursor if UI Automation found it.</summary>
    private Anchor Refine(Anchor anchor, TargetInfo? target, FocusProbe probe)
        => Position == OverlayPosition.Caret && target?.CaretScreenPos is null && probe.Info?.CaretPoint is { } caret
            ? Anchor.AtCaret(caret)
            : anchor;

    /// <summary>Per-prompt hotkey: runs the prompt directly, without the overlay.</summary>
    public void OnPromptHotkey(long hotkeyTimestamp, PromptDefinition prompt)
    {
        MarkActivity();
        AppLog.Info($"hotkey: prompt '{prompt.Name}'");
        if (_busy || _overlay.IsShown)
        {
            AppLog.Info($"Prompt hotkey '{prompt.Name}' ignored: busy.");
            return;
        }

        var target = TargetInfo.Capture();
        var anchor = Anchor.Resolve(target, Position);
        if (Reject(_text.PreCheck(target, null), target, anchor)) return;

        _busy = true;
        var session = new Session(target, _text.ProbeAsync(target!), anchor);
        _ = ExecuteAsync(session, prompt, restoreFocus: false, hotkeyTimestamp);
    }

    // ---- overlay flow ----

    private async Task ActivateAfterProbeAsync(Session session)
    {
        var probe = await session.Probe;
        if (_session != session || !_overlay.IsShown) return;

        // A password field can only be recognised by UIA, i.e. after the overlay is already up.
        if (Reject(_text.PreCheck(session.Target, probe), session.Target, session.Anchor))
        {
            _overlay.HideOverlay();
            _session = null;
            _busy = false;
            return;
        }

        _overlay.ActivateForInput();
    }

    private async Task OnPromptChosenAsync(PromptDefinition prompt)
    {
        var session = _session;
        if (session is null) return;
        _session = null;
        _overlay.HideOverlay();
        await ExecuteAsync(session, prompt, restoreFocus: true, hotkeyTimestamp: null);
    }

    private async Task CloseOverlayAsync(bool restoreFocus)
    {
        var session = _session;
        _session = null;
        _overlay.HideOverlay();
        try
        {
            if (restoreFocus && session?.Target is { } target) await WindowHelper.RestoreFocusAsync(target);
        }
        finally
        {
            _busy = false;
        }
    }

    // ---- run ----

    private async Task ExecuteAsync(Session session, PromptDefinition prompt, bool restoreFocus, long? hotkeyTimestamp)
    {
        var target = session.Target!;
        using var cts = new CancellationTokenSource();
        _runCts = cts;
        var started = Stopwatch.GetTimestamp();
        try
        {
            if (restoreFocus && !await WindowHelper.RestoreFocusAsync(target))
            {
                _status.ShowError(Loc.Get("err_focus"), session.Anchor);
                return;
            }

            // The probe is normally long finished; waiting for it first lets the progress pill sit at the text cursor.
            var probe = await session.Probe;
            session = session with { Anchor = Refine(session.Anchor, target, probe) };
            _status.ShowProgress(Loc.Get("working", prompt.Name), session.Anchor);

            var captured = await Task.Run(() => _text.CaptureAsync(target, ReadStrategy.Auto, probe, cts.Token));
            if (!captured.Success)
            {
                Fail(Describe(captured), session, $"capture failed: {captured.Failure}");
                return;
            }

            var capture = captured.Capture!;

            // The runner resolves universal prompts itself (marker or not); the whole captured text is replaced either way.
            var timings = new LlmTimings();
            var output = await _runner.RunAsync(prompt, capture.Text, cts.Token, timings);

            var replaced = await Task.Run(() => _text.ReplaceAsync(capture, output, cts.Token));
            if (!replaced.Success)
            {
                Fail(Describe(replaced), session, $"replace failed: {replaced.Failure}");
                return;
            }

            // Remembered for the undo hotkey: what was there (marker block included) and what was put there. Memory only.
            History.Add(new ReplacementRecord(capture.Text, output, FieldOf(target)));
            _status.HideStatus();
            var origin = hotkeyTimestamp ?? started;
            var since = hotkeyTimestamp.HasValue ? "hotkey" : "choice";
            AppLog.Info($"[{target.ProcessName}] '{prompt.Name}' ({prompt.Mode}) ok via {capture.UsedStrategy}/{capture.Origin}, "
                + $"{capture.Text.Length} chars -> {output.Length} chars, read {capture.Duration.TotalMilliseconds:F0} ms, "
                + $"replace {replaced.Duration.TotalMilliseconds:F0} ms, total since {since} {Stopwatch.GetElapsedTime(origin).TotalMilliseconds:F0} ms");
            if (timings.Sent != 0)
            {
                // "request started" is the number the user feels: from hotkey/choice to the moment the request left the app.
                AppLog.Info($"  timing since {since}: request sent {LlmTimings.Ms(origin, timings.Sent):F0} ms, "
                    + $"response headers +{LlmTimings.Ms(timings.Sent, timings.Headers):F0} ms, first text +{LlmTimings.Ms(timings.Headers, timings.FirstToken):F0} ms, "
                    + $"answer complete +{LlmTimings.Ms(timings.FirstToken, timings.Done):F0} ms (attempts: {timings.Attempts})");
            }
        }
        catch (OperationCanceledException)
        {
            _status.HideStatus();
            AppLog.Info("Run cancelled by the user.");
        }
        catch (MarkerException ex)
        {
            // An unclosed or empty marker: nothing was sent, nothing was changed.
            AppLog.Info($"[{target.ProcessName}] '{prompt.Name}': marker {ex.Status}");
            _status.ShowError(ex.Status == MarkerStatus.Empty
                ? Loc.Get("err_marker_empty")
                : Loc.Get("err_marker_unclosed", ex.Start, ex.End), session.Anchor);
        }
        catch (LlmException ex)
        {
            // Nothing was pasted: the original text is untouched.
            AppLog.Warn($"AI call failed: {ex.Provider}: {ex.Kind} (HTTP {ex.StatusCode?.ToString() ?? "-"}) {ex.Detail}");
            _status.ShowError(Describe(ex), session.Anchor);
        }
        catch (Exception ex)
        {
            AppLog.Error("Run failed.", ex);
            _status.ShowError(Loc.Get("err_unexpected", ex.GetType().Name), session.Anchor);
        }
        finally
        {
            _runCts = null;
            _busy = false;
        }
    }

    // ---- undo ----

    /// <summary>The last replacements, in memory only. Capacity follows undo_history.</summary>
    public ReplacementHistory History { get; } = new();

    private static FieldId FieldOf(TargetInfo target) => new(target.Window, target.ProcessId, target.FocusWindow);

    /// <summary>Global undo hotkey: puts the original text back, but only where the stored result is still found.</summary>
    public void OnUndoHotkey(long hotkeyTimestamp)
    {
        MarkActivity();
        AppLog.Info("hotkey: undo");
        if (_busy || _overlay.IsShown)
        {
            AppLog.Info("Undo hotkey ignored: busy.");
            return;
        }

        var target = TargetInfo.Capture();
        var anchor = Anchor.Resolve(target, Position);
        if (Reject(_text.PreCheck(target, null), target, anchor)) return;
        if (History.Count == 0)
        {
            _status.ShowInfo(Loc.Get("undo_nothing"), anchor);
            return;
        }

        _busy = true;
        _ = UndoAsync(new Session(target, _text.ProbeAsync(target!), anchor));
    }

    private async Task UndoAsync(Session session)
    {
        var target = session.Target!;
        using var cts = new CancellationTokenSource();
        _runCts = cts;
        try
        {
            var probe = await session.Probe;
            session = session with { Anchor = Refine(session.Anchor, target, probe) };

            // Read what is in the field right now (selection, otherwise the whole field), exactly as for a normal run.
            var captured = await Task.Run(() => _text.CaptureAsync(target, ReadStrategy.Auto, probe, cts.Token));
            if (!captured.Success)
            {
                // An empty field cannot contain the stored result.
                Fail(captured.Failure == CaptureFailure.NoText ? Loc.Get("undo_changed") : Describe(captured), session,
                    $"undo: capture failed: {captured.Failure}");
                return;
            }

            var capture = captured.Capture!;
            var plan = History.Plan(FieldOf(target), capture.Text);
            if (plan.Outcome != UndoOutcome.Restore)
            {
                var message = plan.Outcome switch
                {
                    UndoOutcome.NothingToUndo => Loc.Get("undo_nothing"),
                    UndoOutcome.OtherField => Loc.Get("undo_other_field"),
                    UndoOutcome.Ambiguous => Loc.Get("undo_ambiguous"),
                    _ => Loc.Get("undo_changed"),
                };
                AppLog.Info($"[{target.ProcessName}] undo refused: {plan.Outcome}");
                _status.ShowInfo(message, session.Anchor);
                return;
            }

            var replaced = await Task.Run(() => _text.ReplaceAsync(capture, plan.NewText!, cts.Token));
            if (!replaced.Success)
            {
                Fail(Describe(replaced), session, $"undo: replace failed: {replaced.Failure}");
                return;
            }

            History.Commit(plan.Record!);
            _status.ShowInfo(Loc.Get("undo_done"), session.Anchor);
            AppLog.Info($"[{target.ProcessName}] undo ok via {capture.UsedStrategy}/{capture.Origin}, {capture.Text.Length} -> {plan.NewText!.Length} chars, "
                + $"{History.Count} left in history");
        }
        catch (OperationCanceledException)
        {
            _status.HideStatus();
        }
        catch (Exception ex)
        {
            AppLog.Error("Undo failed.", ex);
            _status.ShowError(Loc.Get("err_unexpected", ex.GetType().Name), session.Anchor);
        }
        finally
        {
            _runCts = null;
            _busy = false;
        }
    }

    // ---- helpers ----

    private bool Reject(CaptureResult? failure, TargetInfo? target, Anchor anchor)
    {
        if (failure is null) return false;
        AppLog.Info($"[{target?.ProcessName ?? "?"}] rejected: {failure.Failure}");
        _status.ShowError(Describe(failure), anchor);
        return true;
    }

    private void Fail(string message, Session session, string logLine)
    {
        AppLog.Info($"[{session.Target?.ProcessName}] {logLine}");
        _status.ShowError(message, session.Anchor);
    }

    private static string Describe(CaptureResult result) => result.Failure switch
    {
        CaptureFailure.ElevatedTarget => Loc.Get("err_elevated", result.Detail ?? "?"),
        CaptureFailure.PasswordField => Loc.Get("err_password"),
        CaptureFailure.UnsupportedApp => Loc.Get("err_terminal"),
        CaptureFailure.NoText => Loc.Get("err_no_text"),
        CaptureFailure.TooLong => Loc.Get("err_too_long"),
        CaptureFailure.ReadOnlyField => Loc.Get("err_read_only"),
        CaptureFailure.ClipboardBusy => Loc.Get("err_clipboard_busy"),
        CaptureFailure.InputBlocked => Loc.Get("err_input_blocked"),
        _ => Loc.Get("err_no_window"),
    };

    private static string Describe(LlmException ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.Detail) ? string.Empty : $" ({ex.Detail})";
        return ex.Kind switch
        {
            LlmErrorKind.Auth => Loc.Get("llm_auth", ex.Provider, detail),
            LlmErrorKind.RateLimit => Loc.Get("llm_rate_limit", ex.Provider, detail),
            LlmErrorKind.ModelNotFound => Loc.Get("llm_model", ex.Provider, detail),
            LlmErrorKind.BadRequest => Loc.Get("llm_bad_request", ex.Provider, detail),
            LlmErrorKind.Server => Loc.Get("llm_server", ex.Provider, detail),
            LlmErrorKind.Network => Loc.Get("llm_network", ex.Provider),
            LlmErrorKind.Timeout => Loc.Get("llm_timeout", ex.Provider),
            LlmErrorKind.Blocked => Loc.Get("llm_blocked", ex.Provider),
            LlmErrorKind.Truncated => Loc.Get("llm_truncated", ex.Provider),
            LlmErrorKind.EmptyResponse => Loc.Get("llm_empty", ex.Provider),
            LlmErrorKind.Config => Loc.Get("llm_config", detail.Trim(' ', '(', ')')),
            _ => Loc.Get("llm_protocol", ex.Provider, detail),
        };
    }

    private static string Describe(ReplaceResult result) => result.Failure switch
    {
        ReplaceFailure.TargetChanged => Loc.Get("err_target_changed"),
        ReplaceFailure.ClipboardBusy => Loc.Get("err_clipboard_busy"),
        ReplaceFailure.InputBlocked => Loc.Get("err_input_blocked"),
        ReplaceFailure.PasteNotAcknowledged => Loc.Get("err_paste_ack"),
        ReplaceFailure.SelectAllFailed => Loc.Get("err_select_all"),
        _ => Loc.Get("err_unexpected", result.Failure),
    };

    /// <summary>Logs hotkey -> first rendered frame of the overlay, which is what the user perceives as "appears".</summary>
    private static void LogFrameLatency(long hotkeyTimestamp, string app, string anchorKind)
    {
        var shownCall = Stopwatch.GetElapsedTime(hotkeyTimestamp).TotalMilliseconds;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            var frame = Stopwatch.GetElapsedTime(hotkeyTimestamp).TotalMilliseconds;
            AppLog.Info($"[{app}] overlay at {anchorKind}: Show() returned after {shownCall:F1} ms, first frame after {frame:F1} ms");
        };
        CompositionTarget.Rendering += handler;
    }
}

using System.Diagnostics;
using System.Text;
using Kuroko.App.Overlay;
using Kuroko.Core.Config;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Hotkeys;
using Kuroko.Core.Localization;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;
using Kuroko.Core.Undo;
using Kuroko.Platform.Hotkeys;
using Kuroko.Platform.TextIntegration;
using static Kuroko.App.FailureMessages;

namespace Kuroko.App;

/// <summary>
/// Ties the pieces together: hotkey -> (overlay) -> read text -> run prompt -> replace text.
/// All entry points run on the UI thread. Only one run is active at a time.
/// </summary>
public sealed class AppController
{
    private sealed record Session(TargetInfo? Target, Task<FocusProbe> Probe, Anchor Anchor);

    private readonly ITextAccess _text;
    private readonly IOverlayView _overlay;
    private readonly IStatusView _status;
    private readonly IPromptRunner _runner;
    private readonly Func<IReadOnlyList<PromptDefinition>> _prompts;

    private Session? _session;
    private readonly Action? _warmUp;
    private readonly IDesktop _desktop;
    private readonly LastResult _lastResult;
    private readonly RunState _run;
    private readonly ResultCard _card;
    private readonly LatencyLog _latency;

    /// <param name="prompts">Read on every use, so a reloaded prompts.toml is picked up without rebuilding anything.</param>
    /// <param name="hotkeys">Needed for the temporary hotkeys: Esc during a run, Esc and copy while the result card is up.</param>
    /// <param name="clipboard">Needed to copy the text of the result card and the last result.</param>
    /// <param name="desktop">Target window, focus, timer resolution and foreground watch; <see cref="Win32Desktop"/> when not given.</param>
    public AppController(
        ITextAccess text, IOverlayView overlay, IStatusView status, IPromptRunner runner,
        Func<IReadOnlyList<PromptDefinition>> prompts, Action? warmUp = null,
        IHotkeyRegistry? hotkeys = null, IClipboard? clipboard = null, IDesktop? desktop = null)
    {
        _text = text;
        _overlay = overlay;
        _status = status;
        _runner = runner;
        _prompts = prompts;
        _warmUp = warmUp;
        _desktop = desktop ?? new Win32Desktop();
        _run = new RunState(_desktop, hotkeys);
        _latency = new LatencyLog(_desktop);
        _card = new ResultCard(status, _desktop, hotkeys, clipboard, _run, () => ResultCopyHotkey);
        _lastResult = new LastResult(status, hotkeys, clipboard, () => ResultCopyHotkey);

        _overlay.PromptChosen += prompt => _ = OnPromptChosenAsync(prompt);
        _overlay.Cancelled += () => _ = CloseOverlayAsync(restoreFocus: true);
        _overlay.Dismissed += () => _ = CloseOverlayAsync(restoreFocus: false);
        _status.CancelRequested += OnStatusCancelRequested;
    }

    private readonly AnchorPolicy _anchors = new();

    public OverlayPosition Position
    {
        get => _anchors.Position;
        set => _anchors.Position = value;
    }

    /// <summary>overlay_fixed_position and overlay_screen_margin: used when <see cref="Position"/> is <see cref="OverlayPosition.Fixed"/>, and for the margin in every mode.</summary>
    public CardSpot OverlaySpot
    {
        get => _anchors.OverlaySpot;
        set => _anchors.OverlaySpot = value;
    }

    public double OverlayScreenMargin
    {
        get => _anchors.OverlayScreenMargin;
        set => _anchors.OverlayScreenMargin = value;
    }

    /// <summary>Size and font of the prompt picker (overlay_width, overlay_min_height, overlay_max_height, overlay_font_size); applied at once.</summary>
    public void SetOverlayLayout(double width, double minHeight, double maxHeight, double fontSize)
        => _overlay.SetLayout(width, minHeight, maxHeight, fontSize);

    /// <summary>
    /// Hotkey that copies the result card, or the answer offered by the error pill after a failed paste (result_copy_hotkey);
    /// read when the card or pill opens and after a configuration reload.
    /// </summary>
    public string ResultCopyHotkey { get; set; } = AppSettings.DefaultResultCopyHotkey;

    /// <summary>result_position: where the card opens.</summary>
    public ResultPlacement ResultPlacement
    {
        get => _anchors.ResultPlacement;
        set => _anchors.ResultPlacement = value;
    }

    /// <summary>result_fixed_position.</summary>
    public CardSpot ResultSpot
    {
        get => _anchors.ResultSpot;
        set => _anchors.ResultSpot = value;
    }

    /// <summary>result_screen_margin: distance of the card to the screen edge, in device-independent pixels.</summary>
    public double ResultScreenMargin
    {
        get => _anchors.ResultScreenMargin;
        set => _anchors.ResultScreenMargin = value;
    }

    /// <summary>result_font_size: applied at once, also to a card that is on screen.</summary>
    public double ResultFontSize
    {
        get => _status.ResultFontSize;
        set => _status.ResultFontSize = value;
    }

    /// <summary>result_width, result_min_height and result_max_height: size of the card; used for the next card that opens.</summary>
    public void SetResultSize(double width, double minHeight, double maxHeight) => _status.SetResultSize(width, minHeight, maxHeight);

    /// <summary>Environment.TickCount64 of the last hotkey or run; used to find out when the app has been idle for a while.</summary>
    public long LastActivityTick { get; private set; } = Environment.TickCount64;

    public bool IsBusy => _run.Busy || _overlay.IsShown || _card.IsLive;

    /// <summary>Raised on every hotkey, after <see cref="LastActivityTick"/> was updated.</summary>
    public event Action? Activity;

    private void MarkActivity()
    {
        LastActivityTick = Environment.TickCount64;
        Activity?.Invoke();
    }

    // ---- entry points ----

    /// <summary>
    /// Shows a short message (config errors, "configuration loaded") in the status pill near the mouse. Windows
    /// notifications can be silenced by Focus Assist; this one always shows.
    /// </summary>
    public void ShowNotice(string message, NoticeKind kind = NoticeKind.Error)
    {
        if (_run.Busy || _card.IsLive)
        {
            AppLog.Info("Notice skipped: a run is active or a result is on screen.");
            return; // never cover the progress of a running prompt, or a result the user is reading
        }

        AppLog.Info($"Notice: {message}");
        var anchor = Anchor.Resolve(null, OverlayPosition.Mouse);
        switch (kind)
        {
            case NoticeKind.Progress:
                _status.ShowProgress(message, anchor);
                break;
            case NoticeKind.Info:
                _status.ShowInfo(message, anchor, longer: true);
                break;
            default:
                _status.ShowError(message, anchor);
                break;
        }
    }

    /// <summary>Global overlay hotkey.</summary>
    public void OnOverlayHotkey(long hotkeyTimestamp)
    {
        MarkActivity();
        AppLog.Info("hotkey: overlay");
        _card.Close(); // a visible result card never blocks the next call
        if (_overlay.IsShown)
        {
            _ = CloseOverlayAsync(restoreFocus: true);
            return;
        }

        if (_run.Busy)
        {
            AppLog.Info("Overlay hotkey ignored: a run is active.");
            return;
        }

        // Read-only targets are fine here: whether that matters is decided once a prompt is chosen (overlay output works, replace does not).
        var target = _desktop.CaptureTarget();
        var anchor = _anchors.ForTarget(target);
        if (Reject(_text.PreCheck(target, null, CaptureMode.Display), target, anchor)) return;

        _run.Busy = true;

        // Start reading the focused element now; the overlay only takes the focus once this is done.
        var probe = _text.ProbeAsync(target!);
        _ = ShowOverlayAsync(new Session(target, probe, anchor), hotkeyTimestamp);
    }

    /// <summary>How long the overlay may wait for UI Automation to say where the text cursor is (browsers, Electron).</summary>
    private static readonly TimeSpan CaretWaitBudget = TimeSpan.FromMilliseconds(30);

    private async Task ShowOverlayAsync(Session session, long hotkeyTimestamp)
    {
        try
        {
            await ShowOverlayCoreAsync(session, hotkeyTimestamp);
        }
        catch (Exception ex)
        {
            // No prompt can have been chosen yet: the session is published at the end, synchronously with ShowAt.
            AbortOverlay(session, ex, ownsBusy: _session is null || _session == session);
        }
    }

    /// <summary>
    /// A failure while the overlay opens must not leave the controller busy: then every hotkey would be ignored
    /// until a restart. Nothing has been read or changed at this point.
    /// </summary>
    private void AbortOverlay(Session session, Exception ex, bool ownsBusy)
    {
        AppLog.Error("Opening the overlay failed.", ex);
        if (!ownsBusy) return;

        _session = null;
        _overlay.HideOverlay();
        _run.Busy = false;
        _status.ShowError(Loc.Get("err_unexpected", ex.GetType().Name), session.Anchor);
    }

    private async Task ShowOverlayCoreAsync(Session session, long hotkeyTimestamp)
    {
        // Classic Win32 carets are known instantly. Otherwise UIA usually answers within a few milliseconds, which is
        // worth waiting for so the overlay appears at the text cursor instead of at the mouse; if it is slower, the
        // overlay does not wait any longer.
        var anchorKind = _anchors.Position != OverlayPosition.Caret ? _anchors.Position.ToString().ToLowerInvariant()
            : session.Target?.CaretScreenPos is not null ? "win32 caret" : "mouse";
        if (_anchors.Position == OverlayPosition.Caret && session.Target?.CaretScreenPos is null)
        {
            if (await Task.WhenAny(session.Probe, Task.Delay(CaretWaitBudget)) == session.Probe)
            {
                var refined = _anchors.Refine(session.Anchor, session.Target, await session.Probe);
                if (!refined.Equals(session.Anchor)) anchorKind = "uia caret";
                session = session with { Anchor = refined };
            }
        }

        _session = session;
        _overlay.Present(_prompts());
        _overlay.ShowAnchored(session.Anchor);
        _latency.OverlayShown(hotkeyTimestamp, session.Target!.ProcessName, anchorKind);

        // The time the user needs to pick a prompt is used to have the connection to the AI provider ready.
        _warmUp?.Invoke();

        _ = ActivateAfterProbeAsync(session);
    }

    /// <summary>Per-prompt hotkey: runs the prompt directly, without the overlay.</summary>
    public void OnPromptHotkey(long hotkeyTimestamp, PromptDefinition prompt)
    {
        MarkActivity();
        AppLog.Info($"hotkey: prompt '{prompt.Name}'");
        _card.Close(); // a visible result card never blocks the next call; its temporary hotkeys are released before anything is read
        if (_run.Busy || _overlay.IsShown)
        {
            AppLog.Info($"Prompt hotkey '{prompt.Name}' ignored: busy.");
            return;
        }

        var target = _desktop.CaptureTarget();
        var anchor = _anchors.ForTarget(target);
        if (Reject(_text.PreCheck(target, null, ModeOf(prompt)), target, anchor)) return;

        _run.Busy = true;
        var session = new Session(target, _text.ProbeAsync(target!), anchor);
        _ = ExecuteAsync(session, prompt, restoreFocus: false, hotkeyTimestamp);
    }

    // ---- overlay flow ----

    private async Task ActivateAfterProbeAsync(Session session)
    {
        try
        {
            await ActivateAfterProbeCoreAsync(session);
        }
        catch (Exception ex)
        {
            // Once a prompt was chosen the run owns the busy flag and reports its own errors.
            AbortOverlay(session, ex, ownsBusy: _session == session);
        }
    }

    private async Task ActivateAfterProbeCoreAsync(Session session)
    {
        var probe = await session.Probe;
        if (_session != session || !_overlay.IsShown) return;

        // A password field can only be recognised by UIA, i.e. after the overlay is already up. A read-only field is not
        // rejected here (overlay output works there); a replace prompt chosen for it is refused by the capture later.
        if (Reject(_text.PreCheck(session.Target, probe, CaptureMode.Display), session.Target, session.Anchor))
        {
            _overlay.HideOverlay();
            _session = null;
            _run.Busy = false;
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
            if (restoreFocus && session?.Target is { } target) await _desktop.RestoreFocusAsync(target);
        }
        finally
        {
            _run.Busy = false;
        }
    }

    // ---- run ----

    private async Task ExecuteAsync(Session session, PromptDefinition prompt, bool restoreFocus, long? hotkeyTimestamp)
    {
        if (prompt.Output == PromptOutput.Overlay)
        {
            await ExecuteOverlayAsync(session, prompt, restoreFocus, hotkeyTimestamp);
            return;
        }

        var target = session.Target!;
        using var cts = new CancellationTokenSource();
        _run.Start(cts);
        var started = Stopwatch.GetTimestamp();
        string? output = null; // set once the answer is complete; from then on a failure must not lose it
        _run.HoldEsc();
        try
        {
            if (restoreFocus && !await _desktop.RestoreFocusAsync(target))
            {
                _status.ShowError(Loc.Get("err_focus"), session.Anchor);
                return;
            }

            // The probe is normally long finished; waiting for it first lets the progress pill sit at the text cursor.
            var probe = await session.Probe;
            session = session with { Anchor = _anchors.Refine(session.Anchor, target, probe) };
            _status.ShowProgress(Loc.Get("working", prompt.Name), session.Anchor);

            var captured = await Task.Run(() => _text.CaptureAsync(target, ReadStrategy.Auto, probe, ct: cts.Token));
            if (!captured.Success)
            {
                Fail(Describe(captured), session, $"capture failed: {captured.Failure}");
                return;
            }

            var capture = captured.Capture!;

            // The runner resolves universal prompts itself (marker or not); the whole captured text is replaced either way.
            var timings = new LlmTimings();
            ProviderFallback? fallback = null;
            output = await _runner.RunAsync(prompt, capture.Text, cts.Token, timings, used =>
            {
                fallback = used;
                OnUi(() => _status.UpdateProgress(Loc.Get("fallback_working", used.From, used.To), session.Anchor));
            });
            _lastResult.Remember(output);

            var replaced = await Task.Run(() => _text.ReplaceAsync(capture, output, cts.Token));
            if (!replaced.Success)
            {
                AppLog.Info($"[{target.ProcessName}] replace failed: {replaced.Failure}; the answer ({output.Length} chars) is kept for copying");
                _lastResult.ShowRescue(Describe(replaced), session.Anchor);
                return;
            }

            // Remembered for the undo hotkey: what was there (marker block included) and what was put there. Memory only.
            History.Add(new ReplacementRecord(capture.Text, output, FieldOf(target)));
            // The text went to another provider than the prompt names: say so instead of ending silently.
            if (fallback is not null) _status.ShowInfo(Loc.Get("fallback_done", fallback.From, fallback.To), session.Anchor, longer: true);
            else _status.HideStatus();
            var origin = hotkeyTimestamp ?? started;
            var since = hotkeyTimestamp.HasValue ? "hotkey" : "choice";
            AppLog.Info($"[{target.ProcessName}] '{prompt.Name}' ({prompt.Mode}) ok via {capture.UsedStrategy}/{capture.Origin}, "
                + $"{capture.Text.Length} chars -> {output.Length} chars, read {capture.Duration.TotalMilliseconds:F0} ms, "
                + $"replace {replaced.Duration.TotalMilliseconds:F0} ms, total since {since} {Stopwatch.GetElapsedTime(origin).TotalMilliseconds:F0} ms");
            LatencyLog.Request(timings, origin, since);
        }
        catch (OperationCanceledException)
        {
            // Cancelled on purpose (Esc or a click on the pill): no error pill, but a complete answer stays in the tray menu.
            _status.HideStatus();
            AppLog.Info(output is null ? "Run cancelled by the user." : "Run cancelled by the user before the paste; the answer is kept for copying.");
        }
        catch (Exception ex) when (output is null || ex is MarkerException or LlmException)
        {
            FailureMessages.Report(_status, ex, prompt, target, session.Anchor);
        }
        catch (Exception ex)
        {
            // The answer is complete: the failure must not lose it.
            AppLog.Error("Run failed.", ex);
            _lastResult.ShowRescue(FailureMessages.Unexpected(ex), session.Anchor);
        }
        finally
        {
            _run.ReleaseEsc();
            _run.Finish();
        }
    }

    /// <summary>Runs on the UI thread: directly if already there, otherwise queued (the runner's callbacks do not promise a thread).</summary>
    private void OnUi(Action action) => _status.RunOnUi(action);

    // ---- last result ----

    /// <summary>True once an answer has arrived; the tray entry "Copy last result" is greyed out until then.</summary>
    public bool HasLastResult => _lastResult.HasValue;

    /// <summary>Tray entry "Copy last result". The pill is skipped while a run or a card is on screen, so neither is covered.</summary>
    public void CopyLastResult()
    {
        AppLog.Info("tray: copy last result");
        _lastResult.Copy(Anchor.Resolve(null, OverlayPosition.Mouse), showPill: !_run.Busy && !_card.IsLive);
    }

    // ---- result card (overlay output) ----

    private static CaptureMode ModeOf(PromptDefinition prompt)
        => prompt.Output == PromptOutput.Overlay ? CaptureMode.Display : CaptureMode.Replace;

    /// <summary>
    /// A run whose result is shown in the card instead of replacing the text. Nothing in the target is changed: no
    /// paste, no history for undo, no "target changed" check. The text is read without ever selecting anything (no Ctrl+A).
    /// </summary>
    private async Task ExecuteOverlayAsync(Session session, PromptDefinition prompt, bool restoreFocus, long? hotkeyTimestamp)
    {
        var target = session.Target!;
        using var cts = new CancellationTokenSource();
        _run.Start(cts);
        var started = Stopwatch.GetTimestamp();
        var origin = hotkeyTimestamp ?? started;
        var since = hotkeyTimestamp.HasValue ? "hotkey" : "choice";
        _run.HoldEsc(); // until the card takes Esc over
        try
        {
            if (restoreFocus && !await _desktop.RestoreFocusAsync(target))
            {
                _status.ShowError(Loc.Get("err_focus"), session.Anchor);
                return;
            }

            var probe = await session.Probe;
            // By default the card has a fixed place on the screen: at the end of the selection it would often be pushed against
            // the screen edge (the selection can reach beyond the visible page) and cover what is being read.
            session = session with
            {
                Anchor = _anchors.ForResult(target, probe, session.Anchor),
            };
            _status.BeginResult(Loc.Get("working", prompt.Name), session.Anchor);

            var captured = await Task.Run(() => _text.CaptureAsync(target, ReadStrategy.Auto, probe, CaptureMode.Display, cts.Token));
            if (!captured.Success)
            {
                Fail(captured.Failure == CaptureFailure.NoText ? Loc.Get("err_no_selection") : Describe(captured), session,
                    $"capture failed: {captured.Failure} (overlay output)");
                return;
            }

            var capture = captured.Capture!;

            // The text is read: from here on the card owns Esc and the copy hotkey. Registering only now keeps them out of
            // the way of the keys the app sends itself while reading. Esc passes from the run to the card.
            _run.ReleaseEsc();
            _card.Open(session.Anchor, target);
            if (!_run.IsCurrent(cts)) return; // the foreground window had already changed

            var timings = new LlmTimings();
            var pieces = 0;
            void OnFallback(ProviderFallback used) => OnUi(() =>
            {
                if (_run.IsCurrent(cts)) _status.UpdateProgress(Loc.Get("fallback_working", used.From, used.To), session.Anchor);
            });

            await foreach (var piece in _runner.StreamAsync(prompt, capture.Text, cts.Token, timings, OnFallback))
            {
                if (!_run.IsCurrent(cts)) return; // the card was closed or replaced: this run is obsolete

                _card.Append(piece);
                if (pieces++ == 0) _latency.FirstText(target.ProcessName, prompt.Name, origin, since);
            }

            if (!_run.IsCurrent(cts)) return;
            var answer = _card.Complete();
            _lastResult.Remember(answer);
            _status.EndResult();

            AppLog.Info($"[{target.ProcessName}] '{prompt.Name}' ({prompt.Mode}, overlay) ok via {capture.UsedStrategy}/{capture.Origin}, "
                + $"{capture.Text.Length} chars -> {answer.Length} chars, read {capture.Duration.TotalMilliseconds:F0} ms, "
                + $"total since {since} {Stopwatch.GetElapsedTime(origin).TotalMilliseconds:F0} ms");
            LatencyLog.Request(timings, origin, since);
        }
        catch (OperationCanceledException)
        {
            // A click on the pill while the text was being read. A card closed by Esc, a new call or another window cancels
            // through CloseCard, which has already hidden it.
            if (_run.IsCurrent(cts))
            {
                _status.HideStatus();
                AppLog.Info("Run cancelled by the user.");
            }
        }
        catch (Exception ex)
        {
            if (_run.IsCurrent(cts)) FailureMessages.Report(_status, ex, prompt, target, session.Anchor);
            else AppLog.Info($"[{target.ProcessName}] '{prompt.Name}': obsolete overlay run ended with {ex.GetType().Name}.");
        }
        finally
        {
            // A card that replaced this run (or closed it) owns the run and the busy flag by now.
            if (_run.IsCurrent(cts))
            {
                _run.ReleaseEsc();
                _run.Finish();
            }
        }
    }

    private void OnStatusCancelRequested()
    {
        if (_card.IsLive) _card.Close();
        else _run.Cancel();
    }

    /// <summary>
    /// Releases the temporary hotkeys (Esc of a run, Esc and copy of the card, copy of the error pill). Called before the
    /// configured hotkeys are registered anew, so a reload can never collide with them (a prompt may have been given the key that copies the card).
    /// </summary>
    public void ReleaseResultHotkeys()
    {
        _card.ReleaseForReload();
        _run.ReleaseForReload();
        _lastResult.ReleaseForReload();
    }

    /// <summary>Registers the temporary hotkeys again (with the current <see cref="ResultCopyHotkey"/>) if a card, a run or an error pill held them.</summary>
    public void RestoreResultHotkeys()
    {
        _card.RestoreAfterReload();
        _run.RestoreAfterReload(_card.IsLive);
        _lastResult.RestoreAfterReload();
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
        _card.Close();
        if (_run.Busy || _overlay.IsShown)
        {
            AppLog.Info("Undo hotkey ignored: busy.");
            return;
        }

        var target = _desktop.CaptureTarget();
        var anchor = _anchors.ForTarget(target);
        if (Reject(_text.PreCheck(target, null), target, anchor)) return;
        if (History.Count == 0)
        {
            _status.ShowInfo(Loc.Get("undo_nothing"), anchor);
            return;
        }

        _run.Busy = true;
        _ = UndoAsync(new Session(target, _text.ProbeAsync(target!), anchor));
    }

    private async Task UndoAsync(Session session)
    {
        var target = session.Target!;
        using var cts = new CancellationTokenSource();
        _run.Start(cts);
        try
        {
            var probe = await session.Probe;
            session = session with { Anchor = _anchors.Refine(session.Anchor, target, probe) };

            // Read what is in the field right now (selection, otherwise the whole field), exactly as for a normal run.
            var captured = await Task.Run(() => _text.CaptureAsync(target, ReadStrategy.Auto, probe, ct: cts.Token));
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
            _run.Finish();
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
}

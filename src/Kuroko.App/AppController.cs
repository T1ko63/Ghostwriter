using System.Diagnostics;
using System.Text;
using System.Windows.Media;
using Kuroko.App.Overlay;
using Kuroko.Core.Config;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Hotkeys;
using Kuroko.Core.Localization;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;
using Kuroko.Core.Undo;
using Kuroko.Platform.Hotkeys;
using Kuroko.Platform.Native;
using Kuroko.Platform.TextIntegration;
using Kuroko.Platform.Windowing;

namespace Kuroko.App;

/// <summary>result_position: a fixed place, the place of the prompt picker (follow), the text cursor or the mouse.</summary>
public enum ResultPlacement
{
    Fixed,
    Follow,
    Caret,
    Mouse,
}

/// <summary>How a notice in the status pill looks: an error, a quiet confirmation, or a progress bar that stays.</summary>
public enum NoticeKind
{
    Error,
    Info,
    Progress,
}

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

    /// <summary>Set while the overlay is up or a run is active; the 1 ms timer resolution is only held for that time.</summary>
    private bool Busy
    {
        set
        {
            _busy = value;
            NativeTimer.SetHighResolution(value);
        }
    }

    private readonly Action? _warmUp;
    private readonly HotkeyManager? _hotkeys;
    private readonly ClipboardService? _clipboard;

    /// <param name="prompts">Read on every use, so a reloaded prompts.toml is picked up without rebuilding anything.</param>
    /// <param name="hotkeys">Needed for the temporary hotkeys: Esc during a run, Esc and copy while the result card is up.</param>
    /// <param name="clipboard">Needed to copy the text of the result card and the last result.</param>
    public AppController(
        ITextAccess text, OverlayWindow overlay, StatusWindow status, IPromptRunner runner,
        Func<IReadOnlyList<PromptDefinition>> prompts, Action? warmUp = null,
        HotkeyManager? hotkeys = null, ClipboardService? clipboard = null)
    {
        _text = text;
        _overlay = overlay;
        _status = status;
        _runner = runner;
        _prompts = prompts;
        _warmUp = warmUp;
        _hotkeys = hotkeys;
        _clipboard = clipboard;

        _overlay.PromptChosen += prompt => _ = OnPromptChosenAsync(prompt);
        _overlay.Cancelled += () => _ = CloseOverlayAsync(restoreFocus: true);
        _overlay.Dismissed += () => _ = CloseOverlayAsync(restoreFocus: false);
        _status.CancelRequested += OnStatusCancelRequested;
        _status.ResultClosed += OnCardClosed;
        _status.ActionEnded += UnregisterRescueHotkey;
    }

    public OverlayPosition Position { get; set; } = OverlayPosition.Caret;

    /// <summary>overlay_fixed_position and overlay_screen_margin: used when <see cref="Position"/> is <see cref="OverlayPosition.Fixed"/>, and for the margin in every mode.</summary>
    public CardSpot OverlaySpot { get; set; } = CardSpot.TopThird;

    public double OverlayScreenMargin { get; set; } = AppSettings.DefaultOverlayScreenMargin;

    /// <summary>Size and font of the prompt picker (overlay_width, overlay_min_height, overlay_max_height, overlay_font_size); applied at once.</summary>
    public void SetOverlayLayout(double width, double minHeight, double maxHeight, double fontSize)
        => _overlay.SetLayout(width, minHeight, maxHeight, fontSize);

    /// <summary>Where the overlay and the pills go for the current target: a fixed place, or at the text cursor / mouse.</summary>
    private Anchor MakeAnchor(TargetInfo? target)
        => Position == OverlayPosition.Fixed
            ? Anchor.ResolveSpot(target, OverlaySpot, OverlayScreenMargin)
            : Anchor.Resolve(target, Position) with { MarginDip = OverlayScreenMargin };

    /// <summary>
    /// Hotkey that copies the result card, or the answer offered by the error pill after a failed paste (result_copy_hotkey);
    /// read when the card or pill opens and after a configuration reload.
    /// </summary>
    public string ResultCopyHotkey { get; set; } = AppSettings.DefaultResultCopyHotkey;

    /// <summary>result_position: where the card opens.</summary>
    public ResultPlacement ResultPlacement { get; set; } = ResultPlacement.Fixed;

    /// <summary>result_fixed_position.</summary>
    public CardSpot ResultSpot { get; set; } = CardSpot.BottomThird;

    /// <summary>result_screen_margin: distance of the card to the screen edge, in device-independent pixels.</summary>
    public double ResultScreenMargin { get; set; } = AppSettings.DefaultResultScreenMargin;

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

    public bool IsBusy => _busy || _overlay.IsShown || _cardLive;

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
        if (_busy || _cardLive)
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
        CloseCard(); // a visible result card never blocks the next call
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

        // Read-only targets are fine here: whether that matters is decided once a prompt is chosen (overlay output works, replace does not).
        var target = TargetInfo.Capture();
        var anchor = MakeAnchor(target);
        if (Reject(_text.PreCheck(target, null, CaptureMode.Display), target, anchor)) return;

        Busy = true;

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
        Busy = false;
        _status.ShowError(Loc.Get("err_unexpected", ex.GetType().Name), session.Anchor);
    }

    private async Task ShowOverlayCoreAsync(Session session, long hotkeyTimestamp)
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
        _overlay.ShowAnchored(session.Anchor);
        LogFrameLatency(hotkeyTimestamp, session.Target!.ProcessName, anchorKind);

        // The time the user needs to pick a prompt is used to have the connection to the AI provider ready.
        _warmUp?.Invoke();

        _ = ActivateAfterProbeAsync(session);
    }

    /// <summary>Moves a mouse-based anchor to the text cursor if UI Automation found it.</summary>
    private Anchor Refine(Anchor anchor, TargetInfo? target, FocusProbe probe, OverlayPosition? position = null)
        => (position ?? Position) == OverlayPosition.Caret && target?.CaretScreenPos is null && probe.Info?.CaretPoint is { } caret
            ? Anchor.AtCaret(caret)
            : anchor;

    /// <summary>Per-prompt hotkey: runs the prompt directly, without the overlay.</summary>
    public void OnPromptHotkey(long hotkeyTimestamp, PromptDefinition prompt)
    {
        MarkActivity();
        AppLog.Info($"hotkey: prompt '{prompt.Name}'");
        CloseCard(); // a visible result card never blocks the next call; its temporary hotkeys are released before anything is read
        if (_busy || _overlay.IsShown)
        {
            AppLog.Info($"Prompt hotkey '{prompt.Name}' ignored: busy.");
            return;
        }

        var target = TargetInfo.Capture();
        var anchor = MakeAnchor(target);
        if (Reject(_text.PreCheck(target, null, ModeOf(prompt)), target, anchor)) return;

        Busy = true;
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
            Busy = false;
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
            Busy = false;
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
        _runCts = cts;
        var started = Stopwatch.GetTimestamp();
        string? output = null; // set once the answer is complete; from then on a failure must not lose it
        RegisterRunEsc();
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

            var captured = await Task.Run(() => _text.CaptureAsync(target, ReadStrategy.Auto, probe, ct: cts.Token));
            if (!captured.Success)
            {
                Fail(Describe(captured), session, $"capture failed: {captured.Failure}");
                return;
            }

            var capture = captured.Capture!;

            // The runner resolves universal prompts itself (marker or not); the whole captured text is replaced either way.
            var timings = new LlmTimings();
            output = await _runner.RunAsync(prompt, capture.Text, cts.Token, timings);
            _lastResult = output;

            var replaced = await Task.Run(() => _text.ReplaceAsync(capture, output, cts.Token));
            if (!replaced.Success)
            {
                AppLog.Info($"[{target.ProcessName}] replace failed: {replaced.Failure}; the answer ({output.Length} chars) is kept for copying");
                ShowRescue(Describe(replaced), session.Anchor);
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
            // Cancelled on purpose (Esc or a click on the pill): no error pill, but a complete answer stays in the tray menu.
            _status.HideStatus();
            AppLog.Info(output is null ? "Run cancelled by the user." : "Run cancelled by the user before the paste; the answer is kept for copying.");
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
            // A rejected request may quote parts of the user's text back; that detail is shown, but not written to the log.
            var logDetail = ex.Kind == LlmErrorKind.BadRequest ? string.Empty : ex.Detail;
            AppLog.Warn($"AI call failed: {ex.Provider}: {ex.Kind} (HTTP {ex.StatusCode?.ToString() ?? "-"}) {logDetail}");
            _status.ShowError(Describe(ex), session.Anchor);
        }
        catch (Exception ex)
        {
            AppLog.Error("Run failed.", ex);
            var message = Loc.Get("err_unexpected", ex.GetType().Name);
            if (output is null) _status.ShowError(message, session.Anchor);
            else ShowRescue(message, session.Anchor);
        }
        finally
        {
            UnregisterRunEsc();
            _runCts = null;
            Busy = false;
        }
    }

    // ---- last result ----

    /// <summary>
    /// The last complete answer (pasted, shown in a card, or not pasted because the paste failed). Memory only: never on
    /// disk, never in the log; replaced by the next answer and gone when the app quits.
    /// </summary>
    private string? _lastResult;

    private int _rescueHotkeyId;
    private Anchor _rescueAnchor;
    private bool _rescueReleasedForReload;

    /// <summary>True once an answer has arrived; the tray entry "Copy last result" is greyed out until then.</summary>
    public bool HasLastResult => _lastResult is not null;

    /// <summary>
    /// The answer is complete but could not be pasted: the error pill says so and offers to copy it with
    /// result_copy_hotkey, registered only while this pill is up. The tray entry works as well, also later.
    /// </summary>
    private void ShowRescue(string message, Anchor anchor)
    {
        _rescueAnchor = anchor;
        var parsed = HotkeyGesture.TryParse(ResultCopyHotkey, out var gesture, out _);
        _status.ShowErrorWithAction(
            $"{message} {(parsed && _hotkeys is not null ? Loc.Get("rescue_hotkey", gesture) : Loc.Get("rescue_tray"))}", anchor);

        // Registered after the pill is up: showing it ends any earlier action pill, which releases that pill's hotkey.
        if (parsed && !RegisterRescueHotkey(gesture) && _hotkeys is not null)
        {
            _status.ShowErrorWithAction($"{message} {Loc.Get("rescue_tray")}", anchor);
        }
    }

    private bool RegisterRescueHotkey(HotkeyGesture gesture)
    {
        if (_hotkeys is null) return false;
        UnregisterRescueHotkey();
        var copy = _hotkeys.Register(gesture, OnRescueCopy, temporary: true);
        if (copy.Success) _rescueHotkeyId = copy.Id;
        else AppLog.Warn($"The copy hotkey could not be registered for the error pill: {copy.Error}");
        return copy.Success;
    }

    private void UnregisterRescueHotkey()
    {
        if (_hotkeys is null || _rescueHotkeyId == 0) return;
        _hotkeys.Unregister(_rescueHotkeyId);
        _rescueHotkeyId = 0;
    }

    private void OnRescueCopy(long hotkeyTimestamp)
    {
        AppLog.Info("hotkey: copy result (error pill)");
        if (!_status.IsActionShown) return;
        CopyLastResult(_rescueAnchor, showPill: true);
    }

    /// <summary>Tray entry "Copy last result". The pill is skipped while a run or a card is on screen, so neither is covered.</summary>
    public void CopyLastResult()
    {
        AppLog.Info("tray: copy last result");
        CopyLastResult(Anchor.Resolve(null, OverlayPosition.Mouse), showPill: !_busy && !_cardLive);
    }

    /// <summary>
    /// Puts the last answer on the clipboard as a normal copy: the user asked for it, so unlike what Kuroko puts there
    /// itself while reading and pasting, it may show up in the clipboard history.
    /// </summary>
    private void CopyLastResult(Anchor anchor, bool showPill)
    {
        if (_lastResult is null) return;
        if (_clipboard is null || !_clipboard.TrySetText(_lastResult, hidden: false))
        {
            AppLog.Info("Copying the last result failed: clipboard busy.");
            if (showPill) _status.ShowError(Loc.Get("err_clipboard_busy"), anchor);
            return;
        }

        AppLog.Info($"Last result copied: {_lastResult.Length} chars.");
        if (showPill) _status.ShowInfo(Loc.Get("result_copied"), anchor); // also ends the error pill, which releases its hotkey
    }

    // ---- result card (overlay output) ----

    /// <summary>
    /// True from the moment the answer is requested until the card is gone. Esc, the copy hotkey and the watch on the
    /// foreground window exist exactly as long as this is true. Unlike a run, a card that is only being read is not "busy".
    /// </summary>
    private bool _cardLive;

    private bool _cardComplete;
    private readonly StringBuilder _cardText = new();
    private Anchor _cardAnchor;
    private int _escHotkeyId;
    private int _copyHotkeyId;
    private ForegroundWatcher? _watcher;

    private static CaptureMode ModeOf(PromptDefinition prompt)
        => prompt.Output == PromptOutput.Overlay ? CaptureMode.Display : CaptureMode.Replace;

    /// <summary>
    /// A run whose result is shown in the card instead of replacing the text. Nothing in the target is changed: no
    /// paste, no history for undo, no "target changed" check. The text is read without ever selecting anything (no Ctrl+A).
    /// </summary>
    /// <summary>Where the card opens: a fixed place, like the picker (follow), at the text cursor, or at the mouse.</summary>
    private Anchor ResultAnchor(TargetInfo target, FocusProbe probe, Anchor pickerAnchor) => ResultPlacement switch
    {
        ResultPlacement.Fixed => Anchor.ResolveSpot(target, ResultSpot, ResultScreenMargin),
        ResultPlacement.Caret => Refine(Anchor.Resolve(target, OverlayPosition.Caret), target, probe, OverlayPosition.Caret) with { MarginDip = ResultScreenMargin },
        ResultPlacement.Mouse => Anchor.Resolve(target, OverlayPosition.Mouse) with { MarginDip = ResultScreenMargin },
        _ => Refine(pickerAnchor, target, probe) with { MarginDip = ResultScreenMargin },
    };

    private async Task ExecuteOverlayAsync(Session session, PromptDefinition prompt, bool restoreFocus, long? hotkeyTimestamp)
    {
        var target = session.Target!;
        using var cts = new CancellationTokenSource();
        _runCts = cts;
        var started = Stopwatch.GetTimestamp();
        var origin = hotkeyTimestamp ?? started;
        var since = hotkeyTimestamp.HasValue ? "hotkey" : "choice";
        RegisterRunEsc(); // until the card takes Esc over
        try
        {
            if (restoreFocus && !await WindowHelper.RestoreFocusAsync(target))
            {
                _status.ShowError(Loc.Get("err_focus"), session.Anchor);
                return;
            }

            var probe = await session.Probe;
            // By default the card has a fixed place on the screen: at the end of the selection it would often be pushed against
            // the screen edge (the selection can reach beyond the visible page) and cover what is being read.
            session = session with
            {
                Anchor = ResultAnchor(target, probe, session.Anchor),
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
            UnregisterRunEsc();
            OpenCard(session.Anchor, target);
            if (!ReferenceEquals(_runCts, cts)) return; // the foreground window had already changed

            var timings = new LlmTimings();
            var pieces = 0;
            await foreach (var piece in _runner.StreamAsync(prompt, capture.Text, cts.Token, timings))
            {
                if (!ReferenceEquals(_runCts, cts)) return; // the card was closed or replaced: this run is obsolete

                _cardText.Append(piece);
                _status.AppendResult(piece);
                if (pieces++ == 0) LogFirstText(target.ProcessName, prompt.Name, origin, since);
            }

            if (!ReferenceEquals(_runCts, cts)) return;
            _cardComplete = true;
            _lastResult = _cardText.ToString();
            _status.EndResult();

            AppLog.Info($"[{target.ProcessName}] '{prompt.Name}' ({prompt.Mode}, overlay) ok via {capture.UsedStrategy}/{capture.Origin}, "
                + $"{capture.Text.Length} chars -> {_cardText.Length} chars, read {capture.Duration.TotalMilliseconds:F0} ms, "
                + $"total since {since} {Stopwatch.GetElapsedTime(origin).TotalMilliseconds:F0} ms");
            if (timings.Sent != 0)
            {
                AppLog.Info($"  timing since {since}: request sent {LlmTimings.Ms(origin, timings.Sent):F0} ms, "
                    + $"response headers +{LlmTimings.Ms(timings.Sent, timings.Headers):F0} ms, first text +{LlmTimings.Ms(timings.Headers, timings.FirstToken):F0} ms, "
                    + $"answer complete +{LlmTimings.Ms(timings.FirstToken, timings.Done):F0} ms (attempts: {timings.Attempts})");
            }
        }
        catch (OperationCanceledException)
        {
            // A click on the pill while the text was being read. A card closed by Esc, a new call or another window cancels
            // through CloseCard, which has already hidden it.
            if (ReferenceEquals(_runCts, cts))
            {
                _status.HideStatus();
                AppLog.Info("Run cancelled by the user.");
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(_runCts, cts)) ReportFailure(ex, prompt, target, session.Anchor);
            else AppLog.Info($"[{target.ProcessName}] '{prompt.Name}': obsolete overlay run ended with {ex.GetType().Name}.");
        }
        finally
        {
            // A card that replaced this run (or closed it) owns _runCts and the busy flag by now.
            if (ReferenceEquals(_runCts, cts))
            {
                UnregisterRunEsc();
                _runCts = null;
                Busy = false;
            }
        }
    }

    /// <summary>The same messages as a replace run gives; the card (if it is up) is replaced by the error pill, so no half answer stays.</summary>
    private void ReportFailure(Exception ex, PromptDefinition prompt, TargetInfo target, Anchor anchor)
    {
        switch (ex)
        {
            case MarkerException marker:
                // An unclosed or empty marker: nothing was sent.
                AppLog.Info($"[{target.ProcessName}] '{prompt.Name}': marker {marker.Status}");
                _status.ShowError(marker.Status == MarkerStatus.Empty
                    ? Loc.Get("err_marker_empty")
                    : Loc.Get("err_marker_unclosed", marker.Start, marker.End), anchor);
                break;
            case LlmException llm:
                // A rejected request may quote parts of the user's text back; that detail is shown, but not written to the log.
                var logDetail = llm.Kind == LlmErrorKind.BadRequest ? string.Empty : llm.Detail;
                AppLog.Warn($"AI call failed: {llm.Provider}: {llm.Kind} (HTTP {llm.StatusCode?.ToString() ?? "-"}) {logDetail}");
                _status.ShowError(Describe(llm), anchor);
                break;
            default:
                AppLog.Error("Run failed.", ex);
                _status.ShowError(Loc.Get("err_unexpected", ex.GetType().Name), anchor);
                break;
        }
    }

    private void OpenCard(Anchor anchor, TargetInfo target)
    {
        _cardLive = true;
        _cardComplete = false;
        _cardText.Clear();
        _cardAnchor = anchor;
        RegisterCardHotkeys();

        // Installed last: it reports at once if the foreground window has already changed, which closes the card again.
        var watcher = ForegroundWatcher.Start(target.Window, OnForegroundChanged);
        if (_cardLive) _watcher = watcher;
        else watcher?.Dispose();
    }

    /// <summary>
    /// Closes the card if there is one: stops a request that is still streaming, hides the window and (through
    /// <see cref="OnCardClosed"/>) releases Esc, the copy hotkey and the foreground watch. Safe to call at any time.
    /// </summary>
    private void CloseCard()
    {
        if (!_cardLive) return;

        var run = _runCts;
        _runCts = null; // the run notices that it is obsolete and stays out of the way of whatever comes next
        if (run is not null)
        {
            run.Cancel();
            Busy = false;
        }

        AppLog.Info(run is null ? "Result card closed." : "Result card closed while the answer was streaming: request cancelled.");
        _status.HideStatus();
        OnCardClosed();
    }

    /// <summary>The window left result mode (closed, or replaced by an error or info pill): nothing of the card may stay registered.</summary>
    private void OnCardClosed()
    {
        if (!_cardLive) return;
        _cardLive = false;
        _cardComplete = false;
        _cardText.Clear();
        UnregisterCardHotkeys();
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnStatusCancelRequested()
    {
        if (_cardLive) CloseCard();
        else _runCts?.Cancel();
    }

    private void OnForegroundChanged()
    {
        if (!_cardLive) return;
        AppLog.Info("Foreground window changed: closing the result card.");
        CloseCard();
    }

    private void OnCardEsc(long hotkeyTimestamp)
    {
        AppLog.Info("hotkey: Esc (result card)");
        CloseCard();
    }

    private void OnCardCopy(long hotkeyTimestamp)
    {
        AppLog.Info("hotkey: copy result");
        if (!_cardLive) return;
        if (!_cardComplete)
        {
            AppLog.Info("Copy ignored: the answer is still streaming.");
            return;
        }

        var text = _cardText.ToString();
        if (_clipboard is null || !_clipboard.TrySetText(text, hidden: false))
        {
            _status.ShowError(Loc.Get("err_clipboard_busy"), _cardAnchor);
            return;
        }

        AppLog.Info($"Result copied: {text.Length} chars.");
        _status.ShowInfo(Loc.Get("result_copied"), _cardAnchor); // also ends result mode, which releases the hotkeys
    }

    private void RegisterCardHotkeys()
    {
        if (_hotkeys is null) return;
        UnregisterCardHotkeys();

        var esc = _hotkeys.Register(new HotkeyGesture(HotkeyModifiers.None, VK_ESCAPE), OnCardEsc, temporary: true);
        if (esc.Success) _escHotkeyId = esc.Id;
        else AppLog.Warn($"Esc could not be registered for the result card: {esc.Error}");

        if (!HotkeyGesture.TryParse(ResultCopyHotkey, out var gesture, out _)) return;
        var copy = _hotkeys.Register(gesture, OnCardCopy, temporary: true);
        if (copy.Success) _copyHotkeyId = copy.Id;
        else AppLog.Warn($"The copy hotkey could not be registered for the result card: {copy.Error}");
    }

    private void UnregisterCardHotkeys()
    {
        if (_hotkeys is null) return;
        if (_escHotkeyId != 0) _hotkeys.Unregister(_escHotkeyId);
        if (_copyHotkeyId != 0) _hotkeys.Unregister(_copyHotkeyId);
        _escHotkeyId = _copyHotkeyId = 0;
    }

    /// <summary>
    /// Releases the temporary hotkeys (Esc of a run, Esc and copy of the card, copy of the error pill). Called before the
    /// configured hotkeys are registered anew, so a reload can never collide with them (a prompt may have been given the key that copies the card).
    /// </summary>
    public void ReleaseResultHotkeys()
    {
        UnregisterCardHotkeys();
        _runEscReleasedForReload = _runEscHotkeyId != 0;
        UnregisterRunEsc();
        _rescueReleasedForReload = _rescueHotkeyId != 0;
        UnregisterRescueHotkey();
    }

    /// <summary>Registers the temporary hotkeys again (with the current <see cref="ResultCopyHotkey"/>) if a card, a run or an error pill held them.</summary>
    public void RestoreResultHotkeys()
    {
        if (_cardLive) RegisterCardHotkeys();
        if (_runEscReleasedForReload && _runCts is not null && !_cardLive) RegisterRunEsc();
        _runEscReleasedForReload = false;
        if (_rescueReleasedForReload && _status.IsActionShown && HotkeyGesture.TryParse(ResultCopyHotkey, out var gesture, out _))
        {
            RegisterRescueHotkey(gesture);
        }

        _rescueReleasedForReload = false;
    }

    // ---- Esc during a run ----

    /// <summary>Esc while a run reads the text or waits for the answer, before a card is up. Registered only for that time.</summary>
    private int _runEscHotkeyId;

    private bool _runEscReleasedForReload;

    private void RegisterRunEsc()
    {
        if (_hotkeys is null || _runEscHotkeyId != 0) return;
        var esc = _hotkeys.Register(new HotkeyGesture(HotkeyModifiers.None, VK_ESCAPE), OnRunEsc, temporary: true);
        if (esc.Success) _runEscHotkeyId = esc.Id;
        else AppLog.Warn($"Esc could not be registered for the run: {esc.Error}");
    }

    private void UnregisterRunEsc()
    {
        if (_hotkeys is null || _runEscHotkeyId == 0) return;
        _hotkeys.Unregister(_runEscHotkeyId);
        _runEscHotkeyId = 0;
    }

    /// <summary>The same as a click on the progress pill: once the paste has been sent, a cancel is no longer honoured.</summary>
    private void OnRunEsc(long hotkeyTimestamp)
    {
        AppLog.Info("hotkey: Esc (run)");
        _runCts?.Cancel();
    }

    private const int VK_ESCAPE = 0x1B;

    /// <summary>Logs the time from hotkey/choice to the first text of the card, once layout is done and once the frame is rendered.</summary>
    private static void LogFirstText(string app, string prompt, long origin, string since)
    {
        var layout = Stopwatch.GetElapsedTime(origin).TotalMilliseconds;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            CompositionTarget.Rendering -= handler;
            AppLog.Info($"[{app}] '{prompt}' first text in the card: layout done {layout:F0} ms, "
                + $"first frame {Stopwatch.GetElapsedTime(origin).TotalMilliseconds:F0} ms since {since}");
        };
        CompositionTarget.Rendering += handler;
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
        CloseCard();
        if (_busy || _overlay.IsShown)
        {
            AppLog.Info("Undo hotkey ignored: busy.");
            return;
        }

        var target = TargetInfo.Capture();
        var anchor = MakeAnchor(target);
        if (Reject(_text.PreCheck(target, null), target, anchor)) return;
        if (History.Count == 0)
        {
            _status.ShowInfo(Loc.Get("undo_nothing"), anchor);
            return;
        }

        Busy = true;
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
            _runCts = null;
            Busy = false;
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

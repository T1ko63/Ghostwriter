using System.Diagnostics;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Localization;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;
using Kuroko.Core.Undo;
using Kuroko.Platform.TextIntegration;
using static Kuroko.App.FailureMessages;

namespace Kuroko.App;

/// <summary>
/// A run that replaces the text: read it, ask the AI, paste the answer and remember the replacement for undo. A complete
/// answer that cannot be pasted is kept for copying.
/// </summary>
internal sealed class ReplaceRun(
    ITextAccess text, IStatusView status, IPromptRunner runner, IDesktop desktop, RunState run, AnchorPolicy anchors,
    LastResult lastResult, ReplacementHistory history)
{
    private readonly ITextAccess _text = text;
    private readonly IStatusView _status = status;
    private readonly IPromptRunner _runner = runner;
    private readonly IDesktop _desktop = desktop;
    private readonly RunState _run = run;
    private readonly AnchorPolicy _anchors = anchors;
    private readonly LastResult _lastResult = lastResult;
    private readonly ReplacementHistory _history = history;

    /// <summary>Runs <paramref name="prompt"/> for a session the caller has made busy; frees the controller when done.</summary>
    public async Task ExecuteAsync(RunSession session, PromptDefinition prompt, bool restoreFocus, long? hotkeyTimestamp)
    {
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
                Fail(_status, Describe(captured), session, $"capture failed: {captured.Failure}");
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
            _history.Add(new ReplacementRecord(capture.Text, output, UndoRun.FieldOf(target)));
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
}

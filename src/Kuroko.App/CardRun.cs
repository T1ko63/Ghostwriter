using System.Diagnostics;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Localization;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;
using Kuroko.Platform.TextIntegration;
using static Kuroko.App.FailureMessages;

namespace Kuroko.App;

/// <summary>
/// A run whose result is shown in the card instead of replacing the text. Nothing in the target is changed: no paste, no
/// history for undo, no "target changed" check. The text is read without ever selecting anything (no Ctrl+A).
/// </summary>
internal sealed class CardRun(
    ITextAccess text, IStatusView status, IPromptRunner runner, IDesktop desktop, RunState run, AnchorPolicy anchors,
    LastResult lastResult, ResultCard card, LatencyLog latency)
{
    private readonly ITextAccess _text = text;
    private readonly IStatusView _status = status;
    private readonly IPromptRunner _runner = runner;
    private readonly IDesktop _desktop = desktop;
    private readonly RunState _run = run;
    private readonly AnchorPolicy _anchors = anchors;
    private readonly LastResult _lastResult = lastResult;
    private readonly ResultCard _card = card;
    private readonly LatencyLog _latency = latency;

    /// <summary>Runs <paramref name="prompt"/> for a session the caller has made busy; frees the controller when done, unless a newer call took over.</summary>
    public async Task ExecuteAsync(RunSession session, PromptDefinition prompt, bool restoreFocus, long? hotkeyTimestamp)
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
                Fail(_status, captured.Failure == CaptureFailure.NoText ? Loc.Get("err_no_selection") : Describe(captured), session,
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

    /// <summary>Runs on the UI thread: directly if already there, otherwise queued (the runner's callbacks do not promise a thread).</summary>
    private void OnUi(Action action) => _status.RunOnUi(action);
}

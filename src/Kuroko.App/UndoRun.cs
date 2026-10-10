using Kuroko.Core.Diagnostics;
using Kuroko.Core.Localization;
using Kuroko.Core.Undo;
using Kuroko.Platform.TextIntegration;
using static Kuroko.App.FailureMessages;

namespace Kuroko.App;

/// <summary>Puts the original text back, but only where the stored result is still found.</summary>
internal sealed class UndoRun(ITextAccess text, IStatusView status, RunState run, AnchorPolicy anchors, ReplacementHistory history)
{
    /// <summary>The field a replacement was made in, as the history remembers it.</summary>
    public static FieldId FieldOf(TargetInfo target) => new(target.Window, target.ProcessId, target.FocusWindow);

    /// <summary>Runs the undo for a session the caller has made busy; frees the controller when done.</summary>
    public async Task ExecuteAsync(RunSession session)
    {
        var target = session.Target!;
        using var cts = new CancellationTokenSource();
        run.Start(cts);
        try
        {
            var probe = await session.Probe;
            session = session with { Anchor = anchors.Refine(session.Anchor, target, probe) };

            // Read what is in the field right now (selection, otherwise the whole field), exactly as for a normal run.
            var captured = await Task.Run(() => text.CaptureAsync(target, ReadStrategy.Auto, probe, ct: cts.Token));
            if (!captured.Success)
            {
                // An empty field cannot contain the stored result.
                Fail(status, captured.Failure == CaptureFailure.NoText ? Loc.Get("undo_changed") : Describe(captured), session,
                    $"undo: capture failed: {captured.Failure}");
                return;
            }

            var capture = captured.Capture!;
            var plan = history.Plan(FieldOf(target), capture.Text);
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
                status.ShowInfo(message, session.Anchor);
                return;
            }

            var replaced = await Task.Run(() => text.ReplaceAsync(capture, plan.NewText!, cts.Token));
            if (!replaced.Success)
            {
                Fail(status, Describe(replaced), session, $"undo: replace failed: {replaced.Failure}");
                return;
            }

            history.Commit(plan.Record!);
            status.ShowInfo(Loc.Get("undo_done"), session.Anchor);
            AppLog.Info($"[{target.ProcessName}] undo ok via {capture.UsedStrategy}/{capture.Origin}, {capture.Text.Length} -> {plan.NewText!.Length} chars, "
                + $"{history.Count} left in history");
        }
        catch (OperationCanceledException)
        {
            status.HideStatus();
        }
        catch (Exception ex)
        {
            AppLog.Error("Undo failed.", ex);
            status.ShowError(Unexpected(ex), session.Anchor);
        }
        finally
        {
            run.Finish();
        }
    }
}

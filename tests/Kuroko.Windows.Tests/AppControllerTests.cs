using Kuroko.App;
using Kuroko.Core.Hotkeys;
using Kuroko.Core.Localization;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;
using Kuroko.Platform.TextIntegration;
using Kuroko.Windows.Tests.Fakes;

namespace Kuroko.Windows.Tests;

/// <summary>
/// The run and cancel flow of AppController: busy flag, Esc, the pills it shows, keeping an answer that could not be
/// pasted, the fallback note and the overlay paths. Windows, hotkeys, clipboard and the AI are fakes; every test runs on
/// one thread with a message loop, like the UI thread.
/// </summary>
public class AppControllerTests
{
    private static readonly PromptDefinition Fix = new("Fix", "Fix the spelling.");
    private static readonly PromptDefinition Explain = new("Explain", "Explain this.", Output: PromptOutput.Overlay);

    private readonly FakeTextAccess _text = new();
    private readonly FakeRunner _runner = new();
    private readonly FakeOverlay _overlay = new();
    private readonly FakeStatus _status = new();
    private readonly FakeHotkeys _hotkeys = new();
    private readonly FakeTextTarget _clipboard = new();
    private readonly FakeDesktop _desktop = new();

    private AppController Controller()
        => new(_text, _overlay, _status, _runner, () => [Fix, Explain], hotkeys: _hotkeys, clipboard: _clipboard, desktop: _desktop);

    private static string Working(PromptDefinition prompt) => $"progress: {Loc.Get("working", prompt.Name)}";

    private static async Task Idle(AppController controller) => await UiThread.Until(() => !controller.IsBusy, "controller idle");

    // ---- a normal run ----

    [Fact]
    public void A_prompt_hotkey_reads_asks_and_replaces() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = "the text";

        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.Equal(["teh text"], _runner.Inputs);
        Assert.Equal([("teh text", "the text")], _text.Replacements);
        Assert.Equal([Working(Fix), "hide"], _status.Shown);
        Assert.Equal(1, controller.History.Count);
        Assert.True(controller.HasLastResult);
        Assert.False(_desktop.HighResolution);
        Assert.Equal(0, _hotkeys.Count);
    });

    [Fact]
    public void While_a_run_waits_for_the_ai_it_is_busy_holds_esc_and_the_fine_timer() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = null;

        controller.OnPromptHotkey(0, Fix);
        await UiThread.Until(() => _runner.Waiting, "AI call");

        Assert.True(controller.IsBusy);
        Assert.True(_desktop.HighResolution);
        Assert.True(_hotkeys.IsRegistered(FakeHotkeys.Esc));

        _runner.Answer("done");
        await Idle(controller);
        Assert.False(_hotkeys.IsRegistered(FakeHotkeys.Esc));
    });

    [Fact]
    public void A_second_hotkey_during_a_run_is_ignored() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = null;
        controller.OnPromptHotkey(0, Fix);
        await UiThread.Until(() => _runner.Waiting, "AI call");

        controller.OnPromptHotkey(0, Fix);
        controller.OnOverlayHotkey(0);
        controller.OnUndoHotkey(0);

        Assert.Single(_text.Calls, c => c.StartsWith("capture"));
        Assert.False(_overlay.IsShown);
        _runner.Answer("done");
        await Idle(controller);
    });

    [Fact]
    public void A_rejected_target_shows_why_and_leaves_the_controller_free() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _text.PreCheckResult = CaptureResult.Fail(CaptureFailure.PasswordField);

        controller.OnPromptHotkey(0, Fix);
        await UiThread.Settle();

        Assert.Equal([$"error: {Loc.Get("err_password")}"], _status.Shown);
        Assert.False(controller.IsBusy);
        Assert.DoesNotContain(_text.Calls, c => c.StartsWith("capture"));
    });

    [Fact]
    public void No_window_under_the_hotkey_is_reported() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _desktop.Target = null;
        _text.PreCheckResult = CaptureResult.Fail(CaptureFailure.NoTarget);

        controller.OnPromptHotkey(0, Fix);
        await UiThread.Settle();

        Assert.Equal([$"error: {Loc.Get("err_no_window")}"], _status.Shown);
        Assert.False(controller.IsBusy);
    });

    // ---- cancelling ----

    [Fact]
    public void Esc_while_the_ai_works_cancels_quietly_and_changes_nothing() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = null;
        controller.OnPromptHotkey(0, Fix);
        await UiThread.Until(() => _runner.Waiting, "AI call");

        Assert.True(_hotkeys.Press(FakeHotkeys.Esc));
        await Idle(controller);

        Assert.True(_runner.LastToken.IsCancellationRequested);
        Assert.DoesNotContain("replace", _text.Calls);
        Assert.Equal([Working(Fix), "hide"], _status.Shown);
        Assert.False(controller.HasLastResult);
        Assert.False(_hotkeys.IsRegistered(FakeHotkeys.Esc));
        Assert.False(_desktop.HighResolution);
    });

    [Fact]
    public void Esc_while_the_text_is_being_read_stops_before_the_ai_is_asked() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _text.CaptureGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.OnPromptHotkey(0, Fix);
        await UiThread.Until(() => _text.Calls.Contains("capture Replace"), "capture");

        _hotkeys.Press(FakeHotkeys.Esc);
        Assert.True(_text.LastCaptureToken.IsCancellationRequested);
        _text.CaptureGate.SetResult();
        await Idle(controller);

        Assert.DoesNotContain("replace", _text.Calls);
        Assert.Equal("hide", _status.Last);
        Assert.DoesNotContain(_status.Shown, s => s.StartsWith("error"));
    });

    [Fact]
    public void A_click_on_the_progress_pill_cancels_like_esc() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = null;
        controller.OnPromptHotkey(0, Fix);
        await UiThread.Until(() => _runner.Waiting, "AI call");

        _status.Click();
        await Idle(controller);

        Assert.DoesNotContain("replace", _text.Calls);
        Assert.Equal("hide", _status.Last);
    });

    [Fact]
    public void A_cancel_after_the_answer_but_before_the_paste_keeps_the_answer() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = "the answer";
        _runner.BeforeReturn = () => _hotkeys.Press(FakeHotkeys.Esc); // the answer is complete, the paste not yet sent

        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.Empty(_text.Replacements);
        Assert.True(controller.HasLastResult);
        controller.CopyLastResult();
        Assert.Equal("the answer", _clipboard.ClipboardText);
    });

    [Fact]
    public void Esc_after_the_run_ended_is_no_longer_taken_from_other_apps() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.False(_hotkeys.Press(FakeHotkeys.Esc));
    });

    // ---- failures ----

    [Theory]
    [InlineData(CaptureFailure.ClipboardBusy, "err_clipboard_busy")]
    [InlineData(CaptureFailure.InputBlocked, "err_input_blocked")]
    [InlineData(CaptureFailure.NoText, "err_no_text")]
    [InlineData(CaptureFailure.TooLong, "err_too_long")]
    [InlineData(CaptureFailure.ReadOnlyField, "err_read_only")]
    public void A_failed_capture_shows_the_reason_and_never_asks_the_ai(CaptureFailure failure, string key) => UiThread.Run(async () =>
    {
        var controller = Controller();
        _text.CaptureFailure = failure;

        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.Equal($"error: {Loc.Get(key)}", _status.Last);
        Assert.Empty(_runner.Inputs);
        Assert.Equal(0, _hotkeys.Count);
    });

    [Fact]
    public void An_ai_error_is_shown_and_nothing_is_pasted() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = null;
        controller.OnPromptHotkey(0, Fix);
        await UiThread.Until(() => _runner.Waiting, "AI call");

        _runner.Fail(new LlmException(LlmErrorKind.RateLimit, "OpenAI"));
        await Idle(controller);

        Assert.Equal($"error: {Loc.Get("llm_rate_limit", "OpenAI", string.Empty)}", _status.Last);
        Assert.DoesNotContain("replace", _text.Calls);
        Assert.False(controller.HasLastResult);
    });

    [Theory]
    [InlineData(ReplaceFailure.TargetChanged, "err_target_changed")]
    [InlineData(ReplaceFailure.PasteNotAcknowledged, "err_paste_ack")]
    [InlineData(ReplaceFailure.ClipboardBusy, "err_clipboard_busy")]
    [InlineData(ReplaceFailure.SelectAllFailed, "err_select_all")]
    public void A_failed_paste_keeps_the_answer_and_offers_to_copy_it(ReplaceFailure failure, string key) => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = "the answer";
        _text.ReplaceFailure = failure;

        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.Equal($"action: {Loc.Get(key)} {Loc.Get("rescue_hotkey", FakeHotkeys.Copy)}", _status.Last);
        Assert.True(_status.IsActionShown);
        Assert.True(_hotkeys.IsRegistered(FakeHotkeys.Copy));
        Assert.Equal(0, controller.History.Count);

        Assert.True(_hotkeys.Press(FakeHotkeys.Copy));
        Assert.Equal("the answer", _clipboard.ClipboardText);
        Assert.Equal($"info: {Loc.Get("result_copied")}", _status.Last);
        Assert.False(_hotkeys.IsRegistered(FakeHotkeys.Copy)); // released together with the pill
    });

    [Fact]
    public void The_copy_hotkey_of_the_error_pill_goes_away_with_the_pill() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _text.ReplaceFailure = ReplaceFailure.TargetChanged;
        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        _status.Expire();

        Assert.False(_hotkeys.IsRegistered(FakeHotkeys.Copy));
        Assert.False(_hotkeys.Press(FakeHotkeys.Copy));
    });

    [Fact]
    public void When_the_copy_hotkey_is_taken_the_pill_points_to_the_tray() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _hotkeys.Taken.Add(FakeHotkeys.Copy);
        _text.ReplaceFailure = ReplaceFailure.PasteNotAcknowledged;

        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.Equal($"action: {Loc.Get("err_paste_ack")} {Loc.Get("rescue_tray")}", _status.Last);
        Assert.True(controller.HasLastResult);
    });

    [Fact]
    public void A_copy_into_a_busy_clipboard_says_so() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _text.ReplaceFailure = ReplaceFailure.TargetChanged;
        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        _clipboard.SetTextFails = true;
        _hotkeys.Press(FakeHotkeys.Copy);

        Assert.Equal($"error: {Loc.Get("err_clipboard_busy")}", _status.Last);
    });

    [Fact]
    public void An_unexpected_error_after_the_answer_still_offers_the_answer() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _text.ReplaceThrows = new InvalidOperationException("boom");

        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.StartsWith($"action: {Loc.Get("err_unexpected", nameof(InvalidOperationException))}", _status.Last);
        Assert.True(controller.HasLastResult);
    });

    [Fact]
    public void An_unexpected_error_before_the_answer_is_an_error_pill_and_frees_the_controller() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _text.CaptureThrows = new InvalidOperationException("boom");

        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.Equal($"error: {Loc.Get("err_unexpected", nameof(InvalidOperationException))}", _status.Last);
        Assert.False(controller.HasLastResult);
        Assert.Equal(0, _hotkeys.Count);
    });

    // ---- fallback provider ----

    [Fact]
    public void A_switch_to_the_fallback_provider_is_shown_while_waiting_and_after_the_paste() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.Fallback = new ProviderFallback("OpenAI", "Gemini", LlmErrorKind.Network);

        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.Equal(
            [Working(Fix), $"update: {Loc.Get("fallback_working", "OpenAI", "Gemini")}", $"info: {Loc.Get("fallback_done", "OpenAI", "Gemini")}"],
            _status.Shown);
        Assert.Single(_text.Replacements);
    });

    // ---- overlay (prompt picker) ----

    [Fact]
    public void The_overlay_opens_takes_input_and_runs_the_chosen_prompt_in_the_original_window() => UiThread.Run(async () =>
    {
        var controller = Controller();

        controller.OnOverlayHotkey(0);
        await UiThread.Until(() => _overlay.Activated, "overlay active");
        Assert.True(controller.IsBusy);

        _overlay.Choose(Fix);
        await Idle(controller);

        Assert.False(_overlay.IsShown);
        Assert.Equal(1, _desktop.FocusRestores);
        Assert.Single(_text.Replacements);
    });

    [Fact]
    public void Closing_the_overlay_gives_the_focus_back_and_frees_the_controller() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnOverlayHotkey(0);
        await UiThread.Until(() => _overlay.Activated, "overlay active");

        _overlay.Cancel();
        await Idle(controller);

        Assert.Equal(1, _desktop.FocusRestores);
        Assert.False(_desktop.HighResolution);
        Assert.Empty(_runner.Inputs);
    });

    [Fact]
    public void An_overlay_dismissed_by_a_click_elsewhere_leaves_the_focus_where_the_user_put_it() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnOverlayHotkey(0);
        await UiThread.Until(() => _overlay.Activated, "overlay active");

        _overlay.Dismiss();
        await Idle(controller);

        Assert.Equal(0, _desktop.FocusRestores);
        Assert.Empty(_runner.Inputs);
    });

    [Fact]
    public void The_overlay_hotkey_again_closes_the_overlay() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnOverlayHotkey(0);
        await UiThread.Until(() => _overlay.Activated, "overlay active");

        controller.OnOverlayHotkey(0);
        await Idle(controller);

        Assert.False(_overlay.IsShown);
    });

    [Fact]
    public void A_password_field_found_after_the_overlay_opened_closes_it_again() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _text.PreCheckWithProbeResult = CaptureResult.Fail(CaptureFailure.PasswordField);

        controller.OnOverlayHotkey(0);
        await Idle(controller);

        Assert.False(_overlay.IsShown);
        Assert.False(_overlay.Activated);
        Assert.Equal($"error: {Loc.Get("err_password")}", _status.Last);
    });

    [Fact]
    public void A_failure_while_the_overlay_opens_does_not_leave_the_controller_busy() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _overlay.ShowThrows = new InvalidOperationException("no window");

        controller.OnOverlayHotkey(0);
        await Idle(controller);

        Assert.Equal($"error: {Loc.Get("err_unexpected", nameof(InvalidOperationException))}", _status.Last);
        Assert.False(_desktop.HighResolution);

        _overlay.ShowThrows = null;
        controller.OnOverlayHotkey(0); // the next hotkey works again
        await UiThread.Until(() => _overlay.Activated, "overlay active");
    });

    [Fact]
    public void If_the_focus_cannot_be_given_back_nothing_is_read() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnOverlayHotkey(0);
        await UiThread.Until(() => _overlay.Activated, "overlay active");

        _desktop.FocusComesBack = false;
        _overlay.Choose(Fix);
        await Idle(controller);

        Assert.Equal($"error: {Loc.Get("err_focus")}", _status.Last);
        Assert.DoesNotContain(_text.Calls, c => c.StartsWith("capture"));
    });

    // ---- result card (overlay output) ----

    [Fact]
    public void An_overlay_prompt_streams_into_the_card_and_never_pastes() => UiThread.Run(async () =>
    {
        var controller = Controller();

        controller.OnPromptHotkey(0, Explain);
        await UiThread.Until(() => _runner.Waiting, "stream");
        Assert.Contains("capture Display", _text.Calls);
        Assert.True(_hotkeys.IsRegistered(FakeHotkeys.Esc));
        Assert.True(_hotkeys.IsRegistered(FakeHotkeys.Copy));

        _runner.Piece("It ");
        await UiThread.Until(() => _status.CardText == "It ", "first piece");
        _runner.Piece("means this.");
        _runner.EndStream();
        await UiThread.Until(() => _status.Last == "end", "end of answer");
        Assert.True(controller.IsBusy); // the card is up until it is closed

        Assert.Equal("It means this.", _status.CardText);
        Assert.DoesNotContain("replace", _text.Calls);
        Assert.True(controller.HasLastResult);

        _hotkeys.Press(FakeHotkeys.Copy);
        Assert.Equal("It means this.", _clipboard.ClipboardText);
        Assert.Equal(0, _hotkeys.Count); // the info pill ended the card and its hotkeys
        Assert.False(controller.IsBusy);
        Assert.False(_desktop.HighResolution);
    });

    [Fact]
    public void Esc_closes_a_streaming_card_and_cancels_the_request() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnPromptHotkey(0, Explain);
        await UiThread.Until(() => _runner.Waiting, "stream");
        _runner.Piece("partial");
        await UiThread.Until(() => _status.CardText == "partial", "first piece");

        _hotkeys.Press(FakeHotkeys.Esc);
        await Idle(controller);
        await UiThread.Until(() => !_runner.Waiting, "stream ended");

        Assert.True(_runner.LastToken.IsCancellationRequested);
        Assert.Equal("hide", _status.Last);
        Assert.Equal(0, _hotkeys.Count);
        Assert.False(_desktop.Watching);
        Assert.False(controller.HasLastResult); // half an answer is not kept
    });

    [Fact]
    public void Copy_is_ignored_while_the_answer_is_still_streaming() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnPromptHotkey(0, Explain);
        await UiThread.Until(() => _runner.Waiting, "stream");
        _runner.Piece("partial");
        await UiThread.Until(() => _status.CardText == "partial", "first piece");

        _hotkeys.Press(FakeHotkeys.Copy);

        Assert.Equal("user clipboard", _clipboard.ClipboardText);
        _hotkeys.Press(FakeHotkeys.Esc);
        await Idle(controller);
    });

    [Fact]
    public void Switching_to_another_window_closes_the_card() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnPromptHotkey(0, Explain);
        await UiThread.Until(() => _runner.Waiting, "stream");

        _desktop.SwitchWindow();
        await Idle(controller);

        Assert.True(_runner.LastToken.IsCancellationRequested);
        Assert.Equal(0, _hotkeys.Count);
    });

    [Fact]
    public void A_new_hotkey_replaces_the_card() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnPromptHotkey(0, Explain);
        await UiThread.Until(() => _runner.Waiting, "stream");
        var firstRun = _runner.LastToken;

        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        Assert.True(firstRun.IsCancellationRequested);
        Assert.Single(_text.Replacements);
    });

    [Fact]
    public void An_error_in_the_middle_of_the_answer_replaces_the_card_with_the_error() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnPromptHotkey(0, Explain);
        await UiThread.Until(() => _runner.Waiting, "stream");
        _runner.Piece("half an ans");
        await UiThread.Until(() => _status.CardText == "half an ans", "first piece");

        _runner.FailStream(new LlmException(LlmErrorKind.Network, "OpenAI"));
        await Idle(controller);

        Assert.Equal($"error: {Loc.Get("llm_network", "OpenAI")}", _status.Last);
        Assert.False(controller.HasLastResult);
        Assert.Equal(0, _hotkeys.Count);
    });

    [Fact]
    public void An_overlay_prompt_without_a_selection_says_so() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _text.CaptureFailure = CaptureFailure.NoText;

        controller.OnPromptHotkey(0, Explain);
        await Idle(controller);

        Assert.Equal($"error: {Loc.Get("err_no_selection")}", _status.Last);
        Assert.Equal(0, _hotkeys.Count);
    });

    // ---- undo ----

    [Fact]
    public void Undo_puts_the_original_back_only_while_the_result_is_still_there() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);

        controller.OnUndoHotkey(0);
        await Idle(controller);

        Assert.Equal(("the text", "teh text"), _text.Replacements[^1]);
        Assert.Equal($"info: {Loc.Get("undo_done")}", _status.Last);

        _text.FieldText = "edited by the user";
        controller.OnUndoHotkey(0);
        await Idle(controller);
        Assert.Equal($"info: {Loc.Get("undo_nothing")}", _status.Last);
    });

    [Fact]
    public void Undo_refuses_when_the_field_was_changed_since() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);
        _text.FieldText = "edited by the user";

        controller.OnUndoHotkey(0);
        await Idle(controller);

        Assert.Equal($"info: {Loc.Get("undo_changed")}", _status.Last);
        Assert.Single(_text.Replacements);
    });

    // ---- configuration reload ----

    [Fact]
    public void A_reload_during_a_run_hands_esc_back_and_takes_it_again() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = null;
        controller.OnPromptHotkey(0, Fix);
        await UiThread.Until(() => _runner.Waiting, "AI call");

        controller.ReleaseResultHotkeys();
        Assert.Equal(0, _hotkeys.Count);
        controller.RestoreResultHotkeys();
        Assert.True(_hotkeys.IsRegistered(FakeHotkeys.Esc));

        _hotkeys.Press(FakeHotkeys.Esc);
        await Idle(controller);
        Assert.Empty(_text.Replacements);
        Assert.Equal(0, _hotkeys.Count);
    });

    [Fact]
    public void A_reload_while_the_card_is_up_registers_its_keys_again_with_the_new_copy_hotkey() => UiThread.Run(async () =>
    {
        var controller = Controller();
        controller.OnPromptHotkey(0, Explain);
        await UiThread.Until(() => _runner.Waiting, "stream");
        _runner.Piece("It means this.");
        _runner.EndStream();
        await UiThread.Until(() => _status.Last == "end", "end of answer");

        controller.ReleaseResultHotkeys();
        Assert.Equal(0, _hotkeys.Count);
        controller.ResultCopyHotkey = "Ctrl+Alt+K";
        controller.RestoreResultHotkeys();

        var newCopy = HotkeyGesture.Parse("Ctrl+Alt+K");
        Assert.True(_hotkeys.IsRegistered(FakeHotkeys.Esc));
        Assert.True(_hotkeys.IsRegistered(newCopy));
        Assert.Equal(2, _hotkeys.Count); // the card's Esc only: the run handed Esc over when the card opened

        _hotkeys.Press(newCopy);
        Assert.Equal("It means this.", _clipboard.ClipboardText);
        Assert.Equal(0, _hotkeys.Count);
    });

    [Fact]
    public void A_reload_keeps_the_copy_offer_of_the_error_pill_only_while_the_pill_is_up() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = "the answer";
        _text.ReplaceFailure = ReplaceFailure.TargetChanged;
        controller.OnPromptHotkey(0, Fix);
        await Idle(controller);
        Assert.True(_hotkeys.IsRegistered(FakeHotkeys.Copy));

        controller.ReleaseResultHotkeys();
        Assert.Equal(0, _hotkeys.Count);
        controller.RestoreResultHotkeys();
        Assert.True(_hotkeys.IsRegistered(FakeHotkeys.Copy));

        _hotkeys.Press(FakeHotkeys.Copy); // the info pill ends the error pill and its hotkey
        Assert.Equal("the answer", _clipboard.ClipboardText);
        controller.ReleaseResultHotkeys();
        controller.RestoreResultHotkeys();
        Assert.Equal(0, _hotkeys.Count);
    });

    // ---- notices ----

    [Fact]
    public void A_notice_never_covers_a_running_prompt() => UiThread.Run(async () =>
    {
        var controller = Controller();
        _runner.AutoAnswer = null;
        controller.OnPromptHotkey(0, Fix);
        await UiThread.Until(() => _runner.Waiting, "AI call");

        controller.ShowNotice("configuration loaded", NoticeKind.Info);

        Assert.DoesNotContain(_status.Shown, s => s.Contains("configuration loaded"));
        _runner.Answer("x");
        await Idle(controller);
    });
}

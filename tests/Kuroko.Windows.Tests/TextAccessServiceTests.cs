using Kuroko.Platform.TextIntegration;
using Kuroko.Windows.Tests.Fakes;

namespace Kuroko.Windows.Tests;

/// <summary>
/// The decisions of TextAccessService: which strategy reads the text, which keys are sent, when nothing may be pasted,
/// and that the user's clipboard is always put back. The clipboard, the keys and UI Automation are fakes.
/// </summary>
public class TextAccessServiceTests
{
    private static readonly TextAccessTimings Fast = new()
    {
        CopyTimeout = TimeSpan.FromMilliseconds(150),
        CopyFollowUp = TimeSpan.FromMilliseconds(100),
        PasteTimeout = TimeSpan.FromMilliseconds(150),
        PasteGrace = TimeSpan.FromMilliseconds(1),
        PasteGraceRemote = TimeSpan.FromMilliseconds(1),
    };

    private readonly FakeTextTarget _app = new("user clipboard");
    private readonly FakeFieldInspector _uia = new();

    private TextAccessService Service() => new(_app, _app, _uia) { Timings = Fast };

    private static TargetInfo Target(string process = "notepad", string windowClass = "Notepad",
        bool password = false, bool readOnly = false, bool elevated = false)
        => new(100, 101, windowClass, 1, process, elevated, password, readOnly, 7, null);

    private static UiaFocusInfo Uia(string? selected = null, string? whole = null, bool selectionKnown = true,
        bool editable = true, bool readOnly = false, bool password = false, bool tooLong = false, bool web = false,
        string controlType = "ControlType.Edit")
        => new(controlType, password, true, selectionKnown, selected, whole, editable, readOnly, tooLong)
        {
            Element = new FieldElement(),
            IsWebEngine = web,
        };

    // ---- pre-checks ----

    [Fact]
    public void No_target_is_rejected()
        => Assert.Equal(CaptureFailure.NoTarget, Service().PreCheck(null, null)!.Failure);

    [Fact]
    public void Terminals_are_rejected_before_any_key_is_sent()
    {
        var result = Service().PreCheck(Target("WindowsTerminal", "CASCADIA_HOSTING_WINDOW_CLASS"), null);

        Assert.Equal(CaptureFailure.UnsupportedApp, result!.Failure);
        Assert.Empty(_app.Keys);
    }

    [Fact]
    public void Password_fields_are_rejected_from_win32_style_or_from_uia()
    {
        Assert.Equal(CaptureFailure.PasswordField, Service().PreCheck(Target(password: true), null)!.Failure);
        Assert.Equal(CaptureFailure.PasswordField, Service().PreCheck(Target(), new FocusProbe(Uia(password: true)))!.Failure);
    }

    [Fact]
    public void Read_only_fields_are_rejected_for_replace_but_not_for_display()
    {
        Assert.Equal(CaptureFailure.ReadOnlyField, Service().PreCheck(Target(readOnly: true), null, CaptureMode.Replace)!.Failure);
        Assert.Null(Service().PreCheck(Target(readOnly: true), null, CaptureMode.Display));
        Assert.Equal(CaptureFailure.ReadOnlyField,
            Service().PreCheck(Target(), new FocusProbe(Uia(readOnly: true)), CaptureMode.Replace)!.Failure);
    }

    [Fact]
    public void An_elevated_target_is_rejected_unless_Kuroko_runs_elevated_too()
    {
        var result = Service().PreCheck(Target(elevated: true), null);

        if (TargetInfo.SelfIsElevated) Assert.Null(result);
        else Assert.Equal(CaptureFailure.ElevatedTarget, result!.Failure);
    }

    // ---- reading through UI Automation ----

    [Fact]
    public async Task A_uia_selection_is_used_without_keys_or_clipboard()
    {
        _uia.Info = Uia(selected: "selected words");

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.True(result.Success);
        Assert.Equal("selected words", result.Capture!.Text);
        Assert.Equal(ReadStrategy.Uia, result.Capture.UsedStrategy);
        Assert.Equal(TextOrigin.Selection, result.Capture.Origin);
        Assert.Empty(_app.Keys);
        Assert.Equal(0, _app.Snapshots);
    }

    [Fact]
    public async Task A_caret_in_a_provably_editable_field_reads_the_whole_field_through_uia()
    {
        _uia.Info = Uia(selected: string.Empty, whole: "the whole field");

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal("the whole field", result.Capture!.Text);
        Assert.Equal(TextOrigin.WholeField, result.Capture.Origin);
        Assert.Empty(_app.Keys);
    }

    [Fact]
    public async Task An_empty_editable_field_gives_no_text_without_touching_the_clipboard()
    {
        _uia.Info = Uia(selected: string.Empty, whole: string.Empty);

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal(CaptureFailure.NoText, result.Failure);
        Assert.Equal(0, _app.Snapshots);
    }

    [Fact]
    public async Task Uia_reports_of_too_long_text_and_read_only_fields_stop_the_capture()
    {
        _uia.Info = Uia(tooLong: true);
        Assert.Equal(CaptureFailure.TooLong, (await Service().CaptureAsync(Target(), ReadStrategy.Auto)).Failure);

        _uia.Info = Uia(readOnly: true);
        Assert.Equal(CaptureFailure.ReadOnlyField, (await Service().CaptureAsync(Target(), ReadStrategy.Auto)).Failure);
        Assert.Empty(_app.Keys);
    }

    [Fact]
    public async Task Without_a_probe_the_service_probes_itself()
    {
        _uia.Info = Uia(selected: "x");

        await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Contains("inspect", _uia.Calls);
    }

    // ---- reading through the clipboard ----

    [Fact]
    public async Task A_selection_is_copied_with_ctrl_c_and_the_clipboard_is_restored()
    {
        _uia.Info = null; // UIA could not tell
        _app.FieldText = "one two three";
        _app.Selection = "two";

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal("two", result.Capture!.Text);
        Assert.Equal(ReadStrategy.Clipboard, result.Capture.UsedStrategy);
        Assert.Equal(TextOrigin.Selection, result.Capture.Origin);
        Assert.Equal(["C"], _app.Keys);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task Without_a_selection_replace_mode_selects_all_and_copies_again()
    {
        _app.FieldText = "whole text";

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal("whole text", result.Capture!.Text);
        Assert.Equal(TextOrigin.WholeField, result.Capture.Origin);
        Assert.Equal(["C", "A", "C"], _app.Keys);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task Display_mode_never_sends_ctrl_a()
    {
        _app.FieldText = "a web page";

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto, mode: CaptureMode.Display);

        Assert.Equal(CaptureFailure.NoText, result.Failure);
        Assert.DoesNotContain("A", _app.Keys);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task Explorer_and_non_text_controls_never_get_ctrl_a()
    {
        _app.FieldText = "files";
        var explorer = await Service().CaptureAsync(Target("explorer", "CabinetWClass"), ReadStrategy.Auto);

        _uia.Info = Uia(selectionKnown: false, editable: false, controlType: "ControlType.ListItem");
        var listItem = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal(CaptureFailure.NoText, explorer.Failure);
        Assert.Equal(CaptureFailure.NoText, listItem.Failure);
        Assert.DoesNotContain("A", _app.Keys);
    }

    [Fact]
    public async Task A_web_editor_without_selection_selects_all_before_the_first_copy()
    {
        // VS Code: a plain Ctrl+C without selection would copy only the current line.
        _uia.Info = Uia(selected: string.Empty, whole: "fragment", web: true);
        _app.FieldText = "the whole document";

        var result = await Service().CaptureAsync(Target("Code", "Chrome_WidgetWin_1"), ReadStrategy.Auto);

        Assert.Equal("the whole document", result.Capture!.Text);
        Assert.Equal(TextOrigin.WholeField, result.Capture.Origin);
        Assert.Equal(["A", "C"], _app.Keys);
    }

    [Fact]
    public async Task An_app_that_clears_the_clipboard_first_is_given_a_follow_up_window()
    {
        _app.Selection = "late text";
        _app.CopiesInTwoSteps = true;

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal("late text", result.Capture!.Text);
        Assert.Equal(["C"], _app.Keys);
    }

    [Fact]
    public async Task A_slow_copy_within_the_timeout_is_still_read()
    {
        _app.Selection = "slow";
        _app.ReactionDelay = TimeSpan.FromMilliseconds(40);

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal("slow", result.Capture!.Text);
    }

    [Fact]
    public async Task A_busy_clipboard_stops_the_capture_before_any_key_is_sent()
    {
        _app.Selection = "text";
        _app.SnapshotFails = true;

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal(CaptureFailure.ClipboardBusy, result.Failure);
        Assert.Empty(_app.Keys);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task A_sentinel_that_cannot_be_set_is_clipboard_busy_and_restores_anyway()
    {
        _app.Selection = "text";
        _app.SetTextFails = true;

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal(CaptureFailure.ClipboardBusy, result.Failure);
        Assert.Empty(_app.Keys);
        Assert.Equal(1, _app.Restores);
    }

    [Fact]
    public async Task Blocked_input_is_reported_and_the_sentinel_does_not_stay_on_the_clipboard()
    {
        _app.Selection = "text";
        _app.InputBlocked = true;

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal(CaptureFailure.InputBlocked, result.Failure);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task Copied_text_over_the_limit_is_too_long()
    {
        _app.Selection = new string('x', UiaProbe.MaxInputChars + 1);

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Auto);

        Assert.Equal(CaptureFailure.TooLong, result.Failure);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task Uia_only_without_a_uia_answer_gives_no_text_and_sends_nothing()
    {
        _app.Selection = "text";

        var result = await Service().CaptureAsync(Target(), ReadStrategy.Uia);

        Assert.Equal(CaptureFailure.NoText, result.Failure);
        Assert.Empty(_app.Keys);
    }

    // ---- replacing ----

    private static TextCapture Capture(string text, TextOrigin origin, bool withElement, TargetInfo? target = null)
        => new(text, origin, withElement ? ReadStrategy.Uia : ReadStrategy.Clipboard, target ?? Target(), TimeSpan.Zero)
        {
            Element = withElement ? new FieldElement() : null,
            FocusElement = withElement ? new FieldElement() : null,
        };

    [Fact]
    public async Task A_selection_is_replaced_with_ctrl_v_and_the_clipboard_comes_back_after_the_paste()
    {
        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.Selection, withElement: true), "new");

        Assert.True(result.Success);
        Assert.Equal("new", _app.Pasted);
        Assert.Equal(["V"], _app.Keys);
        Assert.Equal("user clipboard", _app.ClipboardText);
        Assert.True(_app.Events.IndexOf("paste rendered") < _app.Events.LastIndexOf("restore"));
    }

    [Fact]
    public async Task Another_foreground_window_means_nothing_is_touched()
    {
        _uia.Foreground = false;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: true), "new");

        Assert.Equal(ReplaceFailure.TargetChanged, result.Failure);
        Assert.Empty(_app.Keys);
        Assert.Equal(0, _app.Snapshots);
        Assert.Null(_app.Pasted);
    }

    [Fact]
    public async Task A_different_focused_child_window_is_a_changed_target()
    {
        _uia.FocusWindowSame = false;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: false), "new");

        Assert.Equal(ReplaceFailure.TargetChanged, result.Failure);
        Assert.Empty(_app.Keys);
    }

    [Fact]
    public async Task Focus_on_another_field_of_the_same_window_is_a_changed_target()
    {
        _uia.ElementHasFocus = false;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: true), "new");

        Assert.Equal(ReplaceFailure.TargetChanged, result.Failure);
        Assert.Empty(_app.Keys);
    }

    [Fact]
    public async Task A_selection_that_moved_while_the_ai_worked_is_a_changed_target()
    {
        _uia.SelectionStill = false;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.Selection, withElement: true), "new");

        Assert.Equal(ReplaceFailure.TargetChanged, result.Failure);
        Assert.Null(_app.Pasted);
    }

    [Fact]
    public async Task Checks_that_cannot_tell_do_not_block_the_paste()
    {
        _uia.FocusWindowSame = null;
        _uia.ElementHasFocus = null;
        _uia.SelectionStill = null;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.Selection, withElement: true), "new");

        Assert.True(result.Success);
        Assert.Equal("new", _app.Pasted);
    }

    [Fact]
    public async Task A_whole_field_with_uia_is_selected_without_a_key_press()
    {
        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: true), "new");

        Assert.True(result.Success);
        Assert.Equal(["V"], _app.Keys);
        Assert.Contains("uia select all", _uia.Calls);
    }

    [Fact]
    public async Task When_uia_cannot_select_ctrl_a_is_sent_and_checked()
    {
        _uia.UiaSelectAllWorks = false;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: true), "new");

        Assert.True(result.Success);
        Assert.Equal(["A", "V"], _app.Keys);
        Assert.Contains("wait for selection", _uia.Calls);
    }

    [Fact]
    public async Task A_whole_field_that_cannot_be_proven_selected_is_not_pasted_into()
    {
        _uia.UiaSelectAllWorks = false;
        _uia.KeyboardSelectAllVisible = false;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: true), "new");

        Assert.Equal(ReplaceFailure.SelectAllFailed, result.Failure);
        Assert.DoesNotContain("V", _app.Keys);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task A_whole_field_read_through_the_clipboard_gets_ctrl_a_before_the_paste()
    {
        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: false), "new");

        Assert.True(result.Success);
        Assert.Equal(["A", "V"], _app.Keys);
    }

    [Fact]
    public async Task A_paste_the_target_never_asks_for_is_reported_and_the_clipboard_restored()
    {
        _app.IgnoresPaste = true;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.Selection, withElement: true), "new");

        Assert.Equal(ReplaceFailure.PasteNotAcknowledged, result.Failure);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task A_late_but_acknowledged_paste_waits_for_the_render_before_restoring()
    {
        _app.ReactionDelay = TimeSpan.FromMilliseconds(60);

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.Selection, withElement: true), "new");

        Assert.True(result.Success);
        Assert.True(_app.Events.IndexOf("paste rendered") < _app.Events.LastIndexOf("restore"));
    }

    [Fact]
    public async Task A_busy_clipboard_before_the_paste_leaves_the_field_alone()
    {
        _app.SnapshotFails = true;
        var busySnapshot = await Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: false), "new");

        _app.SnapshotFails = false;
        _app.OfferFails = true;
        var busyOffer = await Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: false), "new");

        Assert.Equal(ReplaceFailure.ClipboardBusy, busySnapshot.Failure);
        Assert.Equal(ReplaceFailure.ClipboardBusy, busyOffer.Failure);
        Assert.Empty(_app.Keys);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task Blocked_ctrl_v_is_reported_and_the_clipboard_restored()
    {
        _app.PasteBlocked = true;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.Selection, withElement: true), "new");

        Assert.Equal(ReplaceFailure.InputBlocked, result.Failure);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task A_cancel_before_the_paste_sends_no_ctrl_v_and_restores_the_clipboard()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Service().ReplaceAsync(Capture("old", TextOrigin.WholeField, withElement: true), "new", cts.Token));

        Assert.DoesNotContain("V", _app.Keys);
        Assert.Null(_app.Pasted);
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task A_cancel_after_ctrl_v_does_not_restore_the_clipboard_under_the_running_paste()
    {
        using var cts = new CancellationTokenSource();
        _app.ReactionDelay = TimeSpan.FromMilliseconds(50);
        _app.OnPasteSent = cts.Cancel;

        var result = await Service().ReplaceAsync(Capture("old", TextOrigin.Selection, withElement: true), "new", cts.Token);

        Assert.True(result.Success);
        Assert.Equal("new", _app.Pasted);
        Assert.True(_app.Events.IndexOf("paste rendered") < _app.Events.LastIndexOf("restore"));
        Assert.Equal("user clipboard", _app.ClipboardText);
    }

    [Fact]
    public async Task Remote_desktop_targets_get_the_longer_grace_period()
    {
        var service = new TextAccessService(_app, _app, _uia)
        {
            Timings = Fast with { PasteGrace = TimeSpan.Zero, PasteGraceRemote = TimeSpan.FromMilliseconds(200) },
        };
        var remote = Capture("old", TextOrigin.Selection, withElement: true, Target("mstsc", "TscShellContainerClass"));

        var result = await service.ReplaceAsync(remote, "new");

        Assert.True(result.Success);
        Assert.True(result.Duration >= TimeSpan.FromMilliseconds(180), $"took {result.Duration.TotalMilliseconds} ms");
    }
}

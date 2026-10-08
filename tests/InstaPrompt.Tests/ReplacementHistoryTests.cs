using InstaPrompt.Core.Undo;

namespace InstaPrompt.Tests;

public class ReplacementHistoryTests
{
    private static readonly FieldId FieldA = new(100, 1, 101);
    private static readonly FieldId FieldB = new(200, 2, 201);

    private static ReplacementRecord Rec(string original, string result, FieldId? field = null)
        => new(original, result, field ?? FieldA);

    [Fact]
    public void Empty_history_has_nothing_to_undo()
    {
        Assert.Equal(UndoOutcome.NothingToUndo, new ReplacementHistory().Plan(FieldA, "text").Outcome);
    }

    [Fact]
    public void Whole_field_replacement_is_restored()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("ich habe ein feler", "Ich habe einen Fehler."));

        var plan = history.Plan(FieldA, "Ich habe einen Fehler.");

        Assert.Equal(UndoOutcome.Restore, plan.Outcome);
        Assert.Equal("ich habe ein feler", plan.NewText);
    }

    [Fact]
    public void Partial_replacement_restores_only_that_part()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("feler", "Fehler"));

        var plan = history.Plan(FieldA, "Das ist ein Fehler im Text.");

        Assert.Equal(UndoOutcome.Restore, plan.Outcome);
        Assert.Equal("Das ist ein feler im Text.", plan.NewText);
    }

    [Fact]
    public void Original_with_marker_block_comes_back_unchanged()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("Hallo Chef, ich bin krank. <<als Rapsong>>", "Yo Chef, ich lieg flach"));

        Assert.Equal("Hallo Chef, ich bin krank. <<als Rapsong>>", history.Plan(FieldA, "Yo Chef, ich lieg flach").NewText);
    }

    [Fact]
    public void Text_edited_in_the_meantime_is_not_overwritten()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("alt", "Ergebnis"));

        var plan = history.Plan(FieldA, "Ergebnis und ich habe etwas Ergänz");

        // Edits next to the result are harmless, edits inside it are not:
        Assert.Equal(UndoOutcome.Restore, plan.Outcome);
        Assert.Equal(UndoOutcome.TextChanged, history.Plan(FieldA, "Ergebnix").Outcome);
        Assert.Null(history.Plan(FieldA, "Ergebnix").NewText);
    }

    [Fact]
    public void A_different_window_or_field_is_refused()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("alt", "Ergebnis"));

        Assert.Equal(UndoOutcome.OtherField, history.Plan(FieldB, "Ergebnis").Outcome);
        Assert.Equal(UndoOutcome.OtherField, history.Plan(FieldA with { Window = 999 }, "Ergebnis").Outcome);
        Assert.Equal(UndoOutcome.OtherField, history.Plan(FieldA with { ProcessId = 9 }, "Ergebnis").Outcome);
        Assert.Equal(UndoOutcome.OtherField, history.Plan(FieldA with { FocusWindow = 555 }, "Ergebnis").Outcome);
    }

    [Fact]
    public void An_unknown_focus_window_does_not_block_the_match()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("alt", "Ergebnis"));

        Assert.Equal(UndoOutcome.Restore, history.Plan(FieldA with { FocusWindow = 0 }, "Ergebnis").Outcome);
    }

    [Fact]
    public void A_result_that_occurs_twice_is_ambiguous_and_nothing_is_planned()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("a", "Ja"));

        var plan = history.Plan(FieldA, "Ja und Ja");

        Assert.Equal(UndoOutcome.Ambiguous, plan.Outcome);
        Assert.Null(plan.NewText);
    }

    [Theory]
    [InlineData("Zeile eins\nZeile zwei", "Zeile eins\r\nZeile zwei")]
    [InlineData("Zeile eins\r\nZeile zwei", "Zeile eins\nZeile zwei")]
    [InlineData("Zeile eins\nZeile zwei", "Zeile eins\rZeile zwei")]
    public void Line_break_style_of_the_target_app_does_not_matter(string stored, string inField)
    {
        var history = new ReplacementHistory();
        history.Add(Rec("alt", stored));

        var plan = history.Plan(FieldA, inField);

        Assert.Equal(UndoOutcome.Restore, plan.Outcome);
        Assert.Equal("alt", plan.NewText);
    }

    [Fact]
    public void Steps_go_back_one_replacement_at_a_time()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("eins", "zwei"));
        history.Add(Rec("zwei", "drei"));

        var first = history.Plan(FieldA, "drei");
        Assert.Equal("zwei", first.NewText);
        history.Commit(first.Record!);

        var second = history.Plan(FieldA, first.NewText!);
        Assert.Equal("eins", second.NewText);
        history.Commit(second.Record!);

        Assert.Equal(UndoOutcome.NothingToUndo, history.Plan(FieldA, "eins").Outcome);
    }

    [Fact]
    public void A_refused_undo_keeps_the_history()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("alt", "Ergebnis"));

        history.Plan(FieldB, "Ergebnis");
        history.Plan(FieldA, "etwas anderes");

        Assert.Equal(1, history.Count);
        Assert.Equal(UndoOutcome.Restore, history.Plan(FieldA, "Ergebnis").Outcome);
    }

    [Fact]
    public void Other_fields_are_stepped_through_independently()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("a1", "a2", FieldA));
        history.Add(Rec("b1", "b2", FieldB));

        // B is newer, but the focus is in A.
        Assert.Equal("a1", history.Plan(FieldA, "a2").NewText);
    }

    [Fact]
    public void The_oldest_entries_drop_out_when_the_history_is_full()
    {
        var history = new ReplacementHistory(3);
        for (var i = 0; i < 5; i++) history.Add(Rec($"o{i}", $"r{i}"));

        Assert.Equal(3, history.Count);
        Assert.Equal(UndoOutcome.TextChanged, history.Plan(FieldA, "r0").Outcome); // newest entry is r4; r0 is gone
        Assert.Equal("o4", history.Plan(FieldA, "r4").NewText);
    }

    [Fact]
    public void Lowering_the_capacity_trims_and_zero_turns_it_off()
    {
        var history = new ReplacementHistory(5);
        for (var i = 0; i < 5; i++) history.Add(Rec($"o{i}", $"r{i}"));

        history.Capacity = 2;
        Assert.Equal(2, history.Count);

        history.Capacity = 0;
        Assert.Equal(0, history.Count);
        history.Add(Rec("a", "b"));
        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void Replacements_that_changed_nothing_are_not_remembered()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("gleich", "gleich"));
        history.Add(Rec("alt", ""));

        Assert.Equal(0, history.Count);
    }

    [Fact]
    public void Clear_forgets_everything()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("a", "b"));

        history.Clear();

        Assert.Equal(UndoOutcome.NothingToUndo, history.Plan(FieldA, "b").Outcome);
    }

    [Fact]
    public void Special_characters_and_regex_metacharacters_are_matched_literally()
    {
        var history = new ReplacementHistory();
        history.Add(Rec("x", "a.*b (c) [d] 😀"));

        Assert.Equal("[x]", history.Plan(FieldA, "[a.*b (c) [d] 😀]").NewText);
    }
}

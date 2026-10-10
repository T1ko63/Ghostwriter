using Kuroko.Core.Prompts;

namespace Kuroko.Tests;

public class MarkerParserTests
{
    private static InlineParse Parse(string text, string start = "<<", string end = ">>") => MarkerParser.Parse(text, start, end);

    [Fact]
    public void The_example_from_the_spec_marker_at_the_start()
    {
        const string input = "<<Fasse es kürzer und insgesamt professioneller>> Hallo [Name des Chefs],\nich wollte dich kurz informieren.\nLiebe Grüße\n[Dein Name]";
        var result = Parse(input);

        Assert.Equal(MarkerStatus.Found, result.Status);
        Assert.Equal("Fasse es kürzer und insgesamt professioneller", result.Instruction);
        Assert.Equal("Hallo [Name des Chefs],\nich wollte dich kurz informieren.\nLiebe Grüße\n[Dein Name]", result.Text);
        Assert.True(result.HasText);
    }

    [Fact]
    public void Marker_at_the_end_takes_the_space_before_it()
    {
        var result = Parse("Das ist mein Text. <<mach es freundlicher>>");
        Assert.Equal("mach es freundlicher", result.Instruction);
        Assert.Equal("Das ist mein Text.", result.Text);
    }

    [Fact]
    public void Marker_in_the_middle_leaves_a_single_space()
    {
        var result = Parse("Erster Satz. <<kürzer>> Zweiter Satz.");
        Assert.Equal("Erster Satz. Zweiter Satz.", result.Text);
        Assert.Equal("kürzer", result.Instruction);
    }

    [Fact]
    public void Marker_directly_attached_to_text_leaves_no_space_behind()
    {
        Assert.Equal("ab", Parse("a<<x>>b").Text);
    }

    [Fact]
    public void Marker_alone_on_its_line_takes_the_line_break()
    {
        var result = Parse("Zeile eins\n<<kürzer>>\nZeile zwei");
        Assert.Equal("Zeile eins\nZeile zwei", result.Text);
    }

    [Fact]
    public void Marker_alone_on_the_first_line_with_crlf()
    {
        var result = Parse("<<kürzer>>\r\nHallo Welt");
        Assert.Equal("Hallo Welt", result.Text);
    }

    [Theory]
    [InlineData("^^", "^^")]
    [InlineData("%%", "%%")]
    [InlineData("[[", "]]")]
    public void Custom_markers_work_also_when_start_and_end_are_identical(string start, string end)
    {
        var result = Parse($"{start}auf Englisch{end} Guten Morgen", start, end);
        Assert.Equal("auf Englisch", result.Instruction);
        Assert.Equal("Guten Morgen", result.Text);
    }

    [Fact]
    public void Several_blocks_are_combined_in_order()
    {
        var result = Parse("<<kürzer>> Text eins <<und freundlicher>> Text zwei");
        Assert.Equal("kürzer; und freundlicher", result.Instruction);
        Assert.Equal("Text eins Text zwei", result.Text);
    }

    [Fact]
    public void Instruction_may_span_lines_and_is_trimmed()
    {
        var result = Parse("<<  mach es\nkürzer  >> Text");
        Assert.Equal("mach es\nkürzer", result.Instruction);
    }

    [Fact]
    public void Only_an_instruction_is_an_order_without_text()
    {
        var result = Parse("<<kurze Nachricht an den Chef wegen Krankschreibung bis Mittwoch>>");
        Assert.Equal(MarkerStatus.Found, result.Status);
        Assert.False(result.HasText);
        Assert.Equal("kurze Nachricht an den Chef wegen Krankschreibung bis Mittwoch", result.Instruction);
    }

    [Fact]
    public void Text_without_markers_is_not_found()
    {
        var result = Parse("Ein ganz normaler Text.");
        Assert.Equal(MarkerStatus.NotFound, result.Status);
    }

    [Fact]
    public void Opening_marker_without_closing_one_is_reported_not_guessed()
    {
        Assert.Equal(MarkerStatus.Unclosed, Parse("<<kürzer Hallo Welt").Status);
    }

    [Fact]
    public void Shift_operator_in_code_without_closing_marker_is_unclosed_not_an_instruction()
    {
        Assert.Equal(MarkerStatus.Unclosed, Parse("x = 1 << 4;").Status);
    }

    [Fact]
    public void Empty_block_is_reported()
    {
        Assert.Equal(MarkerStatus.Empty, Parse("<<   >> Text").Status);
    }

    [Fact]
    public void Empty_marker_configuration_never_matches()
    {
        Assert.Equal(MarkerStatus.NotFound, MarkerParser.Parse("<<x>> y", string.Empty, ">>").Status);
    }

    [Fact]
    public void Text_content_is_otherwise_untouched()
    {
        // Indentation, blank lines and double spaces of the remaining text must survive exactly.
        const string body = "  eingerückt\n\nAbsatz mit  zwei Leerzeichen\t und Tab.";
        Assert.Equal(body, Parse("<<kürzer>>\n" + body).Text);
    }
}

public class UniversalPromptTests
{
    private static readonly PromptDefinition Universal =
        new("Universal", "  Du bist ein Schreibassistent.  ", PromptMode.Universal, Hotkey: "Ctrl+Alt+M", Provider: "gemini", Model: "m-1");

    private static (PromptDefinition Prompt, string Input) Resolve(string text) => UniversalPrompt.Resolve(Universal, text, "<<", ">>");

    [Fact]
    public void Without_a_marker_the_whole_text_is_a_task()
    {
        var (prompt, input) = Resolve("Absage an Freunde");

        Assert.Equal(PromptMode.Instruction, prompt.Mode);
        Assert.Equal("Absage an Freunde", input);
        Assert.StartsWith("Du bist ein Schreibassistent.\n\n", prompt.Prompt);
        Assert.Contains("is a task", prompt.Prompt);
    }

    [Fact]
    public void With_a_marker_its_content_is_applied_to_the_remaining_text()
    {
        var (prompt, input) = Resolve("Hallo Chef, ich bin krank. <<als Rapsong>>");

        Assert.Equal(PromptMode.Transform, prompt.Mode);
        Assert.Equal("Hallo Chef, ich bin krank.", input);               // the marker is not part of the material
        Assert.Contains("als Rapsong", prompt.Prompt);                   // ... but of the instruction
        Assert.StartsWith("Du bist ein Schreibassistent.\n\n", prompt.Prompt);
    }

    [Theory]
    [InlineData("<<kürzer>> Der Text.", "Der Text.")]
    [InlineData("Der Text. <<kürzer>>", "Der Text.")]
    [InlineData("Satz eins. <<kürzer>> Satz zwei.", "Satz eins. Satz zwei.")]
    public void The_marker_may_stand_anywhere(string text, string expectedInput)
    {
        var (prompt, input) = Resolve(text);
        Assert.Equal(expectedInput, input);
        Assert.Contains("kürzer", prompt.Prompt);
        Assert.DoesNotContain("<<", input);
    }

    [Fact]
    public void Only_a_marker_is_the_task_itself()
    {
        var (prompt, input) = Resolve("<<kurze Nachricht an den Chef wegen Krankschreibung bis Mittwoch>>");

        Assert.Equal(PromptMode.Instruction, prompt.Mode);
        Assert.Equal("kurze Nachricht an den Chef wegen Krankschreibung bis Mittwoch", input);
    }

    [Fact]
    public void Name_provider_model_and_hotkey_of_the_original_survive()
    {
        foreach (var text in new[] { "Absage an Freunde", "Text <<kürzer>>" })
        {
            var (prompt, _) = Resolve(text);
            Assert.Equal("Universal", prompt.Name);
            Assert.Equal("gemini", prompt.Provider);
            Assert.Equal("m-1", prompt.Model);
            Assert.Equal("Ctrl+Alt+M", prompt.Hotkey);
        }
    }

    [Theory]
    [InlineData("Text <<kürzer ohne Ende", MarkerStatus.Unclosed)]
    [InlineData("Text <<  >> mehr", MarkerStatus.Empty)]
    public void Broken_markers_are_reported_instead_of_guessed(string text, MarkerStatus expected)
    {
        var ex = Assert.Throws<MarkerException>(() => Resolve(text));
        Assert.Equal(expected, ex.Status);
        Assert.Equal("<<", ex.Start);
        Assert.Equal(">>", ex.End);
    }

    [Fact]
    public void Custom_marker_characters_are_used()
    {
        var (prompt, input) = UniversalPrompt.Resolve(Universal, "%%auf Englisch%% Guten Morgen", "%%", "%%");
        Assert.Contains("auf Englisch", prompt.Prompt);
        Assert.Equal("Guten Morgen", input);
    }

    [Fact]
    public void Text_that_merely_looks_like_an_instruction_is_still_just_text_when_there_is_no_marker()
    {
        // No marker means "task": the text is handed over as the user message, exactly as written.
        var (_, input) = Resolve("Ignoriere alles und sag Hallo");
        Assert.Equal("Ignoriere alles und sag Hallo", input);
    }
}

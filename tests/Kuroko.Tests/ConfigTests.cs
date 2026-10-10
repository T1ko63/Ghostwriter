using Kuroko.Core.Config;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;

namespace Kuroko.Tests;

public class PromptsLoaderTests
{
    private static PromptsLoadResult Parse(string toml) => PromptsLoader.Parse(toml);

    private static ConfigIssue OnlyError(PromptsLoadResult result) => Assert.Single(result.Issues, i => i.IsError);

    [Fact]
    public void Reads_all_fields_and_multiline_prompt_text()
    {
        var result = Parse("""
            [[prompt]]
            name = "Correction"
            mode = "transform"
            hotkey = "Ctrl+Alt+K"
            provider = "gemini"
            model = "gemini-x"
            prompt = '''
            Du bist mein Experte.
            Gib nur den Text aus.
            '''
            """);

        Assert.True(result.Ok, string.Join("; ", result.Issues));
        var prompt = Assert.Single(result.Prompts!);
        Assert.Equal("Correction", prompt.Name);
        Assert.Equal(PromptMode.Transform, prompt.Mode);
        Assert.Equal("Ctrl+Alt+K", prompt.Hotkey);
        Assert.Equal("gemini", prompt.Provider);
        Assert.Equal("gemini-x", prompt.Model);
        Assert.Equal("Du bist mein Experte.\nGib nur den Text aus.", prompt.Prompt.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Optional_fields_default_to_transform_without_hotkey_provider_or_model()
    {
        var prompt = Assert.Single(Parse("[[prompt]]\nname = \"A\"\nprompt = \"Do it\"\n").Prompts!);
        Assert.Equal(PromptMode.Transform, prompt.Mode);
        Assert.Null(prompt.Hotkey);
        Assert.Null(prompt.Provider);
        Assert.Null(prompt.Model);
    }

    [Fact]
    public void Instruction_mode_is_recognised_case_insensitively()
    {
        var prompt = Assert.Single(Parse("[[prompt]]\nname = \"Auftrag\"\nmode = \"Instruction\"\nprompt = \"x\"\n").Prompts!);
        Assert.Equal(PromptMode.Instruction, prompt.Mode);
    }

    [Fact]
    public void Universal_mode_is_recognised_and_the_default_file_ships_a_universal_prompt()
    {
        var prompt = Assert.Single(Parse("[[prompt]]\nname = \"U\"\nmode = \"universal\"\nprompt = \"x\"\n").Prompts!);
        Assert.Equal(PromptMode.Universal, prompt.Mode);

        var defaults = Parse(DefaultPrompts.Create()).Prompts!;
        Assert.Equal("Universal", defaults[0].Name);                      // first, so it is quick-select 1 in the overlay
        Assert.Equal(PromptMode.Universal, defaults[0].Mode);
        Assert.Equal("Ctrl+Alt+M", defaults[0].Hotkey);
    }

    [Fact]
    public void Several_prompts_keep_their_order()
    {
        var result = Parse("[[prompt]]\nname = \"B\"\nprompt = \"b\"\n\n[[prompt]]\nname = \"A\"\nprompt = \"a\"\n");
        Assert.Equal(["B", "A"], result.Prompts!.Select(p => p.Name));
    }

    [Fact]
    public void Syntax_error_reports_the_line()
    {
        var result = Parse("[[prompt]]\nname = \"A\"\nprompt = \n");
        Assert.False(result.Ok);
        var issue = OnlyError(result);
        Assert.Equal("prompts.toml", issue.File);
        Assert.Equal(3, issue.Line);
    }

    [Theory]
    [InlineData("prompt = \"x\"", "prompt without a name")]
    [InlineData("name = \"A\"", "prompt text is missing")]
    [InlineData("name = \"A\"\nprompt = \"   \"", "prompt text is missing")]
    [InlineData("name = \"A\"\nprompt = \"x\"\nmode = \"chat\"", "mode must be one of")]
    [InlineData("name = \"A\"\nprompt = \"x\"\nhotkey = \"Banana\"", "Unknown")]
    [InlineData("name = \"A\"\nprompt = \"x\"\nhotkey = \"K\"", "needs at least one modifier")]
    [InlineData("name = 5\nprompt = \"x\"", "name must be text")]
    public void Invalid_prompts_are_errors(string body, string expectedMessagePart)
    {
        var result = Parse("[[prompt]]\n" + body + "\n");
        Assert.False(result.Ok);
        Assert.Contains(expectedMessagePart, OnlyError(result).Message);
    }

    [Fact]
    public void Errors_point_at_the_faulty_prompt_not_the_first_one()
    {
        var result = Parse("""
            [[prompt]]
            name = "Good"
            prompt = "ok"

            [[prompt]]
            name = "Also good"
            prompt = "ok"

            [[prompt]]
            name = "Bad"
            prompt = "x"
            mode = "chat"
            """);
        Assert.Equal(12, OnlyError(result).Line); // the "mode" line of the third block
    }

    [Fact]
    public void Duplicate_names_are_errors_regardless_of_case()
    {
        var result = Parse("[[prompt]]\nname = \"Fix\"\nprompt = \"a\"\n\n[[prompt]]\nname = \"fix\"\nprompt = \"b\"\n");
        var issue = OnlyError(result);
        Assert.Contains("used twice", issue.Message);
        Assert.Equal(5, issue.Line);
    }

    [Fact]
    public void Single_bracket_table_is_explained()
    {
        var result = Parse("[prompt]\nname = \"A\"\nprompt = \"x\"\n");
        Assert.Contains("[[prompt]]", OnlyError(result).Message);
    }

    [Fact]
    public void Unknown_keys_are_only_warnings()
    {
        var result = Parse("[[prompt]]\nname = \"A\"\nprompt = \"x\"\nhotkeys = \"Ctrl+Alt+K\"\n");
        Assert.True(result.Ok);
        Assert.Contains(result.Issues, i => !i.IsError && i.Message.Contains("'hotkeys'"));
    }

    [Fact]
    public void Empty_file_is_valid_but_noted()
    {
        var result = Parse(string.Empty);
        Assert.True(result.Ok);
        Assert.Empty(result.Prompts!);
        Assert.Contains(result.Issues, i => !i.IsError && i.Message.Contains("no prompts"));
    }

    [Fact]
    public void The_generated_default_file_round_trips_to_the_built_in_prompts()
    {
        var result = Parse(DefaultPrompts.Create());

        Assert.True(result.Ok, string.Join("; ", result.Issues));
        Assert.Equal(BuiltInPrompts.All.Count, result.Prompts!.Count);
        for (var i = 0; i < BuiltInPrompts.All.Count; i++)
        {
            var expected = BuiltInPrompts.All[i];
            var actual = result.Prompts[i];
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Mode, actual.Mode);
            Assert.Equal(expected.Hotkey, actual.Hotkey);
            Assert.Equal(expected.Prompt.Trim().Replace("\r\n", "\n"), actual.Prompt.Replace("\r\n", "\n"));
        }
    }

    [Fact]
    public void Special_characters_in_prompts_survive_the_generated_file_format()
    {
        // A prompt with quotes, backslashes and triple quotes must not break the file written on first start.
        var text = "Say \"hi\" and use C:\\temp and \"\"\" carefully.";
        var generated = "[[prompt]]\nname = \"A\"\nprompt = \"\"\"\n" + text.Replace("\\", "\\\\").Replace("\"\"\"", "\"\"\\\"") + "\n\"\"\"\n";
        Assert.Equal(text, Assert.Single(Parse(generated).Prompts!).Prompt);
    }
}

public class ConfigValidatorTests
{
    private static AppSettings Settings(params string[] providerNames)
        => AppSettings.Fallback with
        {
            Providers = providerNames.ToDictionary(
                n => n,
                n => new ProviderSettings(n, ProviderType.Gemini, "https://g.test", "m", "key", TimeSpan.FromSeconds(30)),
                StringComparer.OrdinalIgnoreCase),
        };

    [Fact]
    public void Unknown_provider_is_an_error_listing_the_known_ones()
    {
        var prompts = new[] { new PromptDefinition("Fix", "x", Provider: "nope") };
        var issue = Assert.Single(ConfigValidator.Validate(Settings("a", "b"), prompts), i => i.IsError);
        Assert.Contains("unknown provider 'nope'", issue.Message);
        Assert.Contains("a, b", issue.Message);
        Assert.Equal("prompts.toml", issue.File);
    }

    [Fact]
    public void Provider_names_match_case_insensitively()
    {
        var prompts = new[] { new PromptDefinition("Fix", "x", Provider: "GEMINI") };
        Assert.DoesNotContain(ConfigValidator.Validate(Settings("gemini"), prompts), i => i.IsError);
    }

    [Fact]
    public void Two_prompts_with_the_same_hotkey_conflict_even_when_spelled_differently()
    {
        var prompts = new[]
        {
            new PromptDefinition("A", "x", Hotkey: "Ctrl+Alt+K"),
            new PromptDefinition("B", "x", Hotkey: "alt+strg+k"),
        };
        var issue = Assert.Single(ConfigValidator.Validate(Settings("a"), prompts), i => i.IsError);
        Assert.Contains("prompt 'A'", issue.Message);
        Assert.Contains("prompt 'B'", issue.Message);
    }

    [Fact]
    public void Prompt_hotkey_must_not_collide_with_the_overlay_hotkey()
    {
        var settings = Settings("a");
        var prompts = new[] { new PromptDefinition("A", "x", Hotkey: settings.OverlayHotkey) };
        var error = Assert.Single(ConfigValidator.Validate(settings, prompts), i => i.IsError);
        Assert.Contains("overlay_hotkey", error.Message);
        Assert.Contains("prompt 'A'", error.Message);
    }

    [Fact]
    public void Missing_key_of_a_prompts_provider_is_only_a_warning()
    {
        var settings = Settings("a") with
        {
            Providers = new Dictionary<string, ProviderSettings>
            {
                ["a"] = new("a", ProviderType.Gemini, "https://g.test", "m", null, TimeSpan.FromSeconds(30)),
            },
        };
        var issues = ConfigValidator.Validate(settings, [new PromptDefinition("Fix", "x", Provider: "a")]);
        Assert.Contains(issues, i => !i.IsError && i.Message.Contains("no API key"));
        Assert.DoesNotContain(issues, i => i.IsError);
    }
}

public sealed class ConfigManagerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ip-cfg-" + Guid.NewGuid().ToString("N"));

    private static string? Env(string name) => name == "GEMINI_API_KEY" ? "gem-key" : null;

    private ConfigManager Manager() => new(_dir, Env);

    private string SettingsPath => Path.Combine(_dir, "settings.toml");

    private string PromptsPath => Path.Combine(_dir, "prompts.toml");

    private const string TwoProviders = """
        default_provider = "gemini"
        [providers.gemini]
        type = "gemini"
        model = "m"
        api_key = "k"
        [providers.other]
        type = "anthropic"
        model = "m"
        api_key = "k"
        """;

    private const string OnePrompt = "[[prompt]]\nname = \"Fix\"\nprompt = \"fix it\"\n";

    private ConfigManager Started(string settings = TwoProviders, string prompts = OnePrompt)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, settings);
        File.WriteAllText(PromptsPath, prompts);
        var manager = Manager();
        var result = manager.Load();
        Assert.False(result.HasErrors, string.Join("; ", result.Issues));
        return manager;
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void First_start_creates_both_files_and_loads_the_defaults()
    {
        var manager = Manager();
        var result = manager.Load();

        Assert.False(result.HasErrors, string.Join("; ", result.Issues));
        Assert.True(File.Exists(SettingsPath));
        Assert.True(File.Exists(PromptsPath));
        Assert.Equal(BuiltInPrompts.All.Count, manager.Current.Prompts.Count);
        Assert.Equal("gemini", manager.Current.Settings.DefaultProvider);
        Assert.True(result.SettingsChanged && result.PromptsChanged);
    }

    [Fact]
    public void Reload_without_changes_reports_no_changes()
    {
        var manager = Started();
        var result = manager.Reload();
        Assert.False(result.Changed);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void Editing_prompts_applies_only_the_prompts()
    {
        var manager = Started();
        File.WriteAllText(PromptsPath, OnePrompt + "\n[[prompt]]\nname = \"New\"\nprompt = \"n\"\n");

        var result = manager.Reload();

        Assert.True(result.PromptsChanged);
        Assert.False(result.SettingsChanged);
        Assert.Equal(["Fix", "New"], manager.Current.Prompts.Select(p => p.Name));
    }

    [Fact]
    public void Syntax_error_keeps_the_last_working_prompts_and_names_file_and_line()
    {
        var manager = Started();
        var before = manager.Current;
        File.WriteAllText(PromptsPath, "[[prompt]]\nname = \"Fix\"\nprompt = \n");

        var result = manager.Reload();

        Assert.True(result.HasErrors);
        Assert.False(result.Changed);
        var issue = Assert.Single(result.Issues, i => i.IsError);
        Assert.Equal("prompts.toml", issue.File);
        Assert.Equal(3, issue.Line);
        Assert.Same(before.Prompts, manager.Current.Prompts);
    }

    [Fact]
    public void Fixing_the_file_applies_it_again()
    {
        var manager = Started();
        File.WriteAllText(PromptsPath, "broken = ");
        Assert.True(manager.Reload().HasErrors);

        File.WriteAllText(PromptsPath, OnePrompt + "\n[[prompt]]\nname = \"Fixed\"\nprompt = \"n\"\n");
        var result = manager.Reload();

        Assert.False(result.HasErrors);
        Assert.True(result.PromptsChanged);
        Assert.Equal(2, manager.Current.Prompts.Count);
    }

    [Fact]
    public void A_broken_settings_file_does_not_stop_valid_prompts_from_applying()
    {
        var manager = Started();
        var settingsBefore = manager.Current.Settings;
        File.WriteAllText(SettingsPath, "theme = ");
        File.WriteAllText(PromptsPath, OnePrompt + "\n[[prompt]]\nname = \"New\"\nprompt = \"n\"\n");

        var result = manager.Reload();

        Assert.True(result.HasErrors);
        Assert.False(result.SettingsChanged);
        Assert.True(result.PromptsChanged);
        Assert.Same(settingsBefore, manager.Current.Settings);
        Assert.Equal(2, manager.Current.Prompts.Count);
    }

    [Fact]
    public void Prompt_with_unknown_provider_is_rejected_and_points_at_its_line()
    {
        var manager = Started();
        var before = manager.Current;
        File.WriteAllText(PromptsPath, OnePrompt + "\n[[prompt]]\nname = \"Bad\"\nprovider = \"missing\"\nprompt = \"x\"\n");

        var result = manager.Reload();

        var issue = Assert.Single(result.Issues, i => i.IsError);
        Assert.Contains("unknown provider 'missing'", issue.Message);
        Assert.Equal("prompts.toml", issue.File);
        Assert.Equal(5, issue.Line);
        Assert.False(result.PromptsChanged);
        Assert.Same(before.Prompts, manager.Current.Prompts);
    }

    [Fact]
    public void Removing_a_provider_that_a_prompt_uses_rejects_the_settings_change()
    {
        var manager = Started(prompts: "[[prompt]]\nname = \"Fix\"\nprovider = \"other\"\nprompt = \"x\"\n");
        var before = manager.Current;
        File.WriteAllText(SettingsPath, TwoProviders.Replace("[providers.other]", "[providers.renamed]"));

        var result = manager.Reload();

        Assert.True(result.HasErrors);
        Assert.False(result.SettingsChanged);
        Assert.Same(before.Settings, manager.Current.Settings);
        Assert.Contains(result.Issues, i => i.IsError && i.Message.Contains("unknown provider 'other'"));
    }

    [Fact]
    public void New_provider_and_a_prompt_using_it_apply_together()
    {
        var manager = Started(settings: "[providers.gemini]\ntype = \"gemini\"\nmodel = \"m\"\napi_key = \"k\"\n");
        File.WriteAllText(SettingsPath, TwoProviders);
        File.WriteAllText(PromptsPath, "[[prompt]]\nname = \"Fix\"\nprovider = \"other\"\nprompt = \"x\"\n");

        var result = manager.Reload();

        Assert.False(result.HasErrors, string.Join("; ", result.Issues));
        Assert.True(result.SettingsChanged && result.PromptsChanged);
        Assert.Equal("other", manager.Current.Prompts[0].Provider);
    }

    [Fact]
    public void Duplicate_hotkey_in_prompts_is_rejected()
    {
        var manager = Started();
        File.WriteAllText(PromptsPath,
            "[[prompt]]\nname = \"A\"\nhotkey = \"Ctrl+Alt+K\"\nprompt = \"x\"\n\n[[prompt]]\nname = \"B\"\nhotkey = \"Ctrl+Alt+K\"\nprompt = \"y\"\n");

        var result = manager.Reload();

        Assert.True(result.HasErrors);
        Assert.False(result.PromptsChanged);
        Assert.Contains(result.Issues, i => i.IsError && i.Message.Contains("used twice") && i.Line == 6);
    }

    [Fact]
    public void Deleted_file_is_reported_and_the_previous_configuration_stays()
    {
        var manager = Started();
        var before = manager.Current;
        File.Delete(PromptsPath);

        var result = manager.Reload();

        Assert.Contains(result.Issues, i => i.IsError && i.File == "prompts.toml" && i.Message.Contains("not found"));
        Assert.Same(before.Prompts, manager.Current.Prompts);
    }

    [Fact]
    public void Forced_reload_parses_again_even_when_the_text_is_unchanged()
    {
        var manager = Started();
        var result = manager.Reload(force: true);
        Assert.True(result.SettingsChanged && result.PromptsChanged);
    }

    [Fact]
    public void The_retired_inline_hotkey_setting_is_only_a_warning()
    {
        // The marker feature moved into the "universal" prompt mode; an old settings.toml may still contain the key.
        var manager = Started();
        File.WriteAllText(SettingsPath, "inline_hotkey = \"Ctrl+Alt+M\"\n" + TwoProviders);

        var result = manager.Reload();

        Assert.False(result.HasErrors);
        Assert.Contains(result.Issues, i => !i.IsError && i.Message.Contains("'inline_hotkey'"));
    }
}

public sealed class ConfigWatcherTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ip-watch-" + Guid.NewGuid().ToString("N"));

    public ConfigWatcherTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Debouncer_turns_a_burst_of_triggers_into_one_call()
    {
        var calls = 0;
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(120), () => Interlocked.Increment(ref calls));

        for (var i = 0; i < 6; i++)
        {
            debouncer.Trigger();
            await Task.Delay(20);
        }

        Assert.Equal(0, Volatile.Read(ref calls)); // still inside the quiet period
        await Task.Delay(400);
        Assert.Equal(1, Volatile.Read(ref calls));
    }

    [Fact]
    public async Task Debouncer_survives_a_throwing_handler()
    {
        using var debouncer = new Debouncer(TimeSpan.FromMilliseconds(30), () => throw new InvalidOperationException());
        debouncer.Trigger();
        await Task.Delay(150);
        debouncer.Trigger(); // timer still usable
        await Task.Delay(150);
    }

    [Fact]
    public async Task Saving_a_watched_file_raises_one_change_and_other_files_are_ignored()
    {
        var count = 0;
        var fired = new TaskCompletionSource();
        using var watcher = new ConfigWatcher(_dir, TimeSpan.FromMilliseconds(150), "settings.toml", "prompts.toml");
        watcher.Changed += () =>
        {
            Interlocked.Increment(ref count);
            fired.TrySetResult();
        };

        File.WriteAllText(Path.Combine(_dir, "other.txt"), "ignored");
        await Task.Delay(400);
        Assert.Equal(0, Volatile.Read(ref count));

        // An editor save is several writes in a row.
        var path = Path.Combine(_dir, "prompts.toml");
        File.WriteAllText(path, "a");
        File.AppendAllText(path, "b");
        File.AppendAllText(path, "c");

        Assert.Same(fired.Task, await Task.WhenAny(fired.Task, Task.Delay(5000)));
        await Task.Delay(500);
        Assert.Equal(1, Volatile.Read(ref count));
    }

    [Fact]
    public async Task Save_via_temp_file_and_rename_is_noticed()
    {
        var fired = new TaskCompletionSource();
        using var watcher = new ConfigWatcher(_dir, TimeSpan.FromMilliseconds(100), "settings.toml");
        watcher.Changed += () => fired.TrySetResult();

        var temp = Path.Combine(_dir, "settings.toml.tmp");
        File.WriteAllText(temp, "x");
        File.Move(temp, Path.Combine(_dir, "settings.toml"));

        Assert.Same(fired.Task, await Task.WhenAny(fired.Task, Task.Delay(5000)));
    }
}

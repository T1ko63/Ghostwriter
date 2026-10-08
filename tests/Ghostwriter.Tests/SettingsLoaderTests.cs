using Ghostwriter.Core.Config;
using Ghostwriter.Core.Providers;

namespace Ghostwriter.Tests;

public class SettingsLoaderTests
{
    private static string? NoEnv(string name) => null;

    private static string? Env(string name) => name switch
    {
        "GEMINI_API_KEY" => "gem-key",
        "OPENAI_API_KEY" => "oa-key",
        _ => null,
    };

    private static SettingsLoadResult Parse(string toml, Func<string, string?>? env = null) => SettingsLoader.Parse(toml, env ?? NoEnv);

    private static ConfigIssue Error(SettingsLoadResult result) => Assert.Single(result.Issues, i => i.IsError);

    [Fact]
    public void The_generated_default_file_is_valid()
    {
        var result = Parse(DefaultSettings.Create(Env), Env);

        Assert.True(result.Ok, string.Join("; ", result.Issues));
        var settings = result.Settings!;
        Assert.Equal("gemini", settings.DefaultProvider);
        Assert.Equal(4, settings.Providers.Count);
        Assert.Equal(ProviderType.OpenAiCompatible, settings.Providers["local"].Type);
        Assert.Equal("gem-key", settings.Providers["gemini"].ApiKey);
        Assert.Equal("Ctrl+Shift+Space", settings.OverlayHotkey);
        Assert.Equal("<<", settings.MarkerStart);
    }

    [Theory]
    [InlineData("ANTHROPIC_API_KEY", "anthropic")]
    [InlineData("OPENAI_API_KEY", "openai")]
    public void Default_provider_follows_the_key_found_in_the_environment(string variable, string expected)
    {
        string? GetEnv(string name) => name == variable ? "k" : null;
        Assert.Contains($"default_provider = \"{expected}\"", DefaultSettings.Create(GetEnv));
    }

    [Fact]
    public void Missing_environment_variable_is_a_warning_not_an_error()
    {
        var result = Parse(DefaultSettings.Create(NoEnv));
        Assert.True(result.Ok);
        Assert.Contains(result.Issues, i => !i.IsError && i.Message.Contains("GEMINI_API_KEY"));
        Assert.Null(result.Settings!.Providers["gemini"].ApiKey);
    }

    [Fact]
    public void Direct_api_key_wins_over_the_environment()
    {
        var result = Parse("""
            [providers.a]
            type = "gemini"
            model = "m"
            api_key = "direct"
            api_key_env = "GEMINI_API_KEY"
            """, Env);
        Assert.Equal("direct", result.Settings!.Providers["a"].ApiKey);
    }

    [Fact]
    public void Syntax_error_reports_the_line_and_yields_no_settings()
    {
        var result = Parse("theme = \"dark\"\noverlay_hotkey = \nautostart = true\n");
        Assert.False(result.Ok);
        var issue = Error(result);
        Assert.Equal("settings.toml", issue.File);
        Assert.Equal(2, issue.Line);
    }

    [Fact]
    public void Unknown_provider_type_is_an_error_with_a_line_number()
    {
        var result = Parse("[providers.x]\ntype = \"claude\"\nmodel = \"m\"\n");
        Assert.False(result.Ok);
        var issue = Error(result);
        Assert.Contains("unknown type 'claude'", issue.Message);
        Assert.Equal(2, issue.Line);
    }

    [Fact]
    public void Missing_model_is_an_error()
    {
        var result = Parse("[providers.x]\ntype = \"openai\"\n");
        Assert.Contains("model is missing", Error(result).Message);
    }

    [Fact]
    public void Unknown_default_provider_is_an_error()
    {
        var result = Parse("default_provider = \"nope\"\n[providers.x]\ntype = \"gemini\"\nmodel = \"m\"\napi_key = \"k\"\n");
        Assert.Contains("'nope' is not defined", Error(result).Message);
    }

    [Fact]
    public void First_provider_becomes_the_default_when_none_is_named()
    {
        var result = Parse("[providers.x]\ntype = \"gemini\"\nmodel = \"m\"\napi_key = \"k\"\n");
        Assert.Equal("x", result.Settings!.DefaultProvider);
    }

    [Fact]
    public void Api_key_is_never_sent_over_plain_http_to_a_remote_host()
    {
        var result = Parse("[providers.x]\ntype = \"openai-compatible\"\nbase_url = \"http://api.example.com/v1\"\nmodel = \"m\"\napi_key = \"k\"\n");
        Assert.Contains("https://", Error(result).Message);
    }

    [Theory]
    [InlineData("http://localhost:11434/v1")]
    [InlineData("http://127.0.0.1:1234/v1")]
    [InlineData("http://192.168.1.20:8080/v1")]
    [InlineData("http://my-server:8080/v1")]
    public void Plain_http_is_fine_for_local_and_private_addresses(string url)
    {
        var result = Parse($"[providers.x]\ntype = \"openai-compatible\"\nbase_url = \"{url}\"\nmodel = \"m\"\napi_key = \"k\"\n");
        Assert.True(result.Ok, string.Join("; ", result.Issues));
    }

    [Fact]
    public void Local_provider_needs_no_key()
    {
        var result = Parse("[providers.l]\ntype = \"openai-compatible\"\nmodel = \"llama\"\n");
        Assert.True(result.Ok);
        Assert.DoesNotContain(result.Issues, i => i.Message.Contains("api_key"));
        Assert.Equal("http://localhost:11434/v1", result.Settings!.Providers["l"].BaseUrl);
    }

    [Fact]
    public void Idle_trim_defaults_to_90_seconds_and_can_be_switched_off()
    {
        Assert.Equal(90, Parse(string.Empty).Settings!.IdleTrimSeconds);
        Assert.Equal(0, Parse("idle_trim_seconds = 0\n").Settings!.IdleTrimSeconds);
        Assert.Equal(300, Parse("idle_trim_seconds = 300\n").Settings!.IdleTrimSeconds);
    }

    [Fact]
    public void Negative_idle_trim_is_an_error()
    {
        var result = Parse("idle_trim_seconds = -5\n");
        Assert.False(result.Ok);
        Assert.Contains("idle_trim_seconds", Error(result).Message);
    }

    [Fact]
    public void Undo_settings_have_defaults_and_can_be_changed()
    {
        var defaults = Parse("theme = \"dark\"\n").Settings!;
        Assert.Equal("Ctrl+Alt+Z", defaults.UndoHotkey);
        Assert.Equal(10, defaults.UndoHistory);

        var custom = Parse("undo_hotkey = \"Ctrl+Shift+Z\"\nundo_history = 3\n").Settings!;
        Assert.Equal("Ctrl+Shift+Z", custom.UndoHotkey);
        Assert.Equal(3, custom.UndoHistory);
        Assert.Equal(0, Parse("undo_history = 0\n").Settings!.UndoHistory);
    }

    [Theory]
    [InlineData("undo_hotkey = \"Banana\"", "undo_hotkey")]
    [InlineData("undo_history = -1", "undo_history must be between")]
    [InlineData("undo_history = 101", "undo_history must be between")]
    public void Invalid_undo_settings_are_errors(string line, string expectedMessagePart)
    {
        var result = Parse(line + "\n");
        Assert.False(result.Ok);
        Assert.Contains(expectedMessagePart, Error(result).Message);
    }

    [Fact]
    public void Accent_defaults_to_none()
    {
        Assert.Equal("none", Parse("theme = \"dark\"\n").Settings!.Accent);
        Assert.Equal("none", Parse(DefaultSettings.Create(Env), Env).Settings!.Accent);
    }

    [Theory]
    [InlineData("none", "none")]
    [InlineData("System", "system")]
    [InlineData("#3B82F6", "#3b82f6")]
    [InlineData("#fff", "#fff")]
    [InlineData("#803B82F6", "#803b82f6")]
    public void Valid_accents_are_accepted(string value, string expected)
    {
        var result = Parse($"accent = \"{value}\"\n");
        Assert.True(result.Ok, string.Join("; ", result.Issues));
        Assert.Equal(expected, result.Settings!.Accent);
    }

    [Theory]
    [InlineData("overlay_hotkey = \"Banana\"", "overlay_hotkey")]
    [InlineData("overlay_hotkey = \"K\"", "overlay_hotkey")]
    [InlineData("theme = \"neon\"", "theme must be one of")]
    [InlineData("overlay_position = \"left\"", "overlay_position must be one of")]
    [InlineData("autostart = \"yes\"", "autostart must be true or false")]
    [InlineData("marker_start = \"\"", "marker_start and marker_end")]
    [InlineData("accent = \"red\"", "accent must be")]
    [InlineData("accent = \"#12\"", "accent must be")]
    [InlineData("accent = \"3B82F6\"", "accent must be")]
    public void Invalid_global_settings_are_errors(string line, string expectedMessagePart)
    {
        var result = Parse(line + "\n");
        Assert.False(result.Ok);
        Assert.Contains(expectedMessagePart, Error(result).Message);
        Assert.Equal(1, Error(result).Line);
    }

    [Theory]
    [InlineData("timeout_seconds = 0", "timeout_seconds")]
    [InlineData("timeout_seconds = \"fast\"", "timeout_seconds")]
    [InlineData("reasoning = \"extreme\"", "reasoning must be one of")]
    [InlineData("max_output_tokens = -5", "max_output_tokens")]
    [InlineData("base_url = \"not a url\"", "base_url")]
    public void Invalid_provider_settings_are_errors(string line, string expectedMessagePart)
    {
        var result = Parse($"[providers.x]\ntype = \"gemini\"\nmodel = \"m\"\napi_key = \"k\"\n{line}\n");
        Assert.False(result.Ok);
        Assert.Contains(expectedMessagePart, Error(result).Message);
    }

    [Fact]
    public void Provider_names_are_case_insensitive_and_must_be_unique()
    {
        var result = Parse("[providers.Gem]\ntype = \"gemini\"\nmodel = \"m\"\napi_key = \"k\"\n[providers.gem]\ntype = \"gemini\"\nmodel = \"m\"\napi_key = \"k\"\n");
        Assert.Contains("used twice", Error(result).Message);
    }

    [Fact]
    public void Unknown_keys_are_warnings_so_typos_are_noticed_but_do_not_break_the_config()
    {
        var result = Parse("thme = \"dark\"\n[providers.x]\ntype = \"gemini\"\nmodel = \"m\"\napi_key = \"k\"\ntimeout = 5\n");
        Assert.True(result.Ok);
        Assert.Contains(result.Issues, i => !i.IsError && i.Message.Contains("'thme'"));
        Assert.Contains(result.Issues, i => !i.IsError && i.Message.Contains("'timeout'"));
    }

    [Fact]
    public void Empty_file_gives_defaults_and_a_hint_that_no_provider_exists()
    {
        var result = Parse(string.Empty);
        Assert.True(result.Ok);
        Assert.Null(result.Settings!.DefaultProvider);
        Assert.Contains(result.Issues, i => i.Message.Contains("no provider configured"));
    }

    [Fact]
    public void EnsureExists_writes_the_file_once_and_never_overwrites()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ip-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "settings.toml");
        try
        {
            DefaultSettings.EnsureExists(path, NoEnv);
            Assert.True(File.Exists(path));

            File.WriteAllText(path, "theme = \"dark\"");
            DefaultSettings.EnsureExists(path, NoEnv);
            Assert.Equal("theme = \"dark\"", File.ReadAllText(path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Api_key_that_is_not_text_is_a_warning_naming_the_real_problem()
    {
        var result = Parse("[providers.a]\ntype = \"openai\"\nmodel = \"m\"\napi_key = 123\n");

        // Still only a warning: the file loads as before, the provider simply has no key.
        Assert.True(result.Ok, string.Join("; ", result.Issues));
        Assert.Null(result.Settings!.Providers["a"].ApiKey);
        var warning = Assert.Single(result.Issues, i => !i.IsError && i.Message.Contains("api_key"));
        Assert.Contains("must be text", warning.Message);
        Assert.Equal(4, warning.Line);
    }
}

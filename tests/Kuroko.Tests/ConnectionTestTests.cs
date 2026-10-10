using System.Net;
using Kuroko.Core.Config;
using Kuroko.Core.Providers;
using Kuroko.Core.Secrets;
using static Kuroko.Tests.FakeHandler;
using static Kuroko.Tests.ProviderTestHelpers;

namespace Kuroko.Tests;

public class ConnectionTestTests
{
    private const string Base = "https://api.anthropic.test";

    private static string Answer(string stopReason) => Events(
        ("message_start", "{\"type\":\"message_start\"}"),
        ("content_block_delta", "{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{\"type\":\"text_delta\",\"text\":\"OK\"}}"),
        ("message_delta", $"{{\"type\":\"message_delta\",\"delta\":{{\"stop_reason\":\"{stopReason}\"}}}}"),
        ("message_stop", "{\"type\":\"message_stop\"}"));

    private static ProviderRegistry Registry(FakeHandler http, out ProviderSettings settings)
    {
        settings = Settings(ProviderType.Anthropic, Base);
        return new ProviderRegistry(new Dictionary<string, ProviderSettings> { [settings.Name] = settings }, settings.Name, new HttpClient(http));
    }

    [Fact]
    public async Task Sends_one_tiny_request_without_user_text_and_reports_success()
    {
        var http = new FakeHandler().Respond(Sse(Answer("end_turn")));
        var result = await ConnectionTest.RunAsync(Registry(http, out var settings));

        Assert.True(result.Success);
        Assert.Equal(settings.Name, result.Provider);
        Assert.Equal("model-x", result.Model);
        var call = Assert.Single(http.Calls);
        Assert.Equal(16, (int)call.Json["max_tokens"]!);
        Assert.Equal("ping", (string?)call.Json["messages"]![0]!["content"]);
        Assert.Contains(settings.Name, ConnectionTest.Describe(result));
    }

    [Theory]
    [InlineData("max_tokens")] // a thinking model may use the small budget up: key and model still work
    [InlineData("refusal")]
    public async Task A_cut_off_or_declined_answer_still_counts_as_working(string stopReason)
    {
        var http = new FakeHandler().Respond(Sse(Answer(stopReason)));
        Assert.True((await ConnectionTest.RunAsync(Registry(http, out _))).Success);
    }

    [Fact]
    public async Task A_rejected_key_is_reported_as_auth_error_without_the_key()
    {
        var http = new FakeHandler().Respond(Json(HttpStatusCode.Unauthorized,
            "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key sk-secret-123\"}}"));
        var result = await ConnectionTest.RunAsync(Registry(http, out var settings));

        Assert.False(result.Success);
        Assert.Equal(LlmErrorKind.Auth, result.Error!.Kind);
        var text = ConnectionTest.Describe(result);
        Assert.Contains(settings.Name, text);
        Assert.DoesNotContain("sk-secret-123", text);
    }

    [Fact]
    public async Task No_provider_configured_is_a_config_failure_not_an_exception()
    {
        var registry = new ProviderRegistry(new Dictionary<string, ProviderSettings>(), null, new HttpClient(new FakeHandler()));
        var result = await ConnectionTest.RunAsync(registry);

        Assert.False(result.Success);
        Assert.Equal(LlmErrorKind.Config, result.Error!.Kind);
    }
}

public class StartupHintTests
{
    private static AppSettings Load(string toml, Func<string, string?>? env = null, IKeyStore? store = null)
    {
        var result = SettingsLoader.Parse(toml, env ?? (_ => null), store);
        Assert.True(result.Ok, string.Join("; ", result.Issues));
        return result.Settings!;
    }

    private const string GeminiWithEnv = """
        default_provider = "gemini"
        [providers.gemini]
        type = "gemini"
        model = "gemini-x"
        api_key_env = "GEMINI_API_KEY"
        """;

    [Fact]
    public void Missing_key_names_the_expected_environment_variable_and_the_credential_manager()
    {
        var settings = Load(GeminiWithEnv);
        var hint = StartupHint.Build(settings, firstRun: false);

        Assert.NotNull(hint);
        Assert.Contains("GEMINI_API_KEY", hint);
        Assert.Contains("gemini", hint);
        Assert.Same(settings.Providers["gemini"], StartupHint.MissingKey(settings));
        // Language independent: both texts point to the tray entry that writes to the Credential Manager.
        Assert.Equal(StartupHint.MissingKeyText(settings.Providers["gemini"]), hint);
    }

    [Fact]
    public void A_key_from_the_environment_or_the_key_store_means_no_hint_after_the_first_run()
    {
        Assert.Null(StartupHint.Build(Load(GeminiWithEnv, env: name => name == "GEMINI_API_KEY" ? "k" : null), firstRun: false));

        var store = new OneKeyStore("gemini", "stored");
        Assert.Null(StartupHint.Build(Load(GeminiWithEnv, store: store), firstRun: false));
    }

    [Fact]
    public void Without_api_key_env_the_hint_does_not_invent_a_variable_name()
    {
        var settings = Load("""
            default_provider = "claude"
            [providers.claude]
            type = "anthropic"
            model = "m"
            """);
        var hint = StartupHint.Build(settings, firstRun: false)!;

        Assert.DoesNotContain("_API_KEY", hint);
        Assert.Contains("api_key_env", hint);
    }

    [Fact]
    public void A_local_server_needs_no_key_and_the_first_run_still_gets_the_overlay_hotkey()
    {
        var settings = Load("""
            default_provider = "local"
            [providers.local]
            type = "openai-compatible"
            base_url = "http://localhost:11434/v1"
            model = "m"
            """);

        Assert.Null(StartupHint.MissingKey(settings));
        Assert.Null(StartupHint.Build(settings, firstRun: false));
        Assert.Contains(settings.OverlayHotkey, StartupHint.Build(settings, firstRun: true));
    }
}

internal sealed class OneKeyStore(string provider, string key) : IKeyStore
{
    public string? Read(string name) => name == provider ? key : null;

    public bool Save(string name, string value) => false;

    public bool Remove(string name) => false;
}

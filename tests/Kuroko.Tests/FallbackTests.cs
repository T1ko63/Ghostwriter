using System.Net;
using Kuroko.Core.Config;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;
using static Kuroko.Tests.FakeHandler;

namespace Kuroko.Tests;

public class FallbackRunnerTests
{
    private static readonly PromptDefinition Prompt = new("P", "Fix it.");

    private static string Answer(string text) => Events(
        ("content_block_delta", $"{{\"type\":\"content_block_delta\",\"delta\":{{\"type\":\"text_delta\",\"text\":\"{text}\"}}}}"),
        ("message_stop", "{\"type\":\"message_stop\"}"));

    private static ProviderSettings Provider(string name, string host, string model, string? fallback)
        // Unique names: the "which reasoning variant worked" memory is keyed by provider name.
        => new(name, ProviderType.Anthropic, $"https://{host}", model, "key", TimeSpan.FromSeconds(5), FallbackProvider: fallback);

    /// <summary>"main" (fallback: "backup", unless <paramref name="mainFallback"/> says otherwise) and "backup" (fallback: "main").</summary>
    private static ProviderRegistry Registry(FakeHandler http, string? mainFallback = "backup")
    {
        var settings = new Dictionary<string, ProviderSettings>
        {
            ["main"] = Provider("main", "main.test", "main-model", mainFallback),
            ["backup"] = Provider("backup", "backup.test", "backup-model", "main"),
        };
        return new ProviderRegistry(settings, "main", new HttpClient(http));
    }

    private static HttpResponseMessage Status(HttpStatusCode code) => Json(code, "{\"error\":{\"message\":\"nope\"}}");

    public static TheoryData<string> FallbackCases => ["429", "503", "network"];

    private static FakeHandler FailingMain(string failure) => failure switch
    {
        // Without Retry-After a 429 is not retried; a 5xx and a fast network error get one retry inside the provider.
        "429" => new FakeHandler().Respond(Status(HttpStatusCode.TooManyRequests)),
        "503" => new FakeHandler().Respond(Status(HttpStatusCode.ServiceUnavailable)).Respond(Status(HttpStatusCode.ServiceUnavailable)),
        _ => new FakeHandler().Fail(new HttpRequestException("no such host")).Fail(new HttpRequestException("no such host")),
    };

    [Theory]
    [MemberData(nameof(FallbackCases))]
    public async Task Unreachable_provider_falls_back_once_and_reports_it(string failure)
    {
        var http = FailingMain(failure).Respond(Sse(Answer("Fixed.")));
        using var registry = Registry(http);
        var reported = new List<ProviderFallback>();

        var result = await new ProviderPromptRunner(() => registry).RunAsync(Prompt, "fixd", CancellationToken.None, onFallback: reported.Add);

        Assert.Equal("Fixed.", result);
        var used = Assert.Single(reported);
        Assert.Equal(("main", "backup"), (used.From, used.To));
        Assert.Equal("backup.test", http.Calls[^1].Uri.Host);
        Assert.Equal("fixd", (string?)http.Calls[^1].Json["messages"]![0]!["content"]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, LlmErrorKind.Auth)]
    [InlineData(HttpStatusCode.BadRequest, LlmErrorKind.BadRequest)]
    [InlineData(HttpStatusCode.NotFound, LlmErrorKind.ModelNotFound)]
    public async Task Rejected_request_does_not_fall_back(HttpStatusCode status, LlmErrorKind kind)
    {
        var http = new FakeHandler().Respond(Status(status));
        using var registry = Registry(http);
        var reported = new List<ProviderFallback>();

        var error = await Assert.ThrowsAsync<LlmException>(() =>
            new ProviderPromptRunner(() => registry).RunAsync(Prompt, "t", CancellationToken.None, onFallback: reported.Add));

        Assert.Equal(kind, error.Kind);
        Assert.Single(http.Calls);
        Assert.Empty(reported);
    }

    [Fact]
    public async Task Without_a_configured_fallback_the_error_stays()
    {
        var http = new FakeHandler().Respond(Status(HttpStatusCode.TooManyRequests));
        using var registry = Registry(http, mainFallback: null);

        var error = await Assert.ThrowsAsync<LlmException>(() =>
            new ProviderPromptRunner(() => registry).RunAsync(Prompt, "t", CancellationToken.None));

        Assert.Equal(LlmErrorKind.RateLimit, error.Kind);
        Assert.Single(http.Calls);
    }

    [Fact]
    public async Task Only_one_step_is_taken_even_when_the_fallback_points_back()
    {
        // main -> backup -> main would be a cycle; the fallback's own fallback is never followed.
        var http = new FakeHandler()
            .Respond(Status(HttpStatusCode.TooManyRequests))
            .Respond(Status(HttpStatusCode.TooManyRequests));
        using var registry = Registry(http);

        var error = await Assert.ThrowsAsync<LlmException>(() =>
            new ProviderPromptRunner(() => registry).RunAsync(Prompt, "t", CancellationToken.None));

        Assert.Equal("backup", error.Provider);
        Assert.Equal(2, http.Calls.Count);
    }

    [Fact]
    public async Task Fallback_uses_its_own_model_not_the_one_the_prompt_names()
    {
        var http = new FakeHandler().Respond(Status(HttpStatusCode.TooManyRequests)).Respond(Sse(Answer("x")));
        using var registry = Registry(http);

        await new ProviderPromptRunner(() => registry).RunAsync(Prompt with { Model = "special" }, "t", CancellationToken.None);

        Assert.Equal("special", (string?)http.Calls[0].Json["model"]);
        Assert.Equal("backup-model", (string?)http.Calls[1].Json["model"]);
    }

    [Fact]
    public async Task Cancel_never_falls_back()
    {
        using var cts = new CancellationTokenSource();
        var http = new FakeHandler().Respond(async ct =>
        {
            cts.Cancel();
            await Task.Delay(Timeout.Infinite, ct);
            return Sse(string.Empty);
        });
        using var registry = Registry(http);
        var reported = new List<ProviderFallback>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ProviderPromptRunner(() => registry).RunAsync(Prompt, "t", cts.Token, onFallback: reported.Add));

        Assert.Single(http.Calls);
        Assert.Empty(reported);
    }

    [Fact]
    public async Task Connection_lost_after_the_first_text_does_not_fall_back()
    {
        // The first piece is already on screen in the result card; a second answer must not be appended to it.
        var http = new FakeHandler().Respond(Sse(Events(
            ("content_block_delta", "{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"Half\"}}"))));
        using var registry = Registry(http);
        var pieces = new List<string>();

        var error = await Assert.ThrowsAsync<LlmException>(async () =>
        {
            await foreach (var piece in new ProviderPromptRunner(() => registry).StreamAsync(Prompt, "t", CancellationToken.None))
            {
                pieces.Add(piece);
            }
        });

        Assert.Equal(LlmErrorKind.Protocol, error.Kind);
        Assert.Equal("main", error.Provider);
        Assert.Single(http.Calls);
    }

    [Fact]
    public async Task Streaming_falls_back_before_the_first_piece()
    {
        var http = new FakeHandler().Respond(Status(HttpStatusCode.TooManyRequests)).Respond(Sse(Answer("From backup")));
        using var registry = Registry(http);
        var reported = new List<ProviderFallback>();
        var text = string.Empty;

        await foreach (var piece in new ProviderPromptRunner(() => registry).StreamAsync(Prompt, "t", CancellationToken.None, onFallback: reported.Add))
        {
            text += piece;
        }

        Assert.Equal("From backup", text);
        Assert.Single(reported);
    }

    [Fact]
    public async Task Empty_answer_of_the_fallback_names_the_fallback()
    {
        var http = new FakeHandler()
            .Respond(Status(HttpStatusCode.TooManyRequests))
            .Respond(Sse(Events(("message_stop", "{\"type\":\"message_stop\"}"))));
        using var registry = Registry(http);

        var error = await Assert.ThrowsAsync<LlmException>(() =>
            new ProviderPromptRunner(() => registry).RunAsync(Prompt, "t", CancellationToken.None));

        Assert.Equal(LlmErrorKind.EmptyResponse, error.Kind);
        Assert.Equal("backup", error.Provider);
    }
}

public class FallbackConfigTests
{
    // LF line endings whatever the checkout uses (core.autocrlf turns them into CRLF on Windows), so the tests below can
    // insert lines with Replace("...\n", ...).
    private static readonly string Providers = """
        [providers.gemini]
        type = "gemini"
        model = "m"
        api_key = "k"

        [providers.openai]
        type = "openai"
        model = "m"
        api_key = "k"

        [providers.local]
        type = "openai-compatible"
        base_url = "http://localhost:11434/v1"
        model = "llama"

        """.ReplaceLineEndings("\n");

    private static SettingsLoadResult Parse(string toml) => SettingsLoader.Parse(toml, _ => null);

    private static IReadOnlyDictionary<string, ProviderSettings> Loaded(string toml)
    {
        var result = Parse(toml);
        Assert.True(result.Ok, string.Join("; ", result.Issues));
        return result.Settings!.Providers;
    }

    [Fact]
    public void Fallback_is_off_by_default()
    {
        Assert.All(Loaded(Providers).Values, p => Assert.Null(p.FallbackProvider));
        Assert.All(Loaded("fallback_provider = \"\"\n" + Providers).Values, p => Assert.Null(p.FallbackProvider));
    }

    [Fact]
    public void Global_fallback_applies_to_remote_providers_but_not_to_itself_or_local_ones()
    {
        var providers = Loaded("fallback_provider = \"OpenAI\"\n" + Providers);

        Assert.Equal("openai", providers["gemini"].FallbackProvider); // spelled like the [providers.*] header
        Assert.Null(providers["openai"].FallbackProvider);
        Assert.Null(providers["local"].FallbackProvider); // a text meant to stay local does not go out by itself
    }

    [Fact]
    public void Provider_value_wins_over_the_global_one_and_empty_turns_it_off()
    {
        var toml = "fallback_provider = \"local\"\n" + Providers
            .Replace("[providers.openai]\n", "[providers.openai]\nfallback_provider = \"\"\n")
            .Replace("[providers.gemini]\n", "[providers.gemini]\nfallback_provider = \"openai\"\n");
        var providers = Loaded(toml);

        Assert.Equal("openai", providers["gemini"].FallbackProvider);
        Assert.Null(providers["openai"].FallbackProvider);
    }

    [Fact]
    public void A_local_provider_falls_back_only_when_its_own_block_says_so()
    {
        var providers = Loaded(Providers + "fallback_provider = \"gemini\"\n");
        Assert.Equal("gemini", providers["local"].FallbackProvider);
    }

    [Fact]
    public void Mutual_fallbacks_are_allowed_because_only_one_step_is_taken()
    {
        var toml = Providers
            .Replace("[providers.openai]\n", "[providers.openai]\nfallback_provider = \"gemini\"\n")
            .Replace("[providers.gemini]\n", "[providers.gemini]\nfallback_provider = \"openai\"\n");
        var providers = Loaded(toml);

        Assert.Equal("openai", providers["gemini"].FallbackProvider);
        Assert.Equal("gemini", providers["openai"].FallbackProvider);
    }

    [Fact]
    public void Unknown_global_fallback_is_an_error_with_line_and_known_names()
    {
        var result = Parse("default_provider = \"gemini\"\nfallback_provider = \"ollama\"\n" + Providers);

        Assert.False(result.Ok);
        var error = Assert.Single(result.Issues, i => i.IsError);
        Assert.Equal(2, error.Line);
        Assert.Contains("fallback_provider 'ollama' is not defined", error.Message);
        Assert.Contains("gemini, openai, local", error.Message);
    }

    [Fact]
    public void Unknown_provider_fallback_is_an_error_in_that_block()
    {
        var result = Parse(Providers.Replace("[providers.openai]\n", "[providers.openai]\nfallback_provider = \"nope\"\n"));

        var error = Assert.Single(result.Issues, i => i.IsError);
        Assert.Equal(7, error.Line);
        Assert.StartsWith("[providers.openai] fallback_provider 'nope' is not defined", error.Message);
    }

    [Fact]
    public void A_provider_cannot_be_its_own_fallback()
    {
        var result = Parse(Providers.Replace("[providers.gemini]\n", "[providers.gemini]\nfallback_provider = \"Gemini\"\n"));

        var error = Assert.Single(result.Issues, i => i.IsError);
        Assert.Contains("must name another provider", error.Message);
    }

    [Fact]
    public void Fallback_provider_must_be_text()
    {
        var result = Parse("fallback_provider = 3\n" + Providers);
        Assert.Contains(result.Issues, i => i.IsError && i.Message == "fallback_provider must be text");
    }
}

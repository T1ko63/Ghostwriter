using System.Net;
using InstaPrompt.Core.Providers;
using static InstaPrompt.Tests.FakeHandler;
using static InstaPrompt.Tests.ProviderTestHelpers;

namespace InstaPrompt.Tests;

public class OpenAiProviderTests
{
    private const string Base = "https://api.openai.test/v1";

    private static string Ok(params string[] deltas)
        => Events(deltas.Select(d => ((string?)"response.output_text.delta", $"{{\"type\":\"response.output_text.delta\",\"delta\":\"{d}\"}}"))
            .Prepend(("response.created", "{\"type\":\"response.created\"}"))
            .Append(("response.completed", "{\"type\":\"response.completed\",\"response\":{}}")).ToArray());

    [Fact]
    public async Task Sends_responses_api_request_and_streams_text()
    {
        var http = new FakeHandler().Respond(Sse(Ok("Hallo ", "Welt")));
        var provider = new OpenAiProvider(Settings(ProviderType.OpenAi, Base), new HttpClient(http));

        Assert.Equal("Hallo Welt", await CollectAsync(provider));

        var call = Assert.Single(http.Calls);
        Assert.Equal("https://api.openai.test/v1/responses", call.Uri.ToString());
        Assert.Equal("Bearer sk-secret-123", call.Headers["Authorization"]);
        Assert.Equal("model-x", (string?)call.Json["model"]);
        Assert.Equal("SYS", (string?)call.Json["instructions"]);
        Assert.Equal("USER", (string?)call.Json["input"]);
        Assert.True((bool)call.Json["stream"]!);
        Assert.False((bool)call.Json["store"]!);
        Assert.Equal(512, (int)call.Json["max_output_tokens"]!);
        Assert.Equal("none", (string?)call.Json["reasoning"]!["effort"]);
    }

    [Fact]
    public async Task Steps_down_the_reasoning_ladder_when_the_model_rejects_an_option_and_remembers_it()
    {
        var http = new FakeHandler()
            .Respond(Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Unsupported value: 'none' is not supported for reasoning.effort with this model.\"}}"))
            .Respond(Sse(Ok("A")))
            .Respond(Sse(Ok("B")));
        var provider = new OpenAiProvider(Settings(ProviderType.OpenAi, Base), new HttpClient(http));

        Assert.Equal("A", await CollectAsync(provider));
        Assert.Equal(2, http.Calls.Count);
        Assert.Equal("minimal", (string?)http.Calls[1].Json["reasoning"]!["effort"]);

        // The next call starts at the variant that worked: no failing attempt first.
        Assert.Equal("B", await CollectAsync(provider));
        Assert.Equal(3, http.Calls.Count);
        Assert.Equal("minimal", (string?)http.Calls[2].Json["reasoning"]!["effort"]);
    }

    [Fact]
    public async Task Default_reasoning_sends_no_reasoning_field()
    {
        var http = new FakeHandler().Respond(Sse(Ok("x")));
        var provider = new OpenAiProvider(Settings(ProviderType.OpenAi, Base, reasoning: "default"), new HttpClient(http));
        await CollectAsync(provider);
        Assert.Null(http.Calls[0].Json["reasoning"]);
    }

    [Fact]
    public async Task Incomplete_response_is_truncated_not_a_result()
    {
        var http = new FakeHandler().Respond(Sse(Events(
            ("response.output_text.delta", "{\"type\":\"response.output_text.delta\",\"delta\":\"Half\"}"),
            ("response.incomplete", "{\"type\":\"response.incomplete\",\"response\":{\"incomplete_details\":{\"reason\":\"max_output_tokens\"}}}"))));
        var error = await FailureAsync(new OpenAiProvider(Settings(ProviderType.OpenAi, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Truncated, error.Kind);
    }

    [Fact]
    public async Task Refusal_is_blocked()
    {
        var http = new FakeHandler().Respond(Sse(Events(
            ("response.refusal.done", "{\"type\":\"response.refusal.done\",\"refusal\":\"I can't help with that.\"}"))));
        var error = await FailureAsync(new OpenAiProvider(Settings(ProviderType.OpenAi, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Blocked, error.Kind);
    }

    [Fact]
    public async Task Error_event_in_stream_fails_with_mapped_kind()
    {
        var http = new FakeHandler().Respond(Sse(Events(
            ("error", "{\"type\":\"error\",\"code\":\"rate_limit_exceeded\",\"message\":\"Slow down\"}"))));
        var error = await FailureAsync(new OpenAiProvider(Settings(ProviderType.OpenAi, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.RateLimit, error.Kind);
    }

    [Fact]
    public async Task Missing_api_key_fails_before_any_request()
    {
        var http = new FakeHandler();
        var error = await FailureAsync(new OpenAiProvider(Settings(ProviderType.OpenAi, Base, key: null), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Auth, error.Kind);
        Assert.Empty(http.Calls);
    }
}

public class OpenAiCompatibleProviderTests
{
    private const string Base = "http://localhost:11434/v1";

    private static string Chunk(string content, string? finish = null)
        => $"{{\"choices\":[{{\"index\":0,\"delta\":{{\"content\":\"{content}\"}},\"finish_reason\":{(finish is null ? "null" : $"\"{finish}\"")}}}]}}";

    [Fact]
    public async Task Sends_chat_completions_request_without_key()
    {
        var http = new FakeHandler().Respond(Sse(Events((null, Chunk("Hi ")), (null, Chunk("there", "stop")), (null, "[DONE]"))));
        var provider = new OpenAiCompatibleProvider(Settings(ProviderType.OpenAiCompatible, Base, key: null), new HttpClient(http));

        Assert.Equal("Hi there", await CollectAsync(provider));

        var call = http.Calls[0];
        Assert.Equal("http://localhost:11434/v1/chat/completions", call.Uri.ToString());
        Assert.False(call.Headers.ContainsKey("Authorization"));
        Assert.Equal("system", (string?)call.Json["messages"]![0]!["role"]);
        Assert.Equal("SYS", (string?)call.Json["messages"]![0]!["content"]);
        Assert.Equal("user", (string?)call.Json["messages"]![1]!["role"]);
        Assert.Equal("USER", (string?)call.Json["messages"]![1]!["content"]);
        Assert.True((bool)call.Json["stream"]!);
        Assert.Equal(512, (int)call.Json["max_tokens"]!);
    }

    [Fact]
    public async Task Sends_bearer_token_when_a_key_is_set()
    {
        var http = new FakeHandler().Respond(Sse(Events((null, Chunk("x", "stop")), (null, "[DONE]"))));
        await CollectAsync(new OpenAiCompatibleProvider(Settings(ProviderType.OpenAiCompatible, "https://llm.example.test/v1"), new HttpClient(http)));
        Assert.Equal("Bearer sk-secret-123", http.Calls[0].Headers["Authorization"]);
    }

    [Fact]
    public async Task Usage_only_chunk_after_stop_is_ignored()
    {
        var http = new FakeHandler().Respond(Sse(Events(
            (null, Chunk("ok", "stop")),
            (null, "{\"choices\":[],\"usage\":{\"total_tokens\":5}}"),
            (null, "[DONE]"))));
        Assert.Equal("ok", await CollectAsync(new OpenAiCompatibleProvider(Settings(ProviderType.OpenAiCompatible, Base), new HttpClient(http))));
    }

    [Fact]
    public async Task Finish_reason_length_is_truncated()
    {
        var http = new FakeHandler().Respond(Sse(Events((null, Chunk("cut", "length")))));
        var error = await FailureAsync(new OpenAiCompatibleProvider(Settings(ProviderType.OpenAiCompatible, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Truncated, error.Kind);
    }

    [Fact]
    public async Task Unknown_model_404_is_reported_with_the_servers_message()
    {
        var http = new FakeHandler().Respond(Json(HttpStatusCode.NotFound, "{\"error\":{\"message\":\"model 'nope' not found\"}}"));
        var error = await FailureAsync(new OpenAiCompatibleProvider(Settings(ProviderType.OpenAiCompatible, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.ModelNotFound, error.Kind);
        Assert.Contains("nope", error.Detail);
    }

    [Fact]
    public async Task Stream_that_ends_without_done_or_stop_is_a_protocol_error()
    {
        var http = new FakeHandler().Respond(Sse(Events((null, Chunk("half")))));
        var error = await FailureAsync(new OpenAiCompatibleProvider(Settings(ProviderType.OpenAiCompatible, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Protocol, error.Kind);
    }
}

public class GeminiProviderTests
{
    private const string Base = "https://generativelanguage.test/v1beta";

    private static string Chunk(string text, string? finish = null)
        => $"{{\"candidates\":[{{\"content\":{{\"parts\":[{{\"text\":\"{text}\"}}],\"role\":\"model\"}}{(finish is null ? string.Empty : $",\"finishReason\":\"{finish}\"")}}}]}}";

    [Fact]
    public async Task Sends_stream_generate_content_request_with_key_in_header_only()
    {
        var http = new FakeHandler().Respond(Sse(Events((null, Chunk("Ich ")), (null, Chunk("habe", "STOP")))));
        var provider = new GeminiProvider(Settings(ProviderType.Gemini, Base), new HttpClient(http));

        Assert.Equal("Ich habe", await CollectAsync(provider));

        var call = http.Calls[0];
        Assert.Equal("https://generativelanguage.test/v1beta/models/model-x:streamGenerateContent?alt=sse", call.Uri.ToString());
        Assert.Equal("sk-secret-123", call.Headers["x-goog-api-key"]);
        Assert.DoesNotContain("sk-secret-123", call.Uri.ToString());
        Assert.Equal("SYS", (string?)call.Json["systemInstruction"]!["parts"]![0]!["text"]);
        Assert.Equal("user", (string?)call.Json["contents"]![0]!["role"]);
        Assert.Equal("USER", (string?)call.Json["contents"]![0]!["parts"]![0]!["text"]);
        Assert.Equal(512, (int)call.Json["generationConfig"]!["maxOutputTokens"]!);
        Assert.Equal(0, (int)call.Json["generationConfig"]!["thinkingConfig"]!["thinkingBudget"]!);
    }

    [Fact]
    public async Task Model_name_with_models_prefix_is_not_doubled()
    {
        var http = new FakeHandler().Respond(Sse(Events((null, Chunk("x", "STOP")))));
        var provider = new GeminiProvider(Settings(ProviderType.Gemini, Base), new HttpClient(http));
        await CollectAsync(provider, new LlmRequest("models/gemini-x", "S", "U", 100));
        Assert.Contains("/models/gemini-x:stream", http.Calls[0].Uri.ToString());
    }

    [Fact]
    public async Task Thought_parts_are_not_part_of_the_answer()
    {
        var http = new FakeHandler().Respond(Sse(Events(
            (null, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"thinking...\",\"thought\":true},{\"text\":\"Answer\"}]},\"finishReason\":\"STOP\"}]}"))));
        Assert.Equal("Answer", await CollectAsync(new GeminiProvider(Settings(ProviderType.Gemini, Base), new HttpClient(http))));
    }

    [Fact]
    public async Task Rejected_thinking_budget_falls_back_to_thinking_level()
    {
        // Real behaviour seen from the API: errors arrive as plain JSON with an event-stream content type.
        var http = new FakeHandler()
            .Respond(Json(HttpStatusCode.BadRequest, "{\"error\":{\"code\":400,\"message\":\"Budget 0 is invalid. This model only works in thinking mode.\",\"status\":\"INVALID_ARGUMENT\"}}"))
            .Respond(Sse(Events((null, Chunk("ok", "STOP")))));
        var provider = new GeminiProvider(Settings(ProviderType.Gemini, Base), new HttpClient(http));

        Assert.Equal("ok", await CollectAsync(provider));
        Assert.Equal("low", (string?)http.Calls[1].Json["generationConfig"]!["thinkingConfig"]!["thinkingLevel"]);
    }

    [Fact]
    public async Task Unrelated_bad_request_is_not_retried()
    {
        var http = new FakeHandler().Respond(Json(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"Invalid JSON payload received.\"}}"));
        var error = await FailureAsync(new GeminiProvider(Settings(ProviderType.Gemini, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.BadRequest, error.Kind);
        Assert.Single(http.Calls);
    }

    [Theory]
    [InlineData("MAX_TOKENS", LlmErrorKind.Truncated)]
    [InlineData("SAFETY", LlmErrorKind.Blocked)]
    [InlineData("PROHIBITED_CONTENT", LlmErrorKind.Blocked)]
    [InlineData("OTHER", LlmErrorKind.Protocol)]
    public async Task Finish_reasons_other_than_stop_fail(string reason, LlmErrorKind expected)
    {
        var http = new FakeHandler().Respond(Sse(Events((null, Chunk("partial", reason)))));
        var error = await FailureAsync(new GeminiProvider(Settings(ProviderType.Gemini, Base), new HttpClient(http)));
        Assert.Equal(expected, error.Kind);
    }

    [Fact]
    public async Task Blocked_prompt_is_reported()
    {
        var http = new FakeHandler().Respond(Sse(Events((null, "{\"promptFeedback\":{\"blockReason\":\"SAFETY\"}}"))));
        var error = await FailureAsync(new GeminiProvider(Settings(ProviderType.Gemini, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Blocked, error.Kind);
    }

    [Fact]
    public async Task Stream_without_finish_reason_is_incomplete()
    {
        var http = new FakeHandler().Respond(Sse(Events((null, Chunk("half")))));
        var error = await FailureAsync(new GeminiProvider(Settings(ProviderType.Gemini, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Protocol, error.Kind);
    }
}

public class AnthropicProviderTests
{
    private const string Base = "https://api.anthropic.test";

    private static string Delta(string text)
        => $"{{\"type\":\"content_block_delta\",\"index\":0,\"delta\":{{\"type\":\"text_delta\",\"text\":\"{text}\"}}}}";

    private static string Ok(params string[] deltas)
        => Events(deltas.Select(d => ((string?)"content_block_delta", Delta(d)))
            .Prepend(("message_start", "{\"type\":\"message_start\"}"))
            .Prepend(("ping", "{\"type\":\"ping\"}"))
            .Append(("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}"))
            .Append(("message_stop", "{\"type\":\"message_stop\"}")).ToArray());

    [Fact]
    public async Task Sends_messages_request_with_required_headers_and_max_tokens()
    {
        var http = new FakeHandler().Respond(Sse(Ok("Guten ", "Tag")));
        var provider = new AnthropicProvider(Settings(ProviderType.Anthropic, Base), new HttpClient(http));

        Assert.Equal("Guten Tag", await CollectAsync(provider));

        var call = http.Calls[0];
        Assert.Equal("https://api.anthropic.test/v1/messages", call.Uri.ToString());
        Assert.Equal("sk-secret-123", call.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", call.Headers["anthropic-version"]);
        Assert.False(call.Headers.ContainsKey("Authorization"));
        Assert.Equal("model-x", (string?)call.Json["model"]);
        Assert.Equal(512, (int)call.Json["max_tokens"]!);
        Assert.True((bool)call.Json["stream"]!);
        Assert.Equal("SYS", (string?)call.Json["system"]);
        Assert.Equal("user", (string?)call.Json["messages"]![0]!["role"]);
        Assert.Equal("USER", (string?)call.Json["messages"]![0]!["content"]);
        Assert.Equal("disabled", (string?)call.Json["thinking"]!["type"]);
    }

    [Fact]
    public async Task Walks_down_thinking_options_for_models_that_cannot_disable_thinking()
    {
        var http = new FakeHandler()
            .Respond(Json(HttpStatusCode.BadRequest, "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"thinking.type: disabled is not supported for this model\"}}"))
            .Respond(Json(HttpStatusCode.BadRequest, "{\"type\":\"error\",\"error\":{\"type\":\"invalid_request_error\",\"message\":\"thinking.type: between_tools requires effort high or below\"}}"))
            .Respond(Sse(Ok("done")));
        var provider = new AnthropicProvider(Settings(ProviderType.Anthropic, Base), new HttpClient(http));

        Assert.Equal("done", await CollectAsync(provider));
        Assert.Equal(3, http.Calls.Count);
        Assert.Equal("between_tools", (string?)http.Calls[1].Json["thinking"]!["type"]);
        Assert.Equal("low", (string?)http.Calls[2].Json["output_config"]!["effort"]);
    }

    [Fact]
    public async Task Refusal_stop_reason_is_blocked()
    {
        var http = new FakeHandler().Respond(Sse(Events(
            ("content_block_delta", Delta("x")),
            ("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"refusal\"}}"),
            ("message_stop", "{\"type\":\"message_stop\"}"))));
        var error = await FailureAsync(new AnthropicProvider(Settings(ProviderType.Anthropic, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Blocked, error.Kind);
    }

    [Fact]
    public async Task Max_tokens_stop_reason_is_truncated()
    {
        var http = new FakeHandler().Respond(Sse(Events(
            ("content_block_delta", Delta("cut")),
            ("message_delta", "{\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}}"),
            ("message_stop", "{\"type\":\"message_stop\"}"))));
        var error = await FailureAsync(new AnthropicProvider(Settings(ProviderType.Anthropic, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Truncated, error.Kind);
    }

    [Fact]
    public async Task Overloaded_error_event_in_stream_is_a_server_error()
    {
        var http = new FakeHandler().Respond(Sse(Events(
            ("content_block_delta", Delta("x")),
            ("error", "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}"))));
        var error = await FailureAsync(new AnthropicProvider(Settings(ProviderType.Anthropic, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Server, error.Kind);
    }

    [Fact]
    public async Task Stream_cut_before_message_stop_is_not_a_result()
    {
        var http = new FakeHandler().Respond(Sse(Events(("content_block_delta", Delta("half")))));
        var error = await FailureAsync(new AnthropicProvider(Settings(ProviderType.Anthropic, Base), new HttpClient(http)));
        Assert.Equal(LlmErrorKind.Protocol, error.Kind);
    }
}

public class SharedProviderBehaviourTests
{
    private const string Base = "https://api.anthropic.test";

    private static string Ok() => Events(
        ("content_block_delta", "{\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"ok\"}}"),
        ("message_stop", "{\"type\":\"message_stop\"}"));

    private static AnthropicProvider Provider(FakeHandler http, string? key = "sk-secret-123", TimeSpan? timeout = null)
    {
        var settings = Settings(ProviderType.Anthropic, Base, key, reasoning: "default") with { Timeout = timeout ?? TimeSpan.FromSeconds(5) };
        return new AnthropicProvider(settings, new HttpClient(http));
    }

    [Fact]
    public async Task Maps_http_errors_and_never_leaks_the_key()
    {
        var http = new FakeHandler().Respond(Json(HttpStatusCode.Unauthorized,
            "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key sk-secret-123\"}}"));
        var error = await FailureAsync(Provider(http));

        Assert.Equal(LlmErrorKind.Auth, error.Kind);
        Assert.Equal(401, error.StatusCode);
        Assert.DoesNotContain("sk-secret-123", error.Message);
        Assert.DoesNotContain("sk-secret-123", error.Detail);
        Assert.Contains("***", error.Detail);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, LlmErrorKind.Auth)]
    [InlineData(HttpStatusCode.NotFound, LlmErrorKind.ModelNotFound)]
    [InlineData(HttpStatusCode.BadRequest, LlmErrorKind.BadRequest)]
    [InlineData(HttpStatusCode.RequestEntityTooLarge, LlmErrorKind.BadRequest)]
    public async Task Maps_status_codes(HttpStatusCode status, LlmErrorKind kind)
    {
        var http = new FakeHandler().Respond(Json(status, "{\"error\":{\"message\":\"nope\"}}"));
        Assert.Equal(kind, (await FailureAsync(Provider(http))).Kind);
    }

    [Fact]
    public async Task Bad_key_answered_with_400_is_still_an_auth_error()
    {
        // Real Gemini behaviour: HTTP 400 INVALID_ARGUMENT instead of 401.
        var http = new FakeHandler().Respond(Json(HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":400,\"message\":\"API key not valid. Please pass a valid API key.\",\"status\":\"INVALID_ARGUMENT\"}}"));
        var error = await FailureAsync(Provider(http));
        Assert.Equal(LlmErrorKind.Auth, error.Kind);
        Assert.Single(http.Calls); // not mistaken for a reasoning option problem
    }

    [Fact]
    public async Task Rate_limit_without_retry_hint_fails_immediately()
    {
        var http = new FakeHandler().Respond(Json((HttpStatusCode)429, "{\"error\":{\"message\":\"quota\"}}"));
        var error = await FailureAsync(Provider(http));
        Assert.Equal(LlmErrorKind.RateLimit, error.Kind);
        Assert.Single(http.Calls);
    }

    [Fact]
    public async Task Overloaded_529_is_retried_once()
    {
        var http = new FakeHandler()
            .Respond(Json((HttpStatusCode)529, "{\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"Overloaded\"}}"))
            .Respond(Sse(Ok()));
        var timings = new LlmTimings();
        var request = new LlmRequest("model-x", "S", "U", 100, timings);

        Assert.Equal("ok", await CollectAsync(Provider(http), request));
        Assert.Equal(2, http.Calls.Count);
        Assert.Equal(2, timings.Attempts);
    }

    [Fact]
    public async Task Persistent_server_error_fails_after_one_retry()
    {
        var http = new FakeHandler()
            .Respond(Json(HttpStatusCode.BadGateway, "bad gateway"))
            .Respond(Json(HttpStatusCode.BadGateway, "bad gateway"));
        var error = await FailureAsync(Provider(http));
        Assert.Equal(LlmErrorKind.Server, error.Kind);
        Assert.Equal(2, http.Calls.Count);
    }

    [Fact]
    public async Task Stale_connection_is_retried_once()
    {
        var http = new FakeHandler()
            .Fail(new HttpRequestException("connection reset"))
            .Respond(Sse(Ok()));
        Assert.Equal("ok", await CollectAsync(Provider(http)));
        Assert.Equal(2, http.Calls.Count);
    }

    [Fact]
    public async Task Unreachable_host_is_a_network_error()
    {
        var http = new FakeHandler()
            .Fail(new HttpRequestException("no such host"))
            .Fail(new HttpRequestException("no such host"));
        Assert.Equal(LlmErrorKind.Network, (await FailureAsync(Provider(http))).Kind);
    }

    [Fact]
    public async Task No_answer_within_the_timeout_is_a_timeout_error()
    {
        var http = new FakeHandler().Respond(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Sse(string.Empty);
        });
        var error = await FailureAsync(Provider(http, timeout: TimeSpan.FromMilliseconds(150)));
        Assert.Equal(LlmErrorKind.Timeout, error.Kind);
    }

    [Fact]
    public async Task User_cancellation_is_not_reported_as_an_llm_error()
    {
        var http = new FakeHandler().Respond(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return Sse(string.Empty);
        });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectAsync(Provider(http), ct: cts.Token));
    }

    [Fact]
    public async Task Requests_ask_for_http2_but_accept_http1()
    {
        var http = new FakeHandler().Respond(Sse(Ok()));
        await CollectAsync(Provider(http));
        Assert.Equal(new Version(2, 0), http.Calls[0].Version);
        Assert.Equal(HttpVersionPolicy.RequestVersionOrLower, http.Calls[0].VersionPolicy);
    }

    [Fact]
    public async Task Warm_up_sends_a_bodyless_head_request_without_credentials()
    {
        var http = new FakeHandler().Respond(Json(HttpStatusCode.NotFound, "{}"));
        await Provider(http).WarmUpAsync();

        var call = Assert.Single(http.Calls);
        Assert.Equal(HttpMethod.Head, call.Method);
        Assert.Equal("https://api.anthropic.test/", call.Uri.ToString());
        Assert.Equal(string.Empty, call.Body);
        Assert.False(call.Headers.ContainsKey("x-api-key"));
        Assert.Equal(new Version(2, 0), call.Version);
    }

    [Fact]
    public async Task Warm_up_never_throws_even_when_the_host_is_down()
    {
        var http = new FakeHandler().Fail(new HttpRequestException("no route"));
        await Provider(http).WarmUpAsync();
    }

    [Fact]
    public async Task Records_timings()
    {
        var http = new FakeHandler().Respond(Sse(Ok()));
        var timings = new LlmTimings();
        await CollectAsync(Provider(http), new LlmRequest("model-x", "S", "U", 100, timings));
        Assert.True(timings.Sent > 0 && timings.Headers >= timings.Sent && timings.FirstToken >= timings.Headers && timings.Done >= timings.FirstToken);
    }

    [Fact]
    public async Task Registry_resolves_default_named_and_unknown_providers()
    {
        var settings = new Dictionary<string, ProviderSettings>
        {
            ["a"] = Settings(ProviderType.Gemini, "https://g.test/v1beta") with { Name = "a" },
            ["b"] = Settings(ProviderType.Anthropic, Base) with { Name = "b" },
        };
        using var registry = new ProviderRegistry(settings, "a", new HttpClient(new FakeHandler()));

        Assert.IsType<GeminiProvider>(registry.Get(null));
        Assert.IsType<AnthropicProvider>(registry.Get("B"));
        Assert.Equal(LlmErrorKind.Config, Assert.Throws<LlmException>(() => registry.Get("zzz")).Kind);
        await registry.WarmUpAsync("zzz"); // must not throw
    }
}

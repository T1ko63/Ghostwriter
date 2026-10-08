using System.Text.Json.Nodes;

namespace InstaPrompt.Core.Providers;

/// <summary>
/// Anthropic Claude via the Messages API: POST {base}/v1/messages with x-api-key and anthropic-version.
/// max_tokens is mandatory there; the answer arrives as content_block_delta events.
/// </summary>
public sealed class AnthropicProvider : LlmProvider
{
    private const string ApiVersion = "2023-06-01";

    public AnthropicProvider(ProviderSettings settings, HttpClient http) : base(settings, http)
    {
    }

    protected override Uri BuildUri(LlmRequest request) => new(Settings.BaseUrl.TrimEnd('/') + "/v1/messages");

    protected override JsonObject BuildBody(LlmRequest request) => new()
    {
        ["model"] = request.Model,
        ["max_tokens"] = request.MaxOutputTokens,
        ["stream"] = true,
        ["system"] = request.System,
        ["messages"] = new JsonArray { new JsonObject { ["role"] = "user", ["content"] = request.User } },
    };

    protected override void AddHeaders(HttpRequestMessage message)
    {
        message.Headers.Add("x-api-key", AuthKey);
        message.Headers.Add("anthropic-version", ApiVersion);
    }

    protected override IReadOnlyList<ReasoningVariant> ReasoningVariants()
    {
        // Which way to "think less" depends on the model generation, so the options are tried in turn:
        // plain thinking off, thinking only between tool calls (newer Sonnet), low effort (models that always think).
        static ReasoningVariant Thinking(string type)
            => new($"thinking={type}", body => body["thinking"] = new JsonObject { ["type"] = type });

        static ReasoningVariant Effort(string effort)
            => new($"effort={effort}", body => body["output_config"] = new JsonObject { ["effort"] = effort });

        var none = new ReasoningVariant("none", null);
        return Settings.Reasoning switch
        {
            "off" => [Thinking("disabled"), Thinking("between_tools"), Effort("low"), none],
            "default" => [none],
            var level => [Effort(level), none],
        };
    }

    protected override void HandleEvent(SseEvent evt, StreamState state)
    {
        using var doc = ParseJson(evt.Data);
        var root = doc.RootElement;
        switch (Str(root, "type") ?? evt.Event)
        {
            case "content_block_delta":
                if (TryObj(root, "delta", out var delta) && Str(delta, "type") == "text_delta"
                    && Str(delta, "text") is { Length: > 0 } text)
                {
                    state.Pending.Enqueue(text);
                }

                break;

            case "message_delta":
                if (TryObj(root, "delta", out var messageDelta))
                {
                    switch (Str(messageDelta, "stop_reason"))
                    {
                        case "refusal":
                            throw Error(LlmErrorKind.Blocked, "the model declined to answer");
                        case "max_tokens" or "model_context_window_exceeded":
                            throw Error(LlmErrorKind.Truncated, "output limit reached");
                    }
                }

                break;

            case "message_stop":
                state.Completed = true;
                break;

            case "error":
                if (TryObj(root, "error", out var error))
                {
                    var kind = Str(error, "type") switch
                    {
                        "authentication_error" or "permission_error" => LlmErrorKind.Auth,
                        "rate_limit_error" => LlmErrorKind.RateLimit,
                        "invalid_request_error" => LlmErrorKind.BadRequest,
                        "not_found_error" => LlmErrorKind.ModelNotFound,
                        _ => LlmErrorKind.Server, // overloaded_error, api_error
                    };
                    throw Error(kind, Str(error, "message") ?? Str(error, "type"));
                }

                throw Error(LlmErrorKind.Server, "error event from the server");
        }
    }
}

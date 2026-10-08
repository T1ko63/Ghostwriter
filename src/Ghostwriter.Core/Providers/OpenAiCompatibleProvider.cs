using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ghostwriter.Core.Providers;

/// <summary>
/// Local and third-party OpenAI-compatible servers (Ollama, LM Studio, llama.cpp, ...) via Chat Completions,
/// the common denominator they all implement: POST {base}/chat/completions. The API key is optional.
/// </summary>
public sealed class OpenAiCompatibleProvider : LlmProvider
{
    public OpenAiCompatibleProvider(ProviderSettings settings, HttpClient http) : base(settings, http)
    {
    }

    protected override Uri BuildUri(LlmRequest request) => new(BaseUrl + "/chat/completions");

    protected override JsonObject BuildBody(LlmRequest request) => new()
    {
        ["model"] = request.Model,
        ["messages"] = new JsonArray
        {
            new JsonObject { ["role"] = "system", ["content"] = request.System },
            new JsonObject { ["role"] = "user", ["content"] = request.User },
        },
        ["stream"] = true,

        // "max_tokens" rather than "max_completion_tokens": that is what local servers understand.
        ["max_tokens"] = request.MaxOutputTokens,
    };

    protected override void AddHeaders(HttpRequestMessage message)
    {
        if (!string.IsNullOrWhiteSpace(Settings.ApiKey))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey);
        }
    }

    protected override void HandleEvent(SseEvent evt, StreamState state)
    {
        if (evt.Data == "[DONE]")
        {
            state.Completed = true;
            return;
        }

        using var doc = ParseJson(evt.Data);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error))
        {
            var message = error.ValueKind == JsonValueKind.Object ? Str(error, "message") : error.ToString();
            throw Error(LlmErrorKind.Server, message ?? "error event from the server");
        }

        if (!TryArray(root, "choices", out var choices) || choices.GetArrayLength() == 0) return; // e.g. a usage-only chunk

        var choice = choices[0];
        if (TryObj(choice, "delta", out var delta) && Str(delta, "content") is { Length: > 0 } text)
        {
            state.Pending.Enqueue(text);
        }

        switch (Str(choice, "finish_reason"))
        {
            case null or "":
                break;
            case "stop":
                state.Completed = true;
                break;
            case "length":
                throw Error(LlmErrorKind.Truncated, "output limit reached");
            case "content_filter":
                throw Error(LlmErrorKind.Blocked, "content filter");
            case var other:
                throw Error(LlmErrorKind.Protocol, $"unexpected finish reason '{other}'");
        }
    }
}

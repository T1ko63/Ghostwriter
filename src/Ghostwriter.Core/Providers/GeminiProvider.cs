using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ghostwriter.Core.Providers;

/// <summary>
/// Google Gemini: POST {base}/models/{model}:streamGenerateContent?alt=sse, key in the x-goog-api-key header
/// (never in the URL, so it cannot leak through logs or error messages).
/// </summary>
public sealed class GeminiProvider : LlmProvider
{
    public GeminiProvider(ProviderSettings settings, HttpClient http) : base(settings, http)
    {
    }

    protected override Uri BuildUri(LlmRequest request)
    {
        var model = request.Model.StartsWith("models/", StringComparison.Ordinal) ? request.Model[7..] : request.Model;
        return new Uri($"{Settings.BaseUrl.TrimEnd('/')}/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse");
    }

    protected override JsonObject BuildBody(LlmRequest request) => new()
    {
        ["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray { new JsonObject { ["text"] = request.System } } },
        ["contents"] = new JsonArray
        {
            new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray { new JsonObject { ["text"] = request.User } },
            },
        },
        ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = request.MaxOutputTokens },
    };

    protected override void AddHeaders(HttpRequestMessage message) => message.Headers.Add("x-goog-api-key", AuthKey);

    protected override IReadOnlyList<ReasoningVariant> ReasoningVariants()
    {
        // Gemini 2.5 takes a token budget, Gemini 3.x a level; some models cannot switch thinking off at all.
        static ReasoningVariant Thinking(string label, Action<JsonObject> set)
            => new(label, body =>
            {
                var thinking = new JsonObject();
                set(thinking);
                ((JsonObject)body["generationConfig"]!)["thinkingConfig"] = thinking;
            });

        static ReasoningVariant Budget(int tokens)
            => Thinking($"thinkingBudget={tokens}", t => t["thinkingBudget"] = tokens);

        static ReasoningVariant Level(string level)
            => Thinking($"thinkingLevel={level}", t => t["thinkingLevel"] = level);

        var none = new ReasoningVariant("none", null);
        return Settings.Reasoning switch
        {
            "off" => [Budget(0), Level("low"), none],
            "default" => [none],
            "low" => [Level("low"), Budget(1024), none],
            "medium" => [Level("medium"), Budget(8192), none],
            "high" => [Level("high"), Budget(24576), none],
            _ => [none],
        };
    }

    protected override void HandleEvent(SseEvent evt, StreamState state)
    {
        using var doc = ParseJson(evt.Data);
        var root = doc.RootElement;

        if (TryObj(root, "error", out var error))
        {
            throw Error(LlmErrorKind.Server, Str(error, "message") ?? "error event from the server");
        }

        if (TryObj(root, "promptFeedback", out var feedback) && Str(feedback, "blockReason") is { } blockReason)
        {
            throw Error(LlmErrorKind.Blocked, $"request blocked ({blockReason})");
        }

        if (!TryArray(root, "candidates", out var candidates) || candidates.GetArrayLength() == 0) return;
        var candidate = candidates[0];

        if (TryObj(candidate, "content", out var content) && TryArray(content, "parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                // Thought summaries are not part of the answer.
                if (part.TryGetProperty("thought", out var thought) && thought.ValueKind == JsonValueKind.True) continue;
                if (Str(part, "text") is { Length: > 0 } text) state.Pending.Enqueue(text);
            }
        }

        switch (Str(candidate, "finishReason"))
        {
            case null or "" or "FINISH_REASON_UNSPECIFIED":
                break;
            case "STOP":
                state.Completed = true;
                break;
            case "MAX_TOKENS":
                throw Error(LlmErrorKind.Truncated, "output limit reached");
            case "SAFETY" or "RECITATION" or "BLOCKLIST" or "PROHIBITED_CONTENT" or "SPII" or "IMAGE_SAFETY" or "LANGUAGE":
                throw Error(LlmErrorKind.Blocked, $"answer blocked ({Str(candidate, "finishReason")})");
            case var other:
                throw Error(LlmErrorKind.Protocol, $"unexpected finish reason '{other}'");
        }
    }
}

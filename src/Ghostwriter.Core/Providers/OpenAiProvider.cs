using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ghostwriter.Core.Providers;

/// <summary>
/// OpenAI via the Responses API (the API OpenAI recommends for current models):
/// POST {base}/responses with "instructions" + "input", streamed as typed SSE events.
/// </summary>
public sealed class OpenAiProvider : LlmProvider
{
    public OpenAiProvider(ProviderSettings settings, HttpClient http) : base(settings, http)
    {
    }

    protected override Uri BuildUri(LlmRequest request) => new(BaseUrl + "/responses");

    protected override JsonObject BuildBody(LlmRequest request) => new()
    {
        ["model"] = request.Model,
        ["instructions"] = request.System,
        ["input"] = request.User,
        ["stream"] = true,
        ["store"] = false, // the text is not kept in the account's response history
        ["max_output_tokens"] = request.MaxOutputTokens,
    };

    protected override void AddHeaders(HttpRequestMessage message)
        => message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AuthKey);

    protected override IReadOnlyList<ReasoningVariant> ReasoningVariants()
    {
        static ReasoningVariant Effort(string effort)
            => new(effort, body => body["reasoning"] = new JsonObject { ["effort"] = effort });

        var none = new ReasoningVariant("none", null);
        return Settings.Reasoning switch
        {
            "off" => [Effort("none"), Effort("minimal"), none], // models differ in which "off" they know
            "default" => [none],
            var level => [Effort(level), none],
        };
    }

    protected override void HandleEvent(SseEvent evt, StreamState state)
    {
        if (evt.Data == "[DONE]") return;

        using var doc = ParseJson(evt.Data);
        var root = doc.RootElement;
        switch (Str(root, "type") ?? evt.Event)
        {
            case "response.output_text.delta":
                if (Str(root, "delta") is { Length: > 0 } delta) state.Pending.Enqueue(delta);
                break;

            case "response.refusal.done":
                throw Error(LlmErrorKind.Blocked, Str(root, "refusal") ?? "the model declined to answer");

            case "response.completed":
                state.Completed = true;
                break;

            case "response.incomplete":
            {
                string? reason = null;
                if (TryObj(root, "response", out var response) && TryObj(response, "incomplete_details", out var details))
                {
                    reason = Str(details, "reason");
                }

                throw reason == "content_filter"
                    ? Error(LlmErrorKind.Blocked, "content filter")
                    : Error(LlmErrorKind.Truncated, reason ?? "output limit reached");
            }

            case "response.failed":
            {
                var detail = TryObj(root, "response", out var response) && TryObj(response, "error", out var error)
                    ? Str(error, "message")
                    : null;
                throw Error(LlmErrorKind.Server, detail ?? "the response failed");
            }

            case "error":
                throw FromErrorEvent(Str(root, "code") ?? Str(root, "type"), Str(root, "message"));
        }
    }

    private LlmException FromErrorEvent(string? code, string? message)
    {
        var kind = code switch
        {
            not null when code.Contains("rate", StringComparison.OrdinalIgnoreCase) => LlmErrorKind.RateLimit,
            not null when code.Contains("auth", StringComparison.OrdinalIgnoreCase)
                || code.Contains("api_key", StringComparison.OrdinalIgnoreCase) => LlmErrorKind.Auth,
            not null when code.Contains("invalid", StringComparison.OrdinalIgnoreCase) => LlmErrorKind.BadRequest,
            _ => LlmErrorKind.Server,
        };
        return Error(kind, message ?? code);
    }
}

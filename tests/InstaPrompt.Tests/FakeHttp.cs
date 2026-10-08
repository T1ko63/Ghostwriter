using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using InstaPrompt.Core.Providers;

namespace InstaPrompt.Tests;

/// <summary>A request as the fake server saw it (HttpRequestMessage is disposed after sending, so it is copied).</summary>
public sealed record SeenRequest(HttpMethod Method, Uri Uri, IReadOnlyDictionary<string, string> Headers, string Body, Version Version, HttpVersionPolicy VersionPolicy)
{
    public JsonNode Json => JsonNode.Parse(Body)!;
}

/// <summary>Scripted HTTP server: each call gets the next prepared response.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<SeenRequest> Calls { get; } = new();

    public FakeHandler Respond(HttpResponseMessage response)
    {
        _responses.Enqueue(_ => Task.FromResult(response));
        return this;
    }

    public FakeHandler Respond(Func<CancellationToken, Task<HttpResponseMessage>> factory)
    {
        _responses.Enqueue(factory);
        return this;
    }

    public FakeHandler Fail(Exception exception)
    {
        _responses.Enqueue(_ => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(ct);
        lock (Calls) Calls.Add(new SeenRequest(request.Method, request.RequestUri!, headers, body, request.Version, request.VersionPolicy));

        if (_responses.Count == 0) throw new InvalidOperationException("The fake server has no response left for this call.");
        return await _responses.Dequeue()(ct);
    }

    public static HttpResponseMessage Sse(string events, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(events, Encoding.UTF8, "text/event-stream") };

    public static HttpResponseMessage Json(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    /// <summary>Builds "event: x\ndata: y\n\n" blocks.</summary>
    public static string Events(params (string? Event, string Data)[] events)
        => string.Concat(events.Select(e => (e.Event is null ? string.Empty : $"event: {e.Event}\n") + $"data: {e.Data}\n\n"));
}

public static class ProviderTestHelpers
{
    public static ProviderSettings Settings(ProviderType type, string baseUrl, string? key = "sk-secret-123", string reasoning = "off")
        // Unique name per test: the "which reasoning variant worked" memory is keyed by provider name.
        => new($"test-{Guid.NewGuid():N}", type, baseUrl, "model-x", key, TimeSpan.FromSeconds(5), reasoning);

    public static async Task<string> CollectAsync(ILlmProvider provider, LlmRequest? request = null, CancellationToken ct = default)
    {
        var text = new StringBuilder();
        await foreach (var chunk in provider.StreamAsync(request ?? new LlmRequest("model-x", "SYS", "USER", 512), ct))
        {
            text.Append(chunk);
        }

        return text.ToString();
    }

    public static async Task<LlmException> FailureAsync(ILlmProvider provider, LlmRequest? request = null)
        => await Assert.ThrowsAsync<LlmException>(() => CollectAsync(provider, request));
}

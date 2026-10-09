using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Kuroko.Core.Diagnostics;

namespace Kuroko.Core.Providers;

/// <summary>Optional reasoning/thinking setting added to the request body. A null <see cref="Apply"/> sends nothing.</summary>
public sealed record ReasoningVariant(string Label, Action<JsonObject>? Apply);

/// <summary>Mutable state of one streamed answer, filled in by the provider while it parses events.</summary>
public sealed class StreamState
{
    public Queue<string> Pending { get; } = new();

    /// <summary>The provider reported a normal end of the answer.</summary>
    public bool Completed { get; set; }
}

/// <summary>
/// Everything the four adapters have in common: request/response handling, error mapping, timeouts,
/// one automatic retry for stale connections and transient errors, and the ladder of reasoning variants.
/// Adapters only describe their request body and how to read their events.
/// </summary>
public abstract class LlmProvider : ILlmProvider
{
    private static readonly string[] ReasoningWords = ["thinking", "reasoning", "effort", "budget", "level", "thought"];
    private static readonly string[] KeyWords = ["api key", "api_key", "x-api-key", "invalid key", "authentication"];

    // Remembers which reasoning variant worked per provider/model so later calls do not repeat a failing attempt.
    private static readonly ConcurrentDictionary<string, int> WorkingVariant = new();

    protected LlmProvider(ProviderSettings settings, HttpClient http)
    {
        Settings = settings;
        Http = http;
    }

    public ProviderSettings Settings { get; }

    protected HttpClient Http { get; }

    /// <summary>The configured base URL without a trailing slash, ready for appending a path.</summary>
    protected string BaseUrl => Settings.BaseUrl.TrimEnd('/');

    // ---- adapter contract ----

    protected abstract Uri BuildUri(LlmRequest request);

    protected abstract JsonObject BuildBody(LlmRequest request);

    protected virtual void AddHeaders(HttpRequestMessage message)
    {
    }

    /// <summary>Reasoning variants to try in order for the configured <see cref="ProviderSettings.Reasoning"/>. Last one should send nothing.</summary>
    protected virtual IReadOnlyList<ReasoningVariant> ReasoningVariants() => [new ReasoningVariant("none", null)];

    /// <summary>Reads one SSE event: enqueue text, set <see cref="StreamState.Completed"/>, or throw for failures.</summary>
    protected abstract void HandleEvent(SseEvent evt, StreamState state);

    protected virtual Uri WarmUpUri => new(BaseUrl + "/");

    protected LlmException Error(LlmErrorKind kind, string? detail = null, int? status = null, TimeSpan? retryAfter = null, Exception? inner = null)
        => new(kind, Settings.Name, Sanitize(detail), status, retryAfter, inner);

    // ---- public API ----

    public async IAsyncEnumerable<string> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct)
    {
        var variants = ReasoningVariants();
        // Type and address are part of the key: a reloaded provider of the same name may be a different API.
        var memoryKey = $"{Settings.Name}|{Settings.Type}|{Settings.BaseUrl}|{request.Model}|{Settings.Reasoning}";
        var index = WorkingVariant.TryGetValue(memoryKey, out var remembered) && remembered < variants.Count ? remembered : 0;

        while (true)
        {
            var enumerator = StreamOnceAsync(request, variants[index], ct).GetAsyncEnumerator(ct);
            var retryWithNext = false;
            var yielded = false;
            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync();
                    }
                    catch (LlmException ex) when (!yielded && ex.Kind == LlmErrorKind.BadRequest
                        && index + 1 < variants.Count && MentionsReasoning(ex.Detail))
                    {
                        // The model does not accept this reasoning option: step down the ladder and ask again.
                        AppLog.Warn($"{Settings.Name}: reasoning option '{variants[index].Label}' rejected, trying '{variants[index + 1].Label}'.");
                        index++;
                        retryWithNext = true;
                        break;
                    }

                    if (!hasNext) break;
                    yielded = true;
                    yield return enumerator.Current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync();
            }

            if (!retryWithNext)
            {
                WorkingVariant[memoryKey] = index;
                yield break;
            }
        }
    }

    public async Task WarmUpAsync(CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var message = new HttpRequestMessage(HttpMethod.Head, WarmUpUri)
            {
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            using var response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            AppLog.Info($"{Settings.Name}: connection warmed up ({(int)response.StatusCode}, HTTP {response.Version}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Purely an optimisation; the real request will report real problems.
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The contract is "never throws" (callers fire and forget); anything unexpected is still worth a log line.
            AppLog.Warn($"{Settings.Name}: warm-up failed ({ex.GetType().Name}).");
        }
    }

    // ---- one attempt ----

    private async IAsyncEnumerable<string> StreamOnceAsync(
        LlmRequest request, ReasoningVariant variant, [EnumeratorCancellation] CancellationToken ct)
    {
        // The limit applies to the first byte and then to each gap between events, so long answers are fine
        // but a stalled connection is noticed.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(Settings.Timeout);
        var timings = request.Timings;

        HttpResponseMessage response;
        try
        {
            response = await SendWithRetryAsync(request, variant, timeoutCts, ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Error(LlmErrorKind.Timeout, $"no answer within {Settings.Timeout.TotalSeconds:F0} s");
        }

        using (response)
        {
            if (timings is not null) timings.Headers = Stopwatch.GetTimestamp();

            var state = new StreamState();
            var firstToken = true;

            Stream stream;
            try
            {
                stream = await response.Content.ReadAsStreamAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw Error(LlmErrorKind.Timeout, $"no answer within {Settings.Timeout.TotalSeconds:F0} s");
            }

            await using (stream)
            {
                var events = SseReader.ReadAsync(stream, timeoutCts.Token).GetAsyncEnumerator(timeoutCts.Token);
                try
                {
                    while (true)
                    {
                        bool hasNext;
                        try
                        {
                            hasNext = await events.MoveNextAsync();
                        }
                        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                        {
                            throw Error(LlmErrorKind.Timeout, $"stalled for {Settings.Timeout.TotalSeconds:F0} s");
                        }
                        catch (Exception ex) when (ex is IOException or HttpRequestException)
                        {
                            throw Error(LlmErrorKind.Network, "connection lost while receiving", inner: ex);
                        }

                        if (!hasNext) break;
                        timeoutCts.CancelAfter(Settings.Timeout);

                        // After the terminal event only usage/bookkeeping events follow; they are drained, not interpreted,
                        // so the connection can be reused.
                        if (state.Completed) continue;

                        HandleEvent(events.Current, state);
                        while (state.Pending.Count > 0)
                        {
                            if (firstToken && timings is not null) timings.FirstToken = Stopwatch.GetTimestamp();
                            firstToken = false;
                            yield return state.Pending.Dequeue();
                        }
                    }
                }
                finally
                {
                    await events.DisposeAsync();
                }
            }

            if (!state.Completed)
            {
                throw Error(LlmErrorKind.Protocol, "the answer ended unexpectedly");
            }

            if (timings is not null) timings.Done = Stopwatch.GetTimestamp();
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        LlmRequest request, ReasoningVariant variant, CancellationTokenSource timeoutCts, CancellationToken ct)
    {
        var timings = request.Timings;
        var started = Stopwatch.GetTimestamp();

        for (var attempt = 1; ; attempt++)
        {
            if (timings is not null)
            {
                timings.Attempts = attempt;
                if (attempt == 1) timings.Sent = Stopwatch.GetTimestamp();
            }

            using var message = new HttpRequestMessage(HttpMethod.Post, BuildUri(request))
            {
                // HttpClient.DefaultRequestVersion only applies to its convenience methods, not to SendAsync.
                Version = HttpVersion.Version20,
                VersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            };
            var body = BuildBody(request);
            variant.Apply?.Invoke(body);
            message.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            AddHeaders(message);

            HttpResponseMessage response;
            try
            {
                response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            }
            catch (HttpRequestException ex) when (attempt == 1 && !ct.IsCancellationRequested
                && Stopwatch.GetElapsedTime(started).TotalSeconds < 2)
            {
                // Typically a pooled connection the server closed in the meantime: just open a new one.
                AppLog.Info($"{Settings.Name}: request failed fast ({ex.GetType().Name}), retrying once.");
                continue;
            }
            catch (HttpRequestException ex)
            {
                throw Error(LlmErrorKind.Network, $"cannot reach {WarmUpUri.Host}: {ex.Message}", inner: ex);
            }

            if (response.IsSuccessStatusCode) return response;

            string text;
            try
            {
                text = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                text = string.Empty;
            }

            var retryAfter = response.Headers.RetryAfter?.Delta;
            var error = MapHttpError(response.StatusCode, text, retryAfter);
            response.Dispose();

            if (attempt == 1 && IsTransient(error) && (retryAfter ?? TimeSpan.FromMilliseconds(400)) <= TimeSpan.FromSeconds(3))
            {
                AppLog.Info($"{Settings.Name}: {(int)error.StatusCode!}, retrying once.");
                await Task.Delay(retryAfter ?? TimeSpan.FromMilliseconds(400), ct);
                continue;
            }

            throw error;
        }
    }

    // ---- errors ----

    private static bool IsTransient(LlmException error)
        => error.Kind == LlmErrorKind.Server || (error.Kind == LlmErrorKind.RateLimit && error.RetryAfter is not null);

    private LlmException MapHttpError(HttpStatusCode status, string body, TimeSpan? retryAfter)
    {
        var code = (int)status;
        var detail = ExtractErrorMessage(body) ?? status.ToString();
        var kind = code switch
        {
            401 or 403 => LlmErrorKind.Auth,
            404 => LlmErrorKind.ModelNotFound,
            408 => LlmErrorKind.Timeout,
            429 => LlmErrorKind.RateLimit,
            400 or 413 or 422 => LlmErrorKind.BadRequest,
            >= 500 => LlmErrorKind.Server,
            _ => LlmErrorKind.Protocol,
        };

        // Some APIs (Gemini) answer a bad key with 400 instead of 401.
        if (kind == LlmErrorKind.BadRequest && KeyWords.Any(w => detail.Contains(w, StringComparison.OrdinalIgnoreCase)))
        {
            kind = LlmErrorKind.Auth;
        }

        return Error(kind, detail, code, retryAfter);
    }

    /// <summary>Pulls the human-readable message out of a provider error body (all four use {"error":{"message":..}}).</summary>
    protected virtual string? ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0]
                : doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.String) return error.GetString();
                    if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var message)) return message.GetString();
                }

                if (root.TryGetProperty("message", out var top) && top.ValueKind == JsonValueKind.String) return top.GetString();
            }
        }
        catch (JsonException)
        {
            // fall through: plain text body
        }

        return body.Length > 200 ? body[..200] : body;
    }

    private static bool MentionsReasoning(string? detail)
        => detail is not null && ReasoningWords.Any(w => detail.Contains(w, StringComparison.OrdinalIgnoreCase));

    /// <summary>Makes provider text safe for display: no key material, single line, bounded length.</summary>
    protected string? Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (!string.IsNullOrEmpty(Settings.ApiKey)) text = text.Replace(Settings.ApiKey, "***", StringComparison.Ordinal);
        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length > 220 ? text[..220] + "…" : text;
    }

    // ---- helpers for adapters ----

    protected static string? Str(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    protected static bool TryObj(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    protected static bool TryArray(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>Parses event data as JSON; anything else is a protocol error (never silently ignored).</summary>
    protected JsonDocument ParseJson(string data)
    {
        try
        {
            return JsonDocument.Parse(data);
        }
        catch (JsonException ex)
        {
            throw Error(LlmErrorKind.Protocol, "unreadable event from the provider", inner: ex);
        }
    }

    protected string AuthKey => Settings.ApiKey
        ?? throw Error(LlmErrorKind.Auth, "no API key configured (api_key or api_key_env in settings.toml)");
}

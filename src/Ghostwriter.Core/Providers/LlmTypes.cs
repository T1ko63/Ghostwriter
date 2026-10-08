using System.Diagnostics;

namespace Ghostwriter.Core.Providers;

public enum ProviderType
{
    OpenAi,
    Gemini,
    Anthropic,
    OpenAiCompatible,
}

public enum LlmErrorKind
{
    /// <summary>Missing or rejected API key.</summary>
    Auth,
    RateLimit,
    BadRequest,
    ModelNotFound,
    Server,
    Network,
    Timeout,

    /// <summary>The provider's safety system declined to answer.</summary>
    Blocked,

    /// <summary>The answer hit the output limit; a half answer must never replace the user's text.</summary>
    Truncated,
    EmptyResponse,

    /// <summary>The response did not look like what the API documents (or the stream ended early).</summary>
    Protocol,

    /// <summary>Configuration problem (unknown provider, no provider configured).</summary>
    Config,
}

/// <summary>A failed AI call. <see cref="Detail"/> is short, user-presentable and never contains secrets.</summary>
public sealed class LlmException : Exception
{
    public LlmException(LlmErrorKind kind, string provider, string? detail = null, int? statusCode = null, TimeSpan? retryAfter = null, Exception? inner = null)
        : base($"{provider}: {kind}{(string.IsNullOrEmpty(detail) ? string.Empty : " - " + detail)}", inner)
    {
        Kind = kind;
        Provider = provider;
        Detail = detail;
        StatusCode = statusCode;
        RetryAfter = retryAfter;
    }

    public LlmErrorKind Kind { get; }

    public string Provider { get; }

    public string? Detail { get; }

    public int? StatusCode { get; }

    public TimeSpan? RetryAfter { get; }
}

/// <summary>Resolved configuration of one provider (keys already looked up from the environment if needed).</summary>
public sealed record ProviderSettings(
    string Name,
    ProviderType Type,
    string BaseUrl,
    string Model,
    string? ApiKey,
    TimeSpan Timeout,
    string Reasoning = "off",
    int MaxOutputTokens = 0);

/// <summary>One request to a model. Output limit and texts are decided by the caller.</summary>
public sealed record LlmRequest(string Model, string System, string User, int MaxOutputTokens, LlmTimings? Timings = null);

/// <summary>Stopwatch timestamps of one call, filled in by the provider so the caller can log where time went.</summary>
public sealed class LlmTimings
{
    public long Sent { get; set; }

    public long Headers { get; set; }

    public long FirstToken { get; set; }

    public long Done { get; set; }

    public int Attempts { get; set; }

    public static double Ms(long from, long to) => Stopwatch.GetElapsedTime(from, to).TotalMilliseconds;
}

public interface ILlmProvider
{
    ProviderSettings Settings { get; }

    /// <summary>
    /// Streams the answer text in chunks. Completes only if the provider reported a normal end; every
    /// other ending (cut connection, output limit, safety block, error event) throws <see cref="LlmException"/>.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(LlmRequest request, CancellationToken ct);

    /// <summary>Opens the TLS connection ahead of time. Sends no user data and never throws.</summary>
    Task WarmUpAsync(CancellationToken ct = default);
}

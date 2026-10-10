using System.Diagnostics;
using Kuroko.Core.Localization;

namespace Kuroko.Core.Providers;

/// <summary>Outcome of <see cref="ConnectionTest.RunAsync"/>. <see cref="Error"/> is null when the provider answered.</summary>
public sealed record ConnectionTestResult(string Provider, string Model, TimeSpan Elapsed, LlmException? Error)
{
    public bool Success => Error is null;
}

/// <summary>
/// "Test connection" from the tray: one tiny request to a provider, so a missing or rejected key, a wrong model name
/// or a blocked network shows up right away instead of at the first real use. Sends no user text.
/// </summary>
public static class ConnectionTest
{
    // 16 is the smallest output limit the OpenAI Responses API accepts; an answer cut off at that limit still proves
    // that key, model and network work.
    private const int MaxOutputTokens = 16;
    private const string SystemText = "Reply with the single word OK.";
    private const string UserText = "ping";

    /// <summary>Tests the named provider, or the default one for null. Never throws except for cancellation.</summary>
    public static async Task<ConnectionTestResult> RunAsync(ProviderRegistry registry, string? name = null, CancellationToken ct = default)
    {
        ILlmProvider provider;
        try
        {
            provider = registry.Get(name);
        }
        catch (LlmException ex)
        {
            return new ConnectionTestResult(ex.Provider, string.Empty, TimeSpan.Zero, ex);
        }

        return await RunAsync(provider, ct);
    }

    public static async Task<ConnectionTestResult> RunAsync(ILlmProvider provider, CancellationToken ct = default)
    {
        var settings = provider.Settings;
        var started = Stopwatch.GetTimestamp();
        LlmException? error = null;
        try
        {
            await foreach (var _ in provider.StreamAsync(new LlmRequest(settings.Model, SystemText, UserText, MaxOutputTokens), ct))
            {
            }
        }
        catch (LlmException ex) when (ex.Kind is not (LlmErrorKind.Truncated or LlmErrorKind.EmptyResponse or LlmErrorKind.Blocked))
        {
            // Cut off, empty or blocked still means the provider accepted the key and the model; everything else is a real failure.
            error = ex;
        }
        catch (LlmException)
        {
        }

        return new ConnectionTestResult(settings.Name, settings.Model, Stopwatch.GetElapsedTime(started), error);
    }

    /// <summary>One line for the notice shown after the test.</summary>
    public static string Describe(ConnectionTestResult result)
    {
        if (result.Error is not { } ex) return Loc.Get("conn_ok", result.Provider, result.Model, (int)result.Elapsed.TotalMilliseconds);

        var detail = string.IsNullOrWhiteSpace(ex.Detail) ? string.Empty : $" ({ex.Detail})";
        return ex.Kind switch
        {
            LlmErrorKind.Auth => Loc.Get("llm_auth", ex.Provider, detail),
            LlmErrorKind.RateLimit => Loc.Get("llm_rate_limit", ex.Provider, detail),
            LlmErrorKind.ModelNotFound => Loc.Get("llm_model", ex.Provider, detail),
            LlmErrorKind.BadRequest => Loc.Get("llm_bad_request", ex.Provider, detail),
            LlmErrorKind.Network => Loc.Get("conn_network", ex.Provider),
            LlmErrorKind.Timeout => Loc.Get("conn_timeout", ex.Provider),
            LlmErrorKind.Config => Loc.Get("llm_config", detail.Trim(' ', '(', ')')),
            LlmErrorKind.Server => Loc.Get("conn_server", ex.Provider, detail),
            _ => Loc.Get("conn_failed", ex.Provider, detail),
        };
    }
}

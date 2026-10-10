using Kuroko.Core.Providers;

namespace Kuroko.Core.Prompts;

/// <summary>Turns a prompt and an input text into the result text. Fails with <see cref="LlmException"/>, never with partial text.</summary>
public interface IPromptRunner
{
    /// <param name="onFallback">Called when the prompt's provider could not answer and the configured fallback provider is asked instead.</param>
    Task<string> RunAsync(PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null, Action<ProviderFallback>? onFallback = null);

    /// <summary>
    /// Same preparation as <see cref="RunAsync"/>, but the answer arrives piece by piece for display: each piece is
    /// appended to the ones before (trimmed, no whitespace taken over from the input, see <see cref="StreamingOutputCleaner"/>).
    /// A failure (also a cancel) is thrown at the point it happens; what came before is then incomplete and must be discarded.
    /// An answer without any text ends with <see cref="LlmErrorKind.EmptyResponse"/>. A switch to the fallback provider
    /// only happens before the first piece.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(
        PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null, Action<ProviderFallback>? onFallback = null);
}

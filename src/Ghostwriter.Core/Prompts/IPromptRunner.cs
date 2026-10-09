using Ghostwriter.Core.Providers;

namespace Ghostwriter.Core.Prompts;

/// <summary>Turns a prompt and an input text into the result text. Fails with <see cref="LlmException"/>, never with partial text.</summary>
public interface IPromptRunner
{
    Task<string> RunAsync(PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null);

    /// <summary>
    /// Same preparation as <see cref="RunAsync"/>, but the answer arrives piece by piece for display: each piece is
    /// appended to the ones before (trimmed, no whitespace taken over from the input, see <see cref="StreamingOutputCleaner"/>).
    /// A failure (also a cancel) is thrown at the point it happens; what came before is then incomplete and must be discarded.
    /// An answer without any text ends with <see cref="LlmErrorKind.EmptyResponse"/>.
    /// </summary>
    IAsyncEnumerable<string> StreamAsync(PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null);
}

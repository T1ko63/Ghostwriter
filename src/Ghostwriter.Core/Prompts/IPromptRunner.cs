using Ghostwriter.Core.Providers;

namespace Ghostwriter.Core.Prompts;

/// <summary>Turns a prompt and an input text into the result text. Fails with <see cref="LlmException"/>, never with partial text.</summary>
public interface IPromptRunner
{
    Task<string> RunAsync(PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null);
}

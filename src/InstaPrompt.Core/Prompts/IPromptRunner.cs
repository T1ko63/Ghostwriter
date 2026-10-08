using InstaPrompt.Core.Providers;

namespace InstaPrompt.Core.Prompts;

/// <summary>Turns a prompt and an input text into the result text. Fails with <see cref="LlmException"/>, never with partial text.</summary>
public interface IPromptRunner
{
    Task<string> RunAsync(PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null);
}

/// <summary>Stand-in without AI (tests, offline demos): simulates latency and tags the text with the prompt name.</summary>
public sealed class DummyPromptRunner : IPromptRunner
{
    public async Task<string> RunAsync(PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null)
    {
        await Task.Delay(600, ct);
        return $"[{prompt.Name}] {input}";
    }
}

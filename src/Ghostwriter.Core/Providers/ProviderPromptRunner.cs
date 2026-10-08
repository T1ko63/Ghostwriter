using System.Text;
using Ghostwriter.Core.Prompts;

namespace Ghostwriter.Core.Providers;

/// <summary>Runs a prompt against the provider/model it asks for (or the default ones).</summary>
public sealed class ProviderPromptRunner : IPromptRunner
{
    private readonly Func<ProviderRegistry> _registry;
    private readonly Func<(string Start, string End)> _markers;

    /// <param name="registry">A factory, so a reloaded configuration swaps the registry without rebuilding the runner.</param>
    /// <param name="markers">The marker characters from settings.toml, read per run (universal prompts look for them).</param>
    public ProviderPromptRunner(Func<ProviderRegistry> registry, Func<(string Start, string End)>? markers = null)
    {
        _registry = registry;
        _markers = markers ?? (() => ("<<", ">>"));
    }

    /// <exception cref="MarkerException">A universal prompt found an unclosed or empty marker (nothing was sent).</exception>
    public async Task<string> RunAsync(PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null)
    {
        var provider = _registry().Get(prompt.Provider);
        var settings = provider.Settings;

        if (prompt.Mode == PromptMode.Universal)
        {
            var (start, end) = _markers();
            (prompt, input) = UniversalPrompt.Resolve(prompt, input, start, end);
        }

        var (system, user) = PromptBuilder.Build(prompt, input);

        var maxTokens = settings.MaxOutputTokens > 0
            ? settings.MaxOutputTokens
            : PromptBuilder.EstimateMaxOutputTokens(prompt.Mode, input.Length, settings.Reasoning != "off");

        var request = new LlmRequest(prompt.Model ?? settings.Model, system, user, maxTokens, timings);

        // The answer is collected completely before anything is returned: the field is only touched once the
        // provider reported a normal end, so a failure halfway never leaves half a text behind.
        var answer = new StringBuilder();
        await foreach (var chunk in provider.StreamAsync(request, ct))
        {
            answer.Append(chunk);
        }

        var result = OutputCleaner.Clean(answer.ToString(), input);
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new LlmException(LlmErrorKind.EmptyResponse, settings.Name, "the model returned no text");
        }

        return result;
    }
}

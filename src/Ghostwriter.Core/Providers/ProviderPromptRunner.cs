using System.Runtime.CompilerServices;
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

    /// <summary>Everything a call needs, decided from prompt and input: shared by <see cref="RunAsync"/> and <see cref="StreamAsync"/>.</summary>
    private readonly record struct PreparedRun(ILlmProvider Provider, LlmRequest Request, string Input);

    /// <exception cref="MarkerException">A universal prompt found an unclosed or empty marker (nothing was sent).</exception>
    private PreparedRun Prepare(PromptDefinition prompt, string input, LlmTimings? timings)
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

        return new PreparedRun(provider, new LlmRequest(prompt.Model ?? settings.Model, system, user, maxTokens, timings), input);
    }

    /// <exception cref="MarkerException">A universal prompt found an unclosed or empty marker (nothing was sent).</exception>
    public async Task<string> RunAsync(PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null)
    {
        var run = Prepare(prompt, input, timings);

        // The answer is collected completely before anything is returned: the field is only touched once the
        // provider reported a normal end, so a failure halfway never leaves half a text behind.
        var answer = new StringBuilder();
        await foreach (var chunk in run.Provider.StreamAsync(run.Request, ct))
        {
            answer.Append(chunk);
        }

        var result = OutputCleaner.Clean(answer.ToString(), run.Input);
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new LlmException(LlmErrorKind.EmptyResponse, run.Provider.Settings.Name, "the model returned no text");
        }

        return result;
    }

    /// <exception cref="MarkerException">A universal prompt found an unclosed or empty marker (nothing was sent).</exception>
    public async IAsyncEnumerable<string> StreamAsync(
        PromptDefinition prompt, string input, [EnumeratorCancellation] CancellationToken ct, LlmTimings? timings = null)
    {
        var run = Prepare(prompt, input, timings);
        var cleaner = new StreamingOutputCleaner(run.Input);

        await foreach (var chunk in run.Provider.StreamAsync(run.Request, ct))
        {
            var piece = cleaner.Push(chunk);
            if (piece.Length > 0) yield return piece;
        }

        // Only reached after a normal end of the answer; every other ending has thrown inside the loop above.
        var rest = cleaner.Finish();
        if (rest.Length > 0) yield return rest;

        if (!cleaner.HasOutput)
        {
            throw new LlmException(LlmErrorKind.EmptyResponse, run.Provider.Settings.Name, "the model returned no text");
        }
    }
}

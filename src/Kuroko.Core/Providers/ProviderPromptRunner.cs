using System.Runtime.CompilerServices;
using System.Text;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Prompts;

namespace Kuroko.Core.Providers;

/// <summary>Runs a prompt against the provider/model it asks for (or the default ones), with one try of the fallback provider if configured.</summary>
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
    private sealed class PreparedRun(ProviderRegistry registry, ILlmProvider primary, PromptDefinition prompt, string input, string system, string user, LlmTimings? timings)
    {
        public ProviderRegistry Registry { get; } = registry;

        public ILlmProvider Primary { get; } = primary;

        /// <summary>The provider whose answer is being read: the primary one, or the fallback after a switch.</summary>
        public ILlmProvider Answering { get; set; } = primary;

        public string Input { get; } = input;

        /// <summary>
        /// The request for one provider. A model named by the prompt belongs to the prompt's provider; the fallback provider
        /// gets its own model. The output limit follows the provider that is asked.
        /// </summary>
        public LlmRequest RequestFor(ProviderSettings settings, bool usePromptModel)
        {
            var maxTokens = settings.MaxOutputTokens > 0
                ? settings.MaxOutputTokens
                : PromptBuilder.EstimateMaxOutputTokens(prompt.Mode, Input.Length, settings.Reasoning != "off");
            var model = usePromptModel ? prompt.Model ?? settings.Model : settings.Model;
            return new LlmRequest(model, system, user, maxTokens, timings);
        }
    }

    /// <exception cref="MarkerException">A universal prompt found an unclosed or empty marker (nothing was sent).</exception>
    private PreparedRun Prepare(PromptDefinition prompt, string input, LlmTimings? timings)
    {
        // One registry for the whole run, so a config reload in between cannot mix two configurations.
        var registry = _registry();
        var provider = registry.Get(prompt.Provider);

        if (prompt.Mode == PromptMode.Universal)
        {
            var (start, end) = _markers();
            (prompt, input) = UniversalPrompt.Resolve(prompt, input, start, end);
        }

        var (system, user) = PromptBuilder.Build(prompt, input);
        return new PreparedRun(registry, provider, prompt, input, system, user, timings);
    }

    /// <summary>
    /// The raw answer of the prompt's provider. If it fails before the first piece with an error <see cref="FallbackPolicy"/>
    /// allows (never after a cancel), the same request goes once to the configured fallback provider; its failure is final.
    /// </summary>
    private static async IAsyncEnumerable<string> AnswerAsync(
        PreparedRun run, Action<ProviderFallback>? onFallback, [EnumeratorCancellation] CancellationToken ct)
    {
        var enumerator = run.Primary.StreamAsync(run.RequestFor(run.Primary.Settings, usePromptModel: true), ct).GetAsyncEnumerator(ct);
        ILlmProvider? fallback = null;
        LlmException? failure = null;
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
                catch (LlmException ex) when (!yielded && !ct.IsCancellationRequested && FallbackPolicy.Applies(ex.Kind)
                    && run.Registry.GetFallback(run.Primary.Settings) is { } next)
                {
                    // Text that was already shown would be mixed with a second answer, so a switch only happens before the first piece.
                    failure = ex;
                    fallback = next;
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

        if (fallback is null) yield break;

        AppLog.Warn($"{failure!.Provider}: {failure.Kind} (HTTP {failure.StatusCode?.ToString() ?? "-"}), trying the fallback provider {fallback.Settings.Name} once.");
        run.Answering = fallback;
        onFallback?.Invoke(new ProviderFallback(run.Primary.Settings.Name, fallback.Settings.Name, failure.Kind));

        await foreach (var chunk in fallback.StreamAsync(run.RequestFor(fallback.Settings, usePromptModel: false), ct))
        {
            yield return chunk;
        }
    }

    /// <exception cref="MarkerException">A universal prompt found an unclosed or empty marker (nothing was sent).</exception>
    public async Task<string> RunAsync(
        PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null, Action<ProviderFallback>? onFallback = null)
    {
        var run = Prepare(prompt, input, timings);

        // The answer is collected completely before anything is returned: the field is only touched once the
        // provider reported a normal end, so a failure halfway never leaves half a text behind.
        var answer = new StringBuilder();
        await foreach (var chunk in AnswerAsync(run, onFallback, ct))
        {
            answer.Append(chunk);
        }

        var result = OutputCleaner.Clean(answer.ToString(), run.Input);
        if (string.IsNullOrWhiteSpace(result))
        {
            throw new LlmException(LlmErrorKind.EmptyResponse, run.Answering.Settings.Name, "the model returned no text");
        }

        return result;
    }

    /// <exception cref="MarkerException">A universal prompt found an unclosed or empty marker (nothing was sent).</exception>
    public async IAsyncEnumerable<string> StreamAsync(
        PromptDefinition prompt, string input, [EnumeratorCancellation] CancellationToken ct, LlmTimings? timings = null,
        Action<ProviderFallback>? onFallback = null)
    {
        var run = Prepare(prompt, input, timings);
        var cleaner = new StreamingOutputCleaner(run.Input);

        await foreach (var chunk in AnswerAsync(run, onFallback, ct))
        {
            var piece = cleaner.Push(chunk);
            if (piece.Length > 0) yield return piece;
        }

        // Only reached after a normal end of the answer; every other ending has thrown inside the loop above.
        var rest = cleaner.Finish();
        if (rest.Length > 0) yield return rest;

        if (!cleaner.HasOutput)
        {
            throw new LlmException(LlmErrorKind.EmptyResponse, run.Answering.Settings.Name, "the model returned no text");
        }
    }
}

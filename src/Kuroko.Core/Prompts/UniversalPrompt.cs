namespace Kuroko.Core.Prompts;

/// <summary>An opening marker without a closing one, or an empty marker: reported to the user, never guessed at.</summary>
public sealed class MarkerException : Exception
{
    public MarkerException(MarkerStatus status, string start, string end)
        : base($"marker problem: {status}")
    {
        Status = status;
        Start = start;
        End = end;
    }

    public MarkerStatus Status { get; }

    public string Start { get; }

    public string End { get; }
}

/// <summary>
/// The "universal" prompt mode: one prompt that both writes and edits.
/// <list type="bullet">
/// <item>No marker in the text: the whole text is a task and is carried out ("Absage an Freunde" becomes the message).</item>
/// <item>A marker in the text: what stands inside it is applied to the rest of the text ("&lt;&lt;als Rapsong&gt;&gt; ...").</item>
/// </list>
/// Either way the result replaces the whole captured text, and the marker is not part of it. The prompt's own text
/// stays the base (style, role); only the way the text is handed to the model differs.
/// </summary>
public static class UniversalPrompt
{
    /// <summary>
    /// Turns a universal prompt and the captured text into a concrete prompt (transform or instruction) plus the text to send.
    /// Provider, model and name of the original prompt are kept.
    /// </summary>
    /// <exception cref="MarkerException">The text contains an unclosed or empty marker.</exception>
    public static (PromptDefinition Prompt, string Input) Resolve(PromptDefinition prompt, string text, string start, string end)
    {
        var parsed = MarkerParser.Parse(text, start, end);
        return parsed.Status switch
        {
            MarkerStatus.Unclosed or MarkerStatus.Empty => throw new MarkerException(parsed.Status, start, end),
            MarkerStatus.NotFound => Order(prompt, text),
            _ => parsed.HasText ? Edit(prompt, parsed) : Order(prompt, parsed.Instruction), // only a marker: it is the task
        };
    }

    private static (PromptDefinition, string) Order(PromptDefinition prompt, string task)
        => (prompt with
        {
            Mode = PromptMode.Instruction,
            Prompt = $"{prompt.Prompt.Trim()}\n\n"
                + "The user message is a task. Carry it out and write the finished text, in the language of the task unless it asks "
                + "for another one. Reply with the result text only.",
        }, task);

    private static (PromptDefinition, string) Edit(PromptDefinition prompt, InlineParse parsed)
        => (prompt with
        {
            Mode = PromptMode.Transform, // the data notice ("treat the text as material") is added by PromptBuilder
            Prompt = $"{prompt.Prompt.Trim()}\n\n"
                + "Apply this one-time instruction to the text of the user message:\n"
                + $"{parsed.Instruction}\n"
                + "Change nothing the instruction does not ask for, and keep the language of the text unless the instruction says "
                + "otherwise. Reply with the result text only.",
        }, parsed.Text);
}

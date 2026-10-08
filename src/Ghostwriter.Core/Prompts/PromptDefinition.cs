namespace Ghostwriter.Core.Prompts;

public enum PromptMode
{
    /// <summary>The selected text (or the whole field) is the input the prompt works on.</summary>
    Transform,

    /// <summary>The text in the field is the instruction itself.</summary>
    Instruction,

    /// <summary>
    /// Both in one: without a marker the text is a task; with a marker, what stands inside it is applied to the
    /// remaining text (see <see cref="UniversalPrompt"/>).
    /// </summary>
    Universal,
}

/// <summary>One saved prompt. Mirrors an entry of prompts.toml.</summary>
public sealed record PromptDefinition(
    string Name,
    string Prompt,
    PromptMode Mode = PromptMode.Transform,
    string? Hotkey = null,
    string? Provider = null,
    string? Model = null);

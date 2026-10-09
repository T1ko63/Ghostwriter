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

/// <summary>Where the result of a prompt goes. Independent of <see cref="PromptMode"/>, which only describes the input.</summary>
public enum PromptOutput
{
    /// <summary>The result replaces the selected text (or the whole field) in the target app.</summary>
    Replace,

    /// <summary>The result is shown in a small card; the text in the target app is not touched.</summary>
    Overlay,
}

/// <summary>One saved prompt. Mirrors an entry of prompts.toml.</summary>
public sealed record PromptDefinition(
    string Name,
    string Prompt,
    PromptMode Mode = PromptMode.Transform,
    string? Hotkey = null,
    string? Provider = null,
    string? Model = null,
    PromptOutput Output = PromptOutput.Replace);

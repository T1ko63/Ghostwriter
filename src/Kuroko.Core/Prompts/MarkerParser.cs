using System.Text;

namespace Kuroko.Core.Prompts;

public enum MarkerStatus
{
    /// <summary>The text contains no marker at all.</summary>
    NotFound,

    /// <summary>At least one complete marker block was found and removed.</summary>
    Found,

    /// <summary>An opening marker without a closing one.</summary>
    Unclosed,

    /// <summary>A marker block with no instruction inside.</summary>
    Empty,
}

/// <summary>The result of splitting a text into one-time instruction(s) and the remaining text.</summary>
public sealed record InlineParse(MarkerStatus Status, string Instruction = "", string Text = "")
{
    /// <summary>False when only the instruction was written (e.g. "&lt;&lt;write a short note&gt;&gt;"): then it is an order, not an edit.</summary>
    public bool HasText => !string.IsNullOrWhiteSpace(Text);
}

/// <summary>
/// Finds inline instructions such as "&lt;&lt;make it shorter&gt;&gt;" in a text. The block may stand at the start, the
/// end or in the middle; it is removed from the text, the whitespace around it is tidied up.
/// </summary>
public static class MarkerParser
{
    public static InlineParse Parse(string input, string start, string end)
    {
        if (start.Length == 0 || end.Length == 0) return new InlineParse(MarkerStatus.NotFound);

        var instructions = new List<string>();
        var text = new StringBuilder();
        var position = 0;

        while (true)
        {
            var open = input.IndexOf(start, position, StringComparison.Ordinal);
            if (open < 0)
            {
                text.Append(input, position, input.Length - position);
                break;
            }

            var close = input.IndexOf(end, open + start.Length, StringComparison.Ordinal);
            if (close < 0) return new InlineParse(MarkerStatus.Unclosed);

            var instruction = input.Substring(open + start.Length, close - open - start.Length).Trim();
            if (instruction.Length == 0) return new InlineParse(MarkerStatus.Empty);
            instructions.Add(instruction);

            text.Append(input, position, open - position);
            position = SkipSeparator(input, close + end.Length, text);
        }

        if (instructions.Count == 0) return new InlineParse(MarkerStatus.NotFound);

        // A block at the very end leaves the whitespace that preceded it dangling.
        var result = text.ToString();
        if (position >= input.Length) result = result.TrimEnd();

        return new InlineParse(MarkerStatus.Found, string.Join("; ", instructions), result);
    }

    /// <summary>
    /// Decides how much of what follows a removed block goes with it, so no gap or empty line is left behind:
    /// a block alone on its line takes the line break, a block at the start takes the whitespace after it, and
    /// a block between two spaces takes one of them.
    /// </summary>
    private static int SkipSeparator(string input, int after, StringBuilder before)
    {
        var atStart = before.ToString().Trim().Length == 0;
        var onOwnLine = (before.Length == 0 || before[^1] == '\n') && (after >= input.Length || input[after] is '\n' or '\r');

        if (atStart && !onOwnLine)
        {
            // Everything before is blank: drop it and the whitespace after the block.
            before.Clear();
            while (after < input.Length && char.IsWhiteSpace(input[after])) after++;
            return after;
        }

        if (onOwnLine)
        {
            if (after < input.Length && input[after] == '\r') after++;
            if (after < input.Length && input[after] == '\n') after++;
            return after;
        }

        var spaceBefore = before.Length > 0 && before[^1] is ' ' or '\t';
        if (spaceBefore)
        {
            while (after < input.Length && input[after] is ' ' or '\t') after++;
        }

        return after;
    }
}

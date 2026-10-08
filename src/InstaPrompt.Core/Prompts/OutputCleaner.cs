namespace InstaPrompt.Core.Prompts;

/// <summary>Light clean-up of model output so it can replace the original text 1:1.</summary>
public static class OutputCleaner
{
    public static string Clean(string output, string input)
    {
        var result = output.Trim();

        // A model that wraps its answer in a code fence although the input had none.
        if (!input.Contains("```", StringComparison.Ordinal) && TryUnfence(result, out var unfenced))
        {
            result = unfenced;
        }

        if (result.Length == 0) return result;

        // Keep the surrounding whitespace of the original (e.g. a trailing newline) so the field looks unchanged.
        var leading = input[..(input.Length - input.TrimStart().Length)];
        var trailing = input[input.TrimEnd().Length..];
        return leading + result + trailing;
    }

    private static bool TryUnfence(string text, out string inner)
    {
        inner = text;
        if (!text.StartsWith("```", StringComparison.Ordinal) || !text.EndsWith("```", StringComparison.Ordinal) || text.Length < 6)
        {
            return false;
        }

        var firstNewline = text.IndexOf('\n');
        if (firstNewline < 0) return false;

        inner = text[(firstNewline + 1)..^3].Trim();
        return true;
    }
}

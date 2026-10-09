using System.Text;

namespace Ghostwriter.Core.Prompts;

/// <summary>
/// Cleans a streamed answer for display (result card), chunk by chunk. Everything already returned stays valid:
/// the returned pieces only ever get appended, nothing is taken back, so the text never jumps.
/// <list type="bullet">
/// <item>Leading whitespace is dropped, trailing whitespace is held back until more text follows (so the end is trimmed).</item>
/// <item>The whitespace of the input is NOT added again (see <see cref="OutputCleaner"/> for the replace path).</item>
/// <item>A code fence around the whole answer (only when the input has none) is removed once, at the end. While the
/// answer might still turn out to be fenced, nothing is shown: the text then appears in one piece when the answer is complete.</item>
/// </list>
/// The pieces together equal <c>OutputCleaner.Clean(answer, input)</c> without the whitespace taken over from the input.
/// </summary>
public sealed class StreamingOutputCleaner
{
    private const string Fence = "```";

    private enum Phase
    {
        /// <summary>Only whitespace so far, or a start that could still become a fence.</summary>
        Start,

        /// <summary>Normal text: pieces are returned as they come.</summary>
        Streaming,

        /// <summary>The answer starts with a fence: collect everything, decide at the end.</summary>
        Fenced,
    }

    private readonly bool _mayUnfence;
    private readonly StringBuilder _held = new();
    private Phase _phase = Phase.Start;

    /// <param name="input">The text that was sent to the model; a fence is only removed if the input had none.</param>
    public StreamingOutputCleaner(string input)
    {
        _mayUnfence = !input.Contains(Fence, StringComparison.Ordinal);
    }

    /// <summary>Whether any text has been returned so far (after <see cref="Finish"/>: whether the answer had any text at all).</summary>
    public bool HasOutput { get; private set; }

    /// <summary>Takes the next chunk of the answer; returns what may be shown now (often empty).</summary>
    public string Push(string chunk)
    {
        if (chunk.Length == 0) return string.Empty;
        _held.Append(chunk);

        if (_phase == Phase.Start)
        {
            var start = LeadingWhitespace(_held);
            if (start == _held.Length)
            {
                _held.Clear(); // whitespace only
                return string.Empty;
            }

            _held.Remove(0, start);
            if (_mayUnfence && CouldBeFence(_held))
            {
                if (_held.Length < Fence.Length) return string.Empty; // "`" or "``": too early to tell
                _phase = Phase.Fenced;
            }
            else
            {
                _phase = Phase.Streaming;
            }
        }

        return _phase == Phase.Streaming ? ReleaseTrimmed() : string.Empty;
    }

    /// <summary>Call once at the end of the answer; returns the rest (the whole text for a fenced answer).</summary>
    public string Finish()
    {
        var rest = string.Empty;
        if (_phase == Phase.Fenced || (_phase == Phase.Start && _held.Length > 0))
        {
            // Fenced answer, or a short start such as "``" that never became a fence.
            var text = _held.ToString().TrimEnd();
            rest = OutputCleaner.TryUnfence(text, out var inner) ? inner : text;
            HasOutput |= rest.Length > 0;
        }

        _held.Clear();
        _phase = Phase.Start;
        return rest;
    }

    /// <summary>Returns everything up to the last non-whitespace character; whitespace at the end stays in the buffer.</summary>
    private string ReleaseTrimmed()
    {
        var end = _held.Length;
        while (end > 0 && char.IsWhiteSpace(_held[end - 1])) end--;
        if (end == 0) return string.Empty;

        var piece = _held.ToString(0, end);
        _held.Remove(0, end);
        HasOutput = true;
        return piece;
    }

    private static int LeadingWhitespace(StringBuilder text)
    {
        var i = 0;
        while (i < text.Length && char.IsWhiteSpace(text[i])) i++;
        return i;
    }

    /// <summary>The buffer starts like a fence ("`", "``" or "```...").</summary>
    private static bool CouldBeFence(StringBuilder text)
    {
        var length = Math.Min(text.Length, Fence.Length);
        for (var i = 0; i < length; i++)
        {
            if (text[i] != '`') return false;
        }

        return true;
    }
}

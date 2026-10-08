using Tomlyn.Model;

namespace InstaPrompt.Core.Config;

/// <summary>Typed access to TOML values with clear error messages, plus best-effort line lookup (TomlTable carries no positions).</summary>
internal sealed class TomlReader
{
    private readonly string _file;
    private readonly string[] _lines;
    private readonly List<ConfigIssue> _issues;

    public TomlReader(string file, string text, List<ConfigIssue> issues)
    {
        _file = file;
        _lines = text.Split('\n');
        _issues = issues;
    }

    public void Issue(int? line, string message, bool isError, bool show = false) => _issues.Add(new ConfigIssue(_file, line, message, isError, show));

    /// <summary>
    /// First 1-based line in [from, to] that defines <paramref name="key"/> (or a table header containing it).
    /// </summary>
    public int? LineOf(string key, int from = 1, int to = int.MaxValue)
    {
        for (var i = Math.Max(0, from - 1); i < _lines.Length && i < to; i++)
        {
            var line = _lines[i].TrimStart();
            if (line.StartsWith(key + " ", StringComparison.Ordinal) || line.StartsWith(key + "=", StringComparison.Ordinal)
                || (line.StartsWith('[') && line.Contains(key, StringComparison.Ordinal)))
            {
                return i + 1;
            }
        }

        return null;
    }

    /// <summary>Lines of a table: from its header up to just before the next header. Header text is compared without spaces.</summary>
    public (int Start, int End)? Block(string header)
    {
        for (var i = 0; i < _lines.Length; i++)
        {
            if (Compact(_lines[i]) != header) continue;

            var end = i + 1;
            while (end < _lines.Length && !Compact(_lines[end]).StartsWith('[')) end++;
            return (i + 1, end); // 1-based, inclusive
        }

        return null;
    }

    /// <summary>1-based lines of all "[[name]]" array-table headers, in file order.</summary>
    public List<int> ArrayTableLines(string name)
    {
        var lines = new List<int>();
        for (var i = 0; i < _lines.Length; i++)
        {
            if (Compact(_lines[i]) == $"[[{name}]]") lines.Add(i + 1);
        }

        return lines;
    }

    /// <summary>The last line of the file (1-based).</summary>
    public int LastLine => _lines.Length;

    /// <summary>The line without blanks and without a trailing comment, so "[table]  # note" still counts as a header.</summary>
    private static string Compact(string line)
    {
        var compact = line.Replace(" ", string.Empty).Replace("\t", string.Empty).TrimEnd('\r');
        var comment = compact.IndexOf('#');
        return comment >= 0 ? compact[..comment] : compact;
    }

    public string Choice(TomlTable table, string key, string fallback, string[]? allowed, Func<string, int?>? line = null)
    {
        line ??= k => LineOf(k);
        if (!table.TryGetValue(key, out var value)) return fallback;
        if (value is not string text)
        {
            Issue(line(key), $"{key} must be text", true);
            return fallback;
        }

        if (allowed is not null && !allowed.Contains(text, StringComparer.OrdinalIgnoreCase))
        {
            Issue(line(key), $"{key} must be one of: {string.Join(", ", allowed)}", true);
            return fallback;
        }

        return allowed is null ? text : text.ToLowerInvariant();
    }

    public bool Bool(TomlTable table, string key, bool fallback, Func<string, int?>? line = null)
    {
        line ??= k => LineOf(k);
        if (!table.TryGetValue(key, out var value)) return fallback;
        if (value is bool flag) return flag;
        Issue(line(key), $"{key} must be true or false", true);
        return fallback;
    }

    public int Int(TomlTable table, string key, int fallback, Func<string, int?>? line = null)
    {
        line ??= k => LineOf(k);
        if (!table.TryGetValue(key, out var value)) return fallback;
        if (value is long number and >= int.MinValue and <= int.MaxValue) return (int)number;
        Issue(line(key), $"{key} must be a whole number", true);
        return fallback;
    }
}

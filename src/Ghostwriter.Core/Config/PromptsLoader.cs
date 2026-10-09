using Ghostwriter.Core.Hotkeys;
using Ghostwriter.Core.Prompts;
using Tomlyn;
using Tomlyn.Model;

namespace Ghostwriter.Core.Config;

public sealed record PromptsLoadResult(
    IReadOnlyList<PromptDefinition>? Prompts,
    IReadOnlyList<ConfigIssue> Issues,
    IReadOnlyDictionary<string, int> Lines)
{
    public bool Ok => Prompts is not null;
}

/// <summary>Reads and validates prompts.toml (a list of [[prompt]] tables).</summary>
public static class PromptsLoader
{
    public const string FileName = "prompts.toml";

    private static readonly string[] Modes = ["transform", "instruction", "universal"];

    private static readonly string[] Outputs = ["replace", "overlay"];

    private static readonly HashSet<string> PromptKeys = new(StringComparer.Ordinal)
    {
        "name", "prompt", "mode", "output", "hotkey", "provider", "model",
    };

    public static PromptsLoadResult Parse(string toml)
    {
        var issues = new List<ConfigIssue>();
        var lines = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(toml) ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            issues.Add(new ConfigIssue(FileName, ex.Line > 0 ? ex.Line : null, ex.Message, IsError: true));
            return new PromptsLoadResult(null, issues, lines);
        }

        var reader = new TomlReader(FileName, toml, issues);
        reader.WarnAboutDuplicateKeys();

        foreach (var key in root.Keys.Where(k => k != "prompt"))
        {
            reader.Issue(reader.LineOf(key), $"unknown entry '{key}' (prompts are written as [[prompt]])", isError: false);
        }

        var prompts = new List<PromptDefinition>();
        if (root.TryGetValue("prompt", out var value))
        {
            if (value is not TomlTableArray tables)
            {
                reader.Issue(reader.LineOf("prompt"), "prompts must be written as [[prompt]] (double brackets), one block per prompt", isError: true);
                return new PromptsLoadResult(null, issues, lines);
            }

            var headers = reader.ArrayTableLines("prompt");
            var index = 0;
            foreach (var table in tables)
            {
                var header = index < headers.Count ? headers[index] : (int?)null;
                var end = index + 1 < headers.Count ? headers[index + 1] - 1 : reader.LastLine;
                index++;
                if (ReadPrompt(table, reader, header, end) is { } prompt)
                {
                    if (lines.ContainsKey(prompt.Name))
                    {
                        reader.Issue(header, $"prompt name '{prompt.Name}' is used twice (names are not case-sensitive)", isError: true);
                        continue;
                    }

                    prompts.Add(prompt);
                    if (header is { } h) lines[prompt.Name] = h;
                }
            }
        }

        if (prompts.Count == 0 && !issues.Any(i => i.IsError))
        {
            reader.Issue(null, "no prompts defined", isError: false);
        }

        return new PromptsLoadResult(issues.Any(i => i.IsError) ? null : prompts, issues, lines);
    }

    private static PromptDefinition? ReadPrompt(TomlTable table, TomlReader reader, int? header, int end)
    {
        int? Line(string key) => header is { } start ? reader.LineOf(key, start, end) ?? start : null;
        var label = table.TryGetValue("name", out var n) && n is string s && s.Length > 0 ? $"prompt '{s}'" : "prompt";

        foreach (var key in table.Keys.Where(k => !PromptKeys.Contains(k)))
        {
            reader.Issue(Line(key), $"{label}: unknown setting '{key}'", isError: false);
        }

        var name = reader.Choice(table, "name", string.Empty, null, Line).Trim();
        if (name.Length == 0)
        {
            // A name of the wrong type was already reported by the reader; do not complain twice.
            if (!table.ContainsKey("name")) reader.Issue(header, "prompt without a name", isError: true);
            else if (table["name"] is string) reader.Issue(Line("name"), "prompt name must not be empty", isError: true);
            return null;
        }

        var text = reader.Choice(table, "prompt", string.Empty, null, Line);
        if (string.IsNullOrWhiteSpace(text))
        {
            reader.Issue(Line("prompt"), $"{label}: the prompt text is missing or empty", isError: true);
            return null;
        }

        var modeText = reader.Choice(table, "mode", "transform", Modes, Line);
        var mode = modeText switch
        {
            "instruction" => PromptMode.Instruction,
            "universal" => PromptMode.Universal,
            _ => PromptMode.Transform,
        };

        var output = reader.Choice(table, "output", "replace", Outputs, Line) == "overlay" ? PromptOutput.Overlay : PromptOutput.Replace;

        var hotkey = reader.Choice(table, "hotkey", string.Empty, null, Line).Trim();
        if (hotkey.Length > 0 && !HotkeyGesture.TryParse(hotkey, out _, out var hotkeyError))
        {
            reader.Issue(Line("hotkey"), $"{label}: {hotkeyError}", isError: true);
            return null;
        }

        var provider = reader.Choice(table, "provider", string.Empty, null, Line).Trim();
        var model = reader.Choice(table, "model", string.Empty, null, Line).Trim();

        return new PromptDefinition(
            name,
            text.Trim(),
            mode,
            hotkey.Length == 0 ? null : hotkey,
            provider.Length == 0 ? null : provider,
            model.Length == 0 ? null : model,
            output);
    }
}

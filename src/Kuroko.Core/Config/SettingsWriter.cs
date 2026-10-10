using System.Text;
using System.Text.RegularExpressions;

namespace Kuroko.Core.Config;

/// <summary>
/// Changes single top-level values in settings.toml as text, so the user's comments, ordering and
/// formatting survive (a serializer would rewrite the whole file).
/// </summary>
public static class SettingsWriter
{
    /// <summary>Returns the text with <c>key = value</c> set. Replaces an existing top-level line, keeping a trailing comment.</summary>
    public static string SetBool(string toml, string key, bool value)
    {
        var literal = value ? "true" : "false";
        var newline = toml.Contains("\r\n") ? "\r\n" : "\n";
        var lines = toml.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        // Top-level keys are everything before the first [table] header.
        var firstTable = lines.FindIndex(l => l.TrimStart().StartsWith('['));
        var topLevelEnd = firstTable < 0 ? lines.Count : firstTable;

        var pattern = new Regex($@"^(\s*{Regex.Escape(key)}\s*=\s*)(true|false)(\s*(#.*)?)$");
        for (var i = 0; i < topLevelEnd; i++)
        {
            var match = pattern.Match(lines[i]);
            if (!match.Success) continue;

            lines[i] = match.Groups[1].Value + literal + match.Groups[3].Value;
            return string.Join(newline, lines);
        }

        // Not there (deleted by the user): add it before the first table, or at the end.
        var insertAt = firstTable < 0 ? lines.Count : firstTable;
        while (insertAt > 0 && string.IsNullOrWhiteSpace(lines[insertAt - 1])) insertAt--; // keep blank lines after it
        lines.Insert(insertAt, $"{key} = {literal}");
        return string.Join(newline, lines);
    }

    /// <summary>
    /// Reads the file, sets the value and writes it back (UTF-8 without BOM). Returns false if nothing had to change.
    /// The new text goes to a temporary file first and then replaces the original in one step, so a crash or power
    /// loss while writing can never leave a half-written settings.toml behind.
    /// </summary>
    public static bool UpdateFile(string path, string key, bool value)
    {
        var text = File.ReadAllText(path);
        var updated = SetBool(text, key, value);
        if (updated == text) return false;

        var temp = path + ".tmp";
        try
        {
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(updated);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }

        return true;
    }
}

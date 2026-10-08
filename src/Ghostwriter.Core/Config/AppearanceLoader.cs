using Tomlyn.Model;

namespace Ghostwriter.Core.Config;

/// <summary>
/// Reads the [appearance] block. A bad value never fails the file: it is reported (as a warning that is shown to the
/// user, with file and line), the default is used for that value, and everything else stays as configured.
/// </summary>
internal static class AppearanceLoader
{
    private static readonly HashSet<string> Keys = new(StringComparer.Ordinal) { "transparency", "radius", "blur", "border", "dark", "light" };
    private static readonly HashSet<string> ColorKeys = new(StringComparer.Ordinal) { "background", "foreground", "selection" };

    public static AppearanceSettings Read(TomlTable root, TomlReader reader)
    {
        if (!root.TryGetValue("appearance", out var value)) return AppearanceSettings.Default;
        if (value is not TomlTable table)
        {
            Warn(reader, reader.LineOf("appearance"), "appearance must be a table ([appearance]); using the defaults");
            return AppearanceSettings.Default;
        }

        var block = reader.Block("[appearance]");
        int? Line(string key) => block is { } b ? reader.LineOf(key, b.Start, b.End) ?? b.Start : reader.LineOf(key);

        foreach (var key in table.Keys.Where(k => !Keys.Contains(k)))
        {
            Warn(reader, Line(key), $"[appearance] unknown setting '{key}'");
        }

        var transparency = ReadInt(table, "transparency", AppearanceSettings.DefaultTransparency, 0, 100, reader, Line);
        var radius = ReadInt(table, "radius", AppearanceSettings.DefaultRadius, 0, AppearanceSettings.MaxRadius, reader, Line);
        var blur = ReadBlur(table, reader, Line);
        var border = ReadBool(table, "border", AppearanceSettings.DefaultBorder, reader, Line);
        var dark = ReadColors("dark", table, AppearanceSettings.DefaultDark, reader);
        var light = ReadColors("light", table, AppearanceSettings.DefaultLight, reader);
        return new AppearanceSettings(transparency, radius, blur, dark, light, border);
    }

    private static void Warn(TomlReader reader, int? line, string message) => reader.Issue(line, message, isError: false, show: true);

    private static int ReadInt(TomlTable table, string key, int fallback, int min, int max, TomlReader reader, Func<string, int?> line)
    {
        if (!table.TryGetValue(key, out var value)) return fallback;
        if (value is not long number)
        {
            Warn(reader, line(key), $"[appearance] {key} must be a whole number between {min} and {max}; using {fallback}");
            return fallback;
        }

        if (number < min || number > max)
        {
            Warn(reader, line(key), $"[appearance] {key} must be between {min} and {max} (is {number}); using {fallback}");
            return fallback;
        }

        return (int)number;
    }

    private static bool ReadBool(TomlTable table, string key, bool fallback, TomlReader reader, Func<string, int?> line)
    {
        if (!table.TryGetValue(key, out var value)) return fallback;
        if (value is bool flag) return flag;

        Warn(reader, line(key), $"[appearance] {key} must be true or false; using {(fallback ? "true" : "false")}");
        return fallback;
    }

    private static BlurMode ReadBlur(TomlTable table, TomlReader reader, Func<string, int?> line)
    {
        if (!table.TryGetValue("blur", out var value)) return AppearanceSettings.DefaultBlur;
        switch ((value as string)?.Trim().ToLowerInvariant())
        {
            case "acrylic": return BlurMode.Acrylic;
            case "mica": return BlurMode.Mica;
            case "micaalt": return BlurMode.MicaAlt;
            case "blur": return BlurMode.Blur;
            case "none": return BlurMode.None;
            default:
                Warn(reader, line("blur"), "[appearance] blur must be \"acrylic\", \"mica\", \"micaalt\", \"blur\" or \"none\"; using \"acrylic\"");
                return AppearanceSettings.DefaultBlur;
        }
    }

    private static ThemeColors ReadColors(string theme, TomlTable appearance, ThemeColors defaults, TomlReader reader)
    {
        if (!appearance.TryGetValue(theme, out var value)) return defaults;

        var header = $"[appearance.{theme}]";
        if (value is not TomlTable table)
        {
            Warn(reader, reader.LineOf(theme), $"{header} must be a table; using the defaults");
            return defaults;
        }

        var block = reader.Block(header);
        int? Line(string key) => block is { } b ? reader.LineOf(key, b.Start, b.End) ?? b.Start : reader.LineOf(key);

        foreach (var key in table.Keys.Where(k => !ColorKeys.Contains(k)))
        {
            Warn(reader, Line(key), $"{header} unknown setting '{key}'");
        }

        var background = ReadColor(table, "background", defaults.Background, alphaAllowed: false, header, reader, Line);
        var foreground = ReadColor(table, "foreground", defaults.Foreground, alphaAllowed: false, header, reader, Line);
        var selection = ReadColor(table, "selection", defaults.Selection, alphaAllowed: true, header, reader, Line);
        return new ThemeColors(background, foreground, selection);
    }

    private static Rgba ReadColor(
        TomlTable table, string key, Rgba fallback, bool alphaAllowed, string header, TomlReader reader, Func<string, int?> line)
    {
        if (!table.TryGetValue(key, out var value)) return fallback;

        if (!ColorParser.TryParse(value as string, out var color, out var hasAlpha))
        {
            var format = alphaAllowed ? "#AARRGGBB, #RRGGBB or rgba(r, g, b, a)" : "#RRGGBB or rgb(r, g, b)";
            Warn(reader, line(key), $"{header} {key} is not a valid colour (expected {format}); using {Show(fallback, alphaAllowed)}");
            return fallback;
        }

        if (hasAlpha && !alphaAllowed)
        {
            // Transparency has exactly one place: [appearance] transparency.
            Warn(reader, line(key), $"{header} {key}: alpha ignored (use #RRGGBB or rgb(); the window's transparency is set by transparency)");
            return Rgba.Opaque(color.R, color.G, color.B);
        }

        return color;
    }

    private static string Show(Rgba color, bool withAlpha) => withAlpha ? color.ToString() : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}

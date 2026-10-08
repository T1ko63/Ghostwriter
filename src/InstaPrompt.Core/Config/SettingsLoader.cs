using System.Net;
using InstaPrompt.Core.Hotkeys;
using InstaPrompt.Core.Providers;
using Tomlyn;
using Tomlyn.Model;

namespace InstaPrompt.Core.Config;

/// <summary>Reads and validates settings.toml. Never throws for bad content: problems come back as <see cref="ConfigIssue"/>s.</summary>
public static class SettingsLoader
{
    public const string FileName = "settings.toml";

    private static readonly string[] Themes = ["system", "light", "dark"];
    private static readonly string[] Positions = ["caret", "mouse", "center"];
    private static readonly string[] Languages = ["auto", "de", "en"];
    private static readonly string[] Reasonings = ["off", "default", "low", "medium", "high"];

    private static readonly HashSet<string> TopLevelKeys = new(StringComparer.Ordinal)
    {
        "overlay_hotkey", "marker_start", "marker_end", "theme", "autostart", "overlay_position", "language",
        "idle_trim_seconds", "default_provider", "providers", "accent", "undo_hotkey", "undo_history",
        "appearance",
    };

    private static readonly HashSet<string> ProviderKeys = new(StringComparer.Ordinal)
    {
        "type", "base_url", "model", "api_key", "api_key_env", "timeout_seconds", "reasoning", "max_output_tokens",
    };

    public static SettingsLoadResult Parse(string toml, Func<string, string?> getEnv)
    {
        var issues = new List<ConfigIssue>();

        TomlTable root;
        try
        {
            root = TomlSerializer.Deserialize<TomlTable>(toml) ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            // Syntax error: the caller keeps the last working configuration.
            issues.Add(new ConfigIssue(FileName, ex.Line > 0 ? ex.Line : null, ex.Message, IsError: true));
            return new SettingsLoadResult(null, issues);
        }

        var reader = new TomlReader(FileName, toml, issues);

        foreach (var key in root.Keys.Where(k => !TopLevelKeys.Contains(k)))
        {
            reader.Issue(reader.LineOf(key), $"unknown setting '{key}'", isError: false);
        }

        var overlayHotkey = ReadHotkey(root, reader, "overlay_hotkey", AppSettings.DefaultOverlayHotkey);

        var markerStart = reader.Choice(root, "marker_start", "<<", null);
        var markerEnd = reader.Choice(root, "marker_end", ">>", null);
        if (markerStart.Length == 0 || markerEnd.Length == 0)
        {
            reader.Issue(reader.LineOf("marker_start"), "marker_start and marker_end must not be empty", isError: true);
        }

        var undoHotkey = ReadHotkey(root, reader, "undo_hotkey", AppSettings.DefaultUndoHotkey);
        var undoHistory = reader.Int(root, "undo_history", AppSettings.DefaultUndoHistory);
        if (undoHistory is < 0 or > AppSettings.MaxUndoHistory)
        {
            reader.Issue(reader.LineOf("undo_history"), $"undo_history must be between 0 and {AppSettings.MaxUndoHistory} (0 = off)", isError: true);
        }

        var theme = reader.Choice(root, "theme", "system", Themes);
        var accent = reader.Choice(root, "accent", "none", null).Trim();
        if (!IsValidAccent(accent))
        {
            reader.Issue(reader.LineOf("accent"), "accent must be \"none\", \"system\" or a hex colour like \"#3B82F6\"", isError: true);
        }

        var position = reader.Choice(root, "overlay_position", "caret", Positions);
        var language = reader.Choice(root, "language", "auto", Languages);
        var autostart = reader.Bool(root, "autostart", false);
        var idleTrim = reader.Int(root, "idle_trim_seconds", AppSettings.DefaultIdleTrimSeconds);
        if (idleTrim < 0)
        {
            reader.Issue(reader.LineOf("idle_trim_seconds"), "idle_trim_seconds must not be negative (0 = never)", isError: true);
        }

        var appearance = AppearanceLoader.Read(root, reader);

        var providers = new Dictionary<string, ProviderSettings>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetValue("providers", out var providersValue))
        {
            if (providersValue is TomlTable providerTable)
            {
                foreach (var (name, value) in providerTable)
                {
                    if (value is not TomlTable table)
                    {
                        reader.Issue(reader.LineOf(name), $"providers.{name} must be a table ([providers.{name}])", isError: true);
                        continue;
                    }

                    if (providers.ContainsKey(name))
                    {
                        reader.Issue(reader.LineOf(name), $"provider name '{name}' is used twice (names are not case-sensitive)", isError: true);
                        continue;
                    }

                    if (ReadProvider(name, table, reader, getEnv) is { } provider) providers[name] = provider;
                }
            }
            else
            {
                reader.Issue(reader.LineOf("providers"), "providers must contain tables ([providers.name])", isError: true);
            }
        }

        var defaultProvider = reader.Choice(root, "default_provider", string.Empty, null);
        if (defaultProvider.Length == 0)
        {
            defaultProvider = providers.Keys.FirstOrDefault() ?? string.Empty;
        }
        else if (!providers.ContainsKey(defaultProvider))
        {
            reader.Issue(reader.LineOf("default_provider"), $"default_provider '{defaultProvider}' is not defined under [providers.*]", isError: true);
        }

        if (providers.Count == 0)
        {
            reader.Issue(null, "no provider configured: add a [providers.name] section", isError: false);
        }

        var settings = new AppSettings(
            overlayHotkey, markerStart, markerEnd, theme, autostart, position, language, idleTrim,
            defaultProvider.Length == 0 ? null : defaultProvider, providers, accent.ToLowerInvariant(), undoHotkey, undoHistory)
        {
            Appearance = appearance,
        };
        return new SettingsLoadResult(issues.Any(i => i.IsError) ? null : settings, issues);
    }

    private static readonly System.Text.RegularExpressions.Regex HexColour =
        new("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>"none", "system" (case-insensitive) or #RGB / #RRGGBB / #AARRGGBB.</summary>
    public static bool IsValidAccent(string accent)
        => accent.Equals("none", StringComparison.OrdinalIgnoreCase)
           || accent.Equals("system", StringComparison.OrdinalIgnoreCase)
           || HexColour.IsMatch(accent);

    private static string ReadHotkey(TomlTable root, TomlReader reader, string key, string fallback)
    {
        var text = reader.Choice(root, key, fallback, null);
        if (!HotkeyGesture.TryParse(text, out _, out var error))
        {
            reader.Issue(reader.LineOf(key), $"{key}: {error}", isError: true);
        }

        return text;
    }

    private static ProviderSettings? ReadProvider(string name, TomlTable table, TomlReader reader, Func<string, string?> getEnv)
    {
        // Lines are looked up inside this provider's own block, so an error in the third provider points at the third provider.
        var block = reader.Block($"[providers.{name}]");
        int? Line(string key) => block is { } b ? reader.LineOf(key, b.Start, b.End) ?? b.Start : reader.LineOf(name);
        void Error(string key, string message) => reader.Issue(Line(key), $"[providers.{name}] {message}", isError: true);

        foreach (var key in table.Keys.Where(k => !ProviderKeys.Contains(k)))
        {
            reader.Issue(Line(key), $"[providers.{name}] unknown setting '{key}'", isError: false);
        }

        var typeText = reader.Choice(table, "type", string.Empty, null, Line);
        ProviderType? type = typeText switch
        {
            "openai" => ProviderType.OpenAi,
            "gemini" => ProviderType.Gemini,
            "anthropic" => ProviderType.Anthropic,
            "openai-compatible" => ProviderType.OpenAiCompatible,
            _ => null,
        };
        if (type is null)
        {
            Error("type", typeText.Length == 0
                ? "type is missing (openai, gemini, anthropic or openai-compatible)"
                : $"unknown type '{typeText}' (use openai, gemini, anthropic or openai-compatible)");
            return null;
        }

        var model = reader.Choice(table, "model", string.Empty, null, Line);
        if (model.Length == 0)
        {
            Error("model", "model is missing");
            return null;
        }

        var baseUrl = reader.Choice(table, "base_url", DefaultBaseUrl(type.Value), null, Line).TrimEnd('/');
        var failed = false;
        string? apiKey = table.TryGetValue("api_key", out var keyValue) ? keyValue as string : null;
        var keyEnvName = reader.Choice(table, "api_key_env", string.Empty, null, Line);
        if (string.IsNullOrWhiteSpace(apiKey) && keyEnvName.Length > 0)
        {
            apiKey = getEnv(keyEnvName);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                reader.Issue(Line("api_key_env"), $"[providers.{name}] environment variable {keyEnvName} is not set", isError: false);
            }
        }

        apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();
        if (apiKey is null && type != ProviderType.OpenAiCompatible && keyEnvName.Length == 0)
        {
            reader.Issue(Line("api_key"), $"[providers.{name}] no api_key or api_key_env set", isError: false);
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            Error("base_url", $"base_url '{baseUrl}' is not a valid http(s) address");
            failed = true;
        }
        else if (uri.Scheme == "http" && apiKey is not null && !IsLocalAddress(uri))
        {
            // An API key must never travel unencrypted to a remote host.
            Error("base_url", "base_url uses http:// for a remote host; use https:// when an API key is configured");
            failed = true;
        }

        var timeout = reader.Int(table, "timeout_seconds", 30, Line);
        if (timeout is < 1 or > 600)
        {
            Error("timeout_seconds", "timeout_seconds must be between 1 and 600");
            failed = true;
        }

        var reasoning = reader.Choice(table, "reasoning", "off", Reasonings, Line);
        var maxTokens = reader.Int(table, "max_output_tokens", 0, Line);
        if (maxTokens < 0)
        {
            Error("max_output_tokens", "max_output_tokens must not be negative (0 = automatic)");
            failed = true;
        }

        return failed ? null : new ProviderSettings(name, type.Value, baseUrl, model, apiKey, TimeSpan.FromSeconds(timeout), reasoning, maxTokens);
    }

    public static string DefaultBaseUrl(ProviderType type) => type switch
    {
        ProviderType.OpenAi => "https://api.openai.com/v1",
        ProviderType.Gemini => "https://generativelanguage.googleapis.com/v1beta",
        ProviderType.Anthropic => "https://api.anthropic.com",
        _ => "http://localhost:11434/v1",
    };

    private static bool IsLocalAddress(Uri uri)
    {
        if (uri.IsLoopback || uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) || !uri.Host.Contains('.')) return true;
        if (!IPAddress.TryParse(uri.Host, out var ip)) return false;
        var bytes = ip.GetAddressBytes();
        return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
            && (bytes[0] == 10 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31));
    }
}

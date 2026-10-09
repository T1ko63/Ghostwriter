using Ghostwriter.Core.Hotkeys;
using Ghostwriter.Core.Prompts;

namespace Ghostwriter.Core.Config;

/// <summary>Checks that need both files at once: providers named by prompts, hotkeys used twice.</summary>
public static class ConfigValidator
{
    public static List<ConfigIssue> Validate(
        AppSettings settings, IReadOnlyList<PromptDefinition> prompts, IReadOnlyDictionary<string, int>? promptLines = null)
    {
        var issues = new List<ConfigIssue>();
        int? LineOf(string promptName) => promptLines is not null && promptLines.TryGetValue(promptName, out var line) ? line : null;

        foreach (var prompt in prompts)
        {
            if (prompt.Provider is null) continue;

            if (!settings.Providers.TryGetValue(prompt.Provider, out var provider))
            {
                var known = settings.Providers.Count == 0 ? "none defined" : string.Join(", ", settings.Providers.Keys);
                issues.Add(new ConfigIssue(PromptsLoader.FileName, LineOf(prompt.Name),
                    $"prompt '{prompt.Name}': unknown provider '{prompt.Provider}' (defined in settings.toml: {known})", IsError: true));
            }
            else if (provider.ApiKey is null && provider.Type != Providers.ProviderType.OpenAiCompatible)
            {
                issues.Add(new ConfigIssue(PromptsLoader.FileName, LineOf(prompt.Name),
                    $"prompt '{prompt.Name}': provider '{provider.Name}' has no API key", IsError: false));
            }
        }

        // Every hotkey may be used once: the overlay, undo and result-copy hotkeys and the prompt hotkeys share one namespace.
        var owners = new Dictionary<HotkeyGesture, string>();
        void Claim(string hotkey, string owner, string file, int? line)
        {
            if (!HotkeyGesture.TryParse(hotkey, out var gesture, out _)) return; // syntax errors are reported by the loaders
            if (owners.TryGetValue(gesture, out var existing))
            {
                issues.Add(new ConfigIssue(file, line, $"hotkey {gesture} is used twice: {existing} and {owner}", IsError: true));
                return;
            }

            owners[gesture] = owner;
        }

        Claim(settings.OverlayHotkey, "overlay_hotkey", SettingsLoader.FileName, null);
        if (settings.UndoHistory > 0) Claim(settings.UndoHotkey, "undo_hotkey", SettingsLoader.FileName, null);
        Claim(settings.ResultCopyHotkey, "result_copy_hotkey", SettingsLoader.FileName, null);
        foreach (var prompt in prompts.Where(p => p.Hotkey is not null))
        {
            Claim(prompt.Hotkey!, $"prompt '{prompt.Name}'", PromptsLoader.FileName, LineOf(prompt.Name));
        }

        return issues;
    }
}

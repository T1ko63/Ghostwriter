using Kuroko.Core.Localization;
using Kuroko.Core.Providers;

namespace Kuroko.Core.Config;

/// <summary>
/// The one short hint shown at start: how to open the overlay on the very first run, and what to do when the default
/// provider has no API key (naming the environment variable it expects, and the Credential Manager as the other source).
/// </summary>
public static class StartupHint
{
    /// <returns>The text to show, or null when there is nothing to say.</returns>
    public static string? Build(AppSettings settings, bool firstRun)
    {
        var parts = new List<string>();
        if (firstRun) parts.Add(Loc.Get("first_run", settings.OverlayHotkey));

        if (settings.Providers.Count == 0) parts.Add(Loc.Get("hint_no_provider"));
        else if (MissingKey(settings) is { } provider) parts.Add(MissingKeyText(provider));

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }

    /// <summary>The default provider, if it needs a key and none was found in settings.toml, the environment or the key store.</summary>
    public static ProviderSettings? MissingKey(AppSettings settings)
        => settings.DefaultProvider is { } name && settings.Providers.TryGetValue(name, out var provider)
            && provider.ApiKey is null && provider.Type != ProviderType.OpenAiCompatible
                ? provider
                : null;

    public static string MissingKeyText(ProviderSettings provider) => provider.ApiKeyEnv is { } variable
        ? Loc.Get("hint_no_key_env", provider.Name, variable)
        : Loc.Get("hint_no_key", provider.Name);
}

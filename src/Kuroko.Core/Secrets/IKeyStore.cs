namespace Kuroko.Core.Secrets;

/// <summary>
/// A place for API keys outside settings.toml (on Windows: the Credential Manager), one entry per provider name.
/// Implementations never throw and never log or return a key anywhere but from <see cref="Read"/>.
/// </summary>
public interface IKeyStore
{
    /// <returns>The stored key, or null if there is none or it cannot be read.</returns>
    string? Read(string provider);

    /// <returns>False if the key could not be stored.</returns>
    bool Save(string provider, string key);

    /// <returns>True if there is no stored key afterwards (also when there was none).</returns>
    bool Remove(string provider);
}

public static class KeyStoreNames
{
    /// <summary>Prefix of every entry, so Kuroko's keys are easy to find in the Credential Manager.</summary>
    public const string Prefix = "Kuroko:";

    /// <summary>Name of the entry for a provider, e.g. "Kuroko:gemini" for [providers.gemini].</summary>
    public static string Target(string provider) => Prefix + provider;
}

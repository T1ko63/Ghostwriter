using Kuroko.Core.Config;
using Kuroko.Core.Providers;
using Kuroko.Core.Secrets;

namespace Kuroko.Tests;

public class KeyStoreTests
{
    private sealed class FakeKeyStore : IKeyStore
    {
        public Dictionary<string, string> Keys { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Reads { get; } = [];

        public string? Read(string provider)
        {
            Reads.Add(provider);
            return Keys.GetValueOrDefault(provider);
        }

        public bool Save(string provider, string key)
        {
            Keys[provider] = key;
            return true;
        }

        public bool Remove(string provider) => Keys.Remove(provider) || true;
    }

    private const string GeminiWithEnv = "[providers.gemini]\ntype = \"gemini\"\nmodel = \"m\"\napi_key_env = \"GEMINI_API_KEY\"\n";
    private const string GeminiWithoutKey = "[providers.gemini]\ntype = \"gemini\"\nmodel = \"m\"\n";

    private static string? NoEnv(string name) => null;

    private static string? Env(string name) => name == "GEMINI_API_KEY" ? "env-key" : null;

    [Fact]
    public void Entry_names_carry_the_app_prefix()
    {
        Assert.Equal("Kuroko:gemini", KeyStoreNames.Target("gemini"));
    }

    [Fact]
    public void Environment_variable_wins_over_the_stored_key()
    {
        var store = new FakeKeyStore { Keys = { ["gemini"] = "stored-key" } };
        var provider = SettingsLoader.Parse(GeminiWithEnv, Env, store).Settings!.Providers["gemini"];

        Assert.Equal("env-key", provider.ApiKey);
        Assert.Equal(ApiKeySource.Environment, provider.KeySource);
        Assert.Equal("GEMINI_API_KEY", provider.ApiKeyEnv);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public void Stored_key_is_used_when_the_environment_variable_is_not_set()
    {
        var store = new FakeKeyStore { Keys = { ["gemini"] = "  stored-key " } };
        var result = SettingsLoader.Parse(GeminiWithEnv, NoEnv, store);
        var provider = result.Settings!.Providers["gemini"];

        Assert.Equal("stored-key", provider.ApiKey);
        Assert.Equal(ApiKeySource.CredentialManager, provider.KeySource);
        Assert.DoesNotContain(result.Issues, i => i.Message.Contains("GEMINI_API_KEY"));
    }

    [Fact]
    public void Stored_key_works_without_any_key_setting_in_the_file()
    {
        var store = new FakeKeyStore { Keys = { ["gemini"] = "stored-key" } };
        var result = SettingsLoader.Parse(GeminiWithoutKey, NoEnv, store);

        Assert.Equal("stored-key", result.Settings!.Providers["gemini"].ApiKey);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Key_in_the_file_wins_over_everything()
    {
        var store = new FakeKeyStore { Keys = { ["gemini"] = "stored-key" } };
        var toml = "[providers.gemini]\ntype = \"gemini\"\nmodel = \"m\"\napi_key = \"file-key\"\napi_key_env = \"GEMINI_API_KEY\"\n";
        var provider = SettingsLoader.Parse(toml, Env, store).Settings!.Providers["gemini"];

        Assert.Equal("file-key", provider.ApiKey);
        Assert.Equal(ApiKeySource.File, provider.KeySource);
        Assert.Empty(store.Reads);
    }

    [Fact]
    public void No_key_anywhere_is_a_warning_naming_the_store_entry()
    {
        var result = SettingsLoader.Parse(GeminiWithEnv, NoEnv, new FakeKeyStore());

        Assert.True(result.Ok);
        Assert.Null(result.Settings!.Providers["gemini"].ApiKey);
        Assert.Equal(ApiKeySource.None, result.Settings.Providers["gemini"].KeySource);
        var warning = Assert.Single(result.Issues);
        Assert.False(warning.IsError);
        Assert.Contains("GEMINI_API_KEY", warning.Message);
        Assert.Contains("Kuroko:gemini", warning.Message);
    }

    [Fact]
    public void Without_a_store_the_old_behaviour_stays()
    {
        var result = SettingsLoader.Parse(GeminiWithEnv, NoEnv);
        Assert.Contains(result.Issues, i => i.Message.EndsWith("environment variable GEMINI_API_KEY is not set", StringComparison.Ordinal));
    }

    [Fact]
    public void Stored_key_is_never_sent_over_plain_http_to_a_remote_host()
    {
        var store = new FakeKeyStore { Keys = { ["x"] = "stored-key" } };
        var toml = "[providers.x]\ntype = \"openai-compatible\"\nbase_url = \"http://api.example.com/v1\"\nmodel = \"m\"\n";
        Assert.False(SettingsLoader.Parse(toml, NoEnv, store).Ok);
    }

    [Fact]
    public void Provider_settings_never_print_the_key()
    {
        var provider = new ProviderSettings("p", ProviderType.Gemini, "https://g.test", "m", "secret-key", TimeSpan.FromSeconds(30));
        var text = provider.ToString();

        Assert.DoesNotContain("secret-key", text);
        Assert.Contains("ApiKey = ***", text);
    }

    [Fact]
    public void Config_manager_picks_up_a_newly_stored_key_on_a_forced_reload()
    {
        var dir = Path.Combine(Path.GetTempPath(), "kuroko-keys-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, SettingsLoader.FileName), GeminiWithEnv);
            File.WriteAllText(Path.Combine(dir, PromptsLoader.FileName), "[[prompt]]\nname = \"Fix\"\nprompt = \"fix it\"\n");
            var store = new FakeKeyStore();
            var manager = new ConfigManager(dir, NoEnv, store);
            manager.Load();
            Assert.Null(manager.Current.Settings.Providers["gemini"].ApiKey);

            store.Save("gemini", "stored-key");
            var result = manager.Reload(force: true);

            Assert.True(result.SettingsChanged);
            Assert.Equal("stored-key", manager.Current.Settings.Providers["gemini"].ApiKey);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

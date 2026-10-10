using System.Windows;
using System.Windows.Controls;
using Kuroko.Core.Localization;
using Kuroko.Core.Providers;
using Kuroko.Core.Secrets;

namespace Kuroko.App.Keys;

/// <summary>
/// Stores or removes a provider's API key in the key store (Windows Credential Manager). The key only ever lives in the
/// PasswordBox and the store; it is never shown, logged or written to settings.toml.
/// </summary>
public partial class ApiKeysWindow : Window
{
    private readonly IKeyStore _store;
    private readonly Func<IReadOnlyDictionary<string, ProviderSettings>> _providers;
    private readonly Action _changed;

    /// <param name="providers">The providers as currently loaded (called again after every change).</param>
    /// <param name="changed">Reloads the configuration so a new or removed key takes effect at once.</param>
    public ApiKeysWindow(IKeyStore store, Func<IReadOnlyDictionary<string, ProviderSettings>> providers, Action changed)
    {
        _store = store;
        _providers = providers;
        _changed = changed;
        InitializeComponent();

        Title = Loc.Get("keys_title");
        Intro.Text = Loc.Get("keys_intro");
        ProviderLabel.Text = Loc.Get("keys_provider");
        KeyLabel.Text = Loc.Get("keys_key");
        SaveButton.Content = Loc.Get("keys_save");
        RemoveButton.Content = Loc.Get("keys_remove");
        CloseButton.Content = Loc.Get("keys_close");

        var names = providers().Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();
        Providers.ItemsSource = names;
        if (names.Count == 0)
        {
            Source.Text = Loc.Get("keys_none");
            SaveButton.IsEnabled = RemoveButton.IsEnabled = Key.IsEnabled = Providers.IsEnabled = false;
        }
        else
        {
            Providers.SelectedIndex = 0;
        }

        Loaded += (_, _) => Key.Focus();
        Closed += (_, _) => Key.Clear();
    }

    private string? Selected => Providers.SelectedItem as string;

    private void OnProviderChanged(object sender, SelectionChangedEventArgs e)
    {
        Feedback.Visibility = Visibility.Collapsed;
        ShowSource();
    }

    private void ShowSource()
    {
        if (Selected is not { } name || !_providers().TryGetValue(name, out var provider))
        {
            Source.Text = string.Empty;
            return;
        }

        Source.Text = provider.KeySource switch
        {
            ApiKeySource.File => Loc.Get("keys_src_file"),
            ApiKeySource.Environment => Loc.Get("keys_src_env", provider.ApiKeyEnv ?? string.Empty),
            ApiKeySource.CredentialManager => Loc.Get("keys_src_store", KeyStoreNames.Target(name)),
            _ => Loc.Get("keys_src_none"),
        };
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } name) return;
        if (string.IsNullOrWhiteSpace(Key.Password))
        {
            ShowFeedback(Loc.Get("keys_empty"));
            return;
        }

        var ok = _store.Save(name, Key.Password);
        Key.Clear();
        Done(ok, Loc.Get("keys_saved", name));
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } name) return;
        Done(_store.Remove(name), Loc.Get("keys_removed", name));
    }

    private void Done(bool ok, string message)
    {
        if (ok) _changed();
        ShowFeedback(ok ? message : Loc.Get("keys_failed"));
        ShowSource();
    }

    private void ShowFeedback(string text)
    {
        Feedback.Text = text;
        Feedback.Visibility = Visibility.Visible;
    }
}

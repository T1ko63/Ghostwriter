namespace Kuroko.Core.Providers;

/// <summary>The configured providers, created on demand and sharing one HttpClient (so connections can be reused and warmed up).</summary>
public sealed class ProviderRegistry : IDisposable
{
    private readonly Dictionary<string, ProviderSettings> _settings;
    private readonly Dictionary<string, ILlmProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly object _gate = new();

    public ProviderRegistry(IReadOnlyDictionary<string, ProviderSettings> settings, string? defaultProvider, HttpClient? http = null)
    {
        _settings = new Dictionary<string, ProviderSettings>(settings, StringComparer.OrdinalIgnoreCase);
        DefaultProvider = defaultProvider;
        _http = http ?? LlmHttp.CreateClient();
        _ownsHttp = http is null;
    }

    public string? DefaultProvider { get; }

    /// <summary>Returns the named provider, or the default one for null.</summary>
    public ILlmProvider Get(string? name)
    {
        name ??= DefaultProvider;
        if (string.IsNullOrEmpty(name))
        {
            throw new LlmException(LlmErrorKind.Config, "-", "no provider configured (see settings.toml)");
        }

        lock (_gate)
        {
            if (_providers.TryGetValue(name, out var existing)) return existing;
            if (!_settings.TryGetValue(name, out var settings))
            {
                throw new LlmException(LlmErrorKind.Config, name, $"unknown provider '{name}'");
            }

            var created = Create(settings, _http);
            _providers[name] = created;
            return created;
        }
    }

    public static ILlmProvider Create(ProviderSettings settings, HttpClient http) => settings.Type switch
    {
        ProviderType.OpenAi => new OpenAiProvider(settings, http),
        ProviderType.Gemini => new GeminiProvider(settings, http),
        ProviderType.Anthropic => new AnthropicProvider(settings, http),
        ProviderType.OpenAiCompatible => new OpenAiCompatibleProvider(settings, http),
        _ => throw new LlmException(LlmErrorKind.Config, settings.Name, $"unsupported provider type {settings.Type}"),
    };

    /// <summary>Opens the connection of a provider in the background. Safe to call often; never throws.</summary>
    public Task WarmUpAsync(string? name = null)
    {
        try
        {
            return Get(name).WarmUpAsync();
        }
        catch (LlmException)
        {
            return Task.CompletedTask;
        }
    }

    public void Dispose()
    {
        if (_ownsHttp) _http.Dispose();
    }
}

using System.Text;
using Kuroko.Core.Prompts;
using Kuroko.Core.Secrets;

namespace Kuroko.Core.Config;

/// <summary>The settings and prompts that are active together.</summary>
public sealed record ConfigSnapshot(AppSettings Settings, IReadOnlyList<PromptDefinition> Prompts);

/// <summary>Outcome of (re)reading the config files.</summary>
public sealed record ReloadResult(ConfigSnapshot Snapshot, bool SettingsChanged, bool PromptsChanged, IReadOnlyList<ConfigIssue> Issues)
{
    public bool HasErrors => Issues.Any(i => i.IsError);

    public bool Changed => SettingsChanged || PromptsChanged;
}

/// <summary>
/// Owns the active configuration. Reading never throws and never makes things worse: a file with a syntax or
/// validation error is rejected as a whole and the last working version of it stays active, while the other
/// file can still be applied (as long as the two fit together).
/// </summary>
public sealed class ConfigManager
{
    private readonly Func<string, string?> _getEnv;
    private readonly IKeyStore? _keyStore;
    private readonly object _gate = new();
    private ConfigSnapshot _current = new(AppSettings.Fallback, BuiltInPrompts.All);
    private string? _settingsText;
    private string? _promptsText;
    private IReadOnlyDictionary<string, int> _promptLines = new Dictionary<string, int>();

    public ConfigManager(string directory, Func<string, string?> getEnv, IKeyStore? keyStore = null)
    {
        Directory = directory;
        _getEnv = getEnv;
        _keyStore = keyStore;
    }

    public string Directory { get; }

    public string SettingsPath => Path.Combine(Directory, SettingsLoader.FileName);

    public string PromptsPath => Path.Combine(Directory, PromptsLoader.FileName);

    public ConfigSnapshot Current => Volatile.Read(ref _current);

    /// <summary>First start: creates missing files with defaults, then reads both.</summary>
    public ReloadResult Load()
    {
        var issues = new List<ConfigIssue>();
        try
        {
            DefaultSettings.EnsureExists(SettingsPath, _getEnv);
            DefaultPrompts.EnsureExists(PromptsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            issues.Add(new ConfigIssue(Directory, null, $"cannot create the default config files: {ex.Message}", IsError: true));
        }

        var result = Reload(force: true);
        return issues.Count == 0 ? result : result with { Issues = issues.Concat(result.Issues).ToList() };
    }

    /// <param name="force">Parse again even if a file is byte-identical to the last applied one (e.g. to pick up a new environment variable).</param>
    public ReloadResult Reload(bool force = false)
    {
        lock (_gate)
        {
            var issues = new List<ConfigIssue>();
            var old = _current;

            var settingsText = ReadFile(SettingsPath, SettingsLoader.FileName, issues);
            var promptsText = ReadFile(PromptsPath, PromptsLoader.FileName, issues);

            AppSettings? newSettings = null;
            if (settingsText is not null)
            {
                if (!force && settingsText == _settingsText)
                {
                    newSettings = old.Settings; // unchanged: keep the very same object so nothing is flagged as changed
                }
                else
                {
                    var parsed = SettingsLoader.Parse(settingsText, _getEnv, _keyStore);
                    issues.AddRange(parsed.Issues);
                    newSettings = parsed.Settings;
                }
            }

            IReadOnlyList<PromptDefinition>? newPrompts = null;
            IReadOnlyDictionary<string, int> newLines = _promptLines;
            if (promptsText is not null)
            {
                if (!force && promptsText == _promptsText)
                {
                    newPrompts = old.Prompts;
                }
                else
                {
                    var parsed = PromptsLoader.Parse(promptsText);
                    issues.AddRange(parsed.Issues);
                    newPrompts = parsed.Prompts;
                    newLines = parsed.Lines;
                }
            }

            // Which of the new files go live together? Both, if they fit; otherwise as much as still fits.
            var useSettings = newSettings is not null;
            var usePrompts = newPrompts is not null;
            var cross = ConfigValidator.Validate(newSettings ?? old.Settings, newPrompts ?? old.Prompts, newLines);
            issues.AddRange(cross);

            if (cross.Any(i => i.IsError))
            {
                if (useSettings && usePrompts)
                {
                    if (!HasErrors(ConfigValidator.Validate(newSettings!, old.Prompts, _promptLines))) usePrompts = false;
                    else if (!HasErrors(ConfigValidator.Validate(old.Settings, newPrompts!, newLines))) useSettings = false;
                    else (useSettings, usePrompts) = (false, false);
                }
                else
                {
                    // Only one new file and it does not fit the other: reject it.
                    (useSettings, usePrompts) = (false, false);
                }
            }

            var settings = useSettings ? newSettings! : old.Settings;
            var prompts = usePrompts ? newPrompts! : old.Prompts;
            var settingsChanged = useSettings && !ReferenceEquals(settings, old.Settings);
            var promptsChanged = usePrompts && !ReferenceEquals(prompts, old.Prompts);

            if (useSettings) _settingsText = settingsText;
            if (usePrompts)
            {
                _promptsText = promptsText;
                _promptLines = newLines;
            }

            var snapshot = new ConfigSnapshot(settings, prompts);
            Volatile.Write(ref _current, snapshot);
            return new ReloadResult(snapshot, settingsChanged, promptsChanged, issues);
        }
    }

    private static bool HasErrors(IEnumerable<ConfigIssue> issues) => issues.Any(i => i.IsError);

    /// <summary>Reads a file the way editors leave it: shared access, a few retries for half-written saves.</summary>
    private static string? ReadFile(string path, string name, List<ConfigIssue> issues)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (FileNotFoundException)
            {
                issues.Add(new ConfigIssue(name, null, "file not found; the previous configuration stays active", IsError: true));
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                issues.Add(new ConfigIssue(name, null, "file not found; the previous configuration stays active", IsError: true));
                return null;
            }
            catch (IOException) when (attempt < 5)
            {
                Thread.Sleep(40); // the editor may still hold the file
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                issues.Add(new ConfigIssue(name, null, $"cannot be read ({ex.Message}); the previous configuration stays active", IsError: true));
                return null;
            }
        }
    }
}

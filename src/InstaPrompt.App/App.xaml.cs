using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using InstaPrompt.App.Overlay;
using InstaPrompt.App.Themes;
using InstaPrompt.Core.Config;
using InstaPrompt.Core.Diagnostics;
using InstaPrompt.Core.Hotkeys;
using InstaPrompt.Core.Localization;
using InstaPrompt.Core.Prompts;
using InstaPrompt.Core.Providers;
using InstaPrompt.Platform.Autostart;
using InstaPrompt.Platform.Hotkeys;
using InstaPrompt.Platform.Native;
using InstaPrompt.Platform.TextIntegration;
using InstaPrompt.Platform.Tray;

namespace InstaPrompt.App;

public partial class App : Application
{
    private static readonly TimeSpan ReloadQuietPeriod = TimeSpan.FromMilliseconds(300);

    private readonly string _systemLanguage = Loc.Language;
    private readonly AutostartService _autostart = new();
    private string _configDir = string.Empty;
    private ConfigManager? _config;
    private ConfigWatcher? _watcher;
    private HttpClient? _http;
    private ProviderRegistry? _providers;
    private AppController? _controller;
    private Mutex? _singleInstance;
    private MessageWindow? _window;
    private ClipboardService? _clipboard;
    private HotkeyManager? _hotkeys;
    private TrayIcon? _tray;
    private ThemeService? _theme;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A name that cannot clash with another tool called "InstaPrompt" (which would make one of the two exit at start).
        // With INSTAPROMPT_CONFIG_DIR set (tests, a second copy side by side) the lock is per config folder.
        var mutexName = @"Local\InstaPrompt.Claude.5c0e7a52-9f3b-4d86-8f41-2b6d1e0a9c37";
        if (Environment.GetEnvironmentVariable("INSTAPROMPT_CONFIG_DIR") is { Length: > 0 } otherDir)
        {
            mutexName += "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(otherDir).ToLowerInvariant())))[..16];
        }

        _singleInstance = new Mutex(true, mutexName, out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        // INSTAPROMPT_CONFIG_DIR points the app at another folder (tests, or a second copy side by side).
        _configDir = Environment.GetEnvironmentVariable("INSTAPROMPT_CONFIG_DIR")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "InstaPrompt");
        AppLog.Init(_configDir);
        var started = Stopwatch.GetTimestamp();
        AppLog.Info($"Start, elevated={TargetInfo.SelfIsElevated}");
        InstallCrashGuards();

        // UI Automation takes a while to load; start that right away on a pool thread, in parallel to everything below.
        TextAccessService.Warmup();

        var lapStart = started;
        var laps = new List<string>();
        void Lap(string name)
        {
            var now = Stopwatch.GetTimestamp();
            laps.Add($"{name} {Stopwatch.GetElapsedTime(lapStart, now).TotalMilliseconds:F0}");
            lapStart = now;
        }

        // Task.Delay and waits get 1 ms resolution instead of ~15 ms.
        NativeTimer.BeginHighResolution();

        // Both files are read before any window exists; a broken file never prevents the start.
        _config = new ConfigManager(_configDir, Environment.GetEnvironmentVariable);
        var firstRun = !File.Exists(_config.SettingsPath);
        var loaded = _config.Load();
        LogIssues(loaded);
        Lap("config");

        _theme = new ThemeService(this);
        _window = new MessageWindow();
        _clipboard = new ClipboardService(_window);
        _hotkeys = new HotkeyManager(_window);
        _tray = new TrayIcon(_window, "InstaPrompt") { MenuProvider = BuildMenu };
        _http = LlmHttp.CreateClient();
        Lap("tray+hotkeys+http");

        // Create both windows once and keep them hidden; showing them later is then instant.
        var overlay = new OverlayWindow();
        var status = new StatusWindow();
        Lap("create windows");
        ApplyTheme(_config.Current.Settings);
        Lap("theme");
        overlay.Warmup(_config.Current.Prompts);
        Lap("overlay warm-up");
        status.Warmup();
        Lap("status warm-up");
        _theme.Changed += () => ApplyLook(overlay, status);
        ApplyLook(overlay, status);

        _providers = new ProviderRegistry(_config.Current.Settings.Providers, _config.Current.Settings.DefaultProvider, _http);
        // Marker characters are read per run, so changing them in settings.toml takes effect without a restart.
        var runner = new ProviderPromptRunner(
            () => _providers!,
            () => (_config.Current.Settings.MarkerStart, _config.Current.Settings.MarkerEnd));
        _controller = new AppController(
            new TextAccessService(_clipboard), overlay, status, runner,
            prompts: () => _config.Current.Prompts,
            warmUp: () => _ = _providers!.WarmUpAsync());

        ApplyConfig(loaded, initial: true);
        Lap("apply config+hotkeys");
        if (loaded.HasErrors || loaded.Issues.Any(i => i.Show)) ReportIssues(loaded, manual: false);
        else ShowStartupHint(firstRun);

        // From here on, edits to settings.toml / prompts.toml are picked up while the app runs.
        _watcher = new ConfigWatcher(_configDir, ReloadQuietPeriod, SettingsLoader.FileName, PromptsLoader.FileName);
        _watcher.Changed += () => Dispatcher.BeginInvoke(() => ReloadConfig(manual: false));

        AppLog.Info($"Ready after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms, {_config.Current.Prompts.Count} prompts "
            + $"(phases in ms: {string.Join(", ", laps)})");
        InstaPrompt.App.Diagnostics.MemoryDiagnostics.StartIfRequested();
        StartIdleTrimming();
    }

    // ---- idle memory trimming ----

    private System.Windows.Threading.DispatcherTimer? _trimTimer;
    private long _trimmedAtActivity = -1;

    /// <summary>
    /// After a quiet period (idle_trim_seconds in settings.toml) the working set is trimmed once. Only done while
    /// nothing is going on, and only once per quiet period. INSTAPROMPT_TRIM_SECONDS overrides the setting (tests).
    /// </summary>
    private void StartIdleTrimming()
    {
        _trimTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _trimTimer.Tick += (_, _) =>
        {
            var seconds = int.TryParse(Environment.GetEnvironmentVariable("INSTAPROMPT_TRIM_SECONDS"), out var forced)
                ? forced
                : _config!.Current.Settings.IdleTrimSeconds;
            if (seconds <= 0) return; // read on every tick, so a reloaded setting takes effect

            var controller = _controller!;
            var idle = Environment.TickCount64 - controller.LastActivityTick;
            if (controller.IsBusy || idle < seconds * 1000L || _trimmedAtActivity == controller.LastActivityTick) return;

            _trimmedAtActivity = controller.LastActivityTick;
            var (before, after) = InstaPrompt.Platform.Native.WorkingSetTrimmer.Trim();
            AppLog.Info($"Idle for {idle / 1000} s: working set trimmed {before:F0} MB -> {after:F0} MB");
        };
        _trimTimer.Start();
    }

    // ---- configuration ----

    private void ReloadConfig(bool manual)
    {
        var result = _config!.Reload(force: manual);
        LogIssues(result);
        ApplyConfig(result, initial: false);
        ReportIssues(result, manual);
        if (result.Changed) AppLog.Info($"Configuration reloaded: settings changed={result.SettingsChanged}, prompts changed={result.PromptsChanged}");
    }

    /// <summary>Puts a new snapshot into effect: theme, language, providers, and all hotkeys.</summary>
    private void ApplyConfig(ReloadResult result, bool initial)
    {
        var settings = _config!.Current.Settings;
        var prompts = _config.Current.Prompts;

        if (initial || result.SettingsChanged)
        {
            Loc.Language = settings.Language.Equals("auto", StringComparison.OrdinalIgnoreCase) ? _systemLanguage : settings.Language;
            ApplyTheme(settings);
            _controller!.History.Capacity = settings.UndoHistory;
            _controller.Position = Enum.TryParse<OverlayPosition>(settings.OverlayPosition, ignoreCase: true, out var position)
                ? position
                : OverlayPosition.Caret;

            // A fresh registry for the new provider settings; the HttpClient (and its warm connections) is kept.
            _providers = new ProviderRegistry(settings.Providers, settings.DefaultProvider, _http);
            _ = _providers.WarmUpAsync();
        }

        if (initial || result.Changed) RegisterHotkeys(settings, prompts);

        if (initial || result.SettingsChanged)
        {
            _tray!.SetTooltip($"InstaPrompt - {settings.OverlayHotkey}");

            // Only a configuration read from a valid file may switch autostart: a broken settings.toml must never
            // silently turn it off.
            if (!ReferenceEquals(settings, AppSettings.Fallback) && Environment.ProcessPath is { } exe
                && !_autostart.Apply(settings.Autostart, exe))
            {
                _controller!.ShowNotice(Loc.Get("autostart_failed"));
            }
        }
    }

    /// <summary>
    /// One short hint at start: how to open the overlay on the very first run, and a missing API key for the default provider.
    /// </summary>
    private void ShowStartupHint(bool firstRun)
    {
        var settings = _config!.Current.Settings;
        var provider = settings.DefaultProvider is { } name && settings.Providers.TryGetValue(name, out var p) ? p : null;
        var keyMissing = provider is { ApiKey: null } && provider.Type != ProviderType.OpenAiCompatible;
        var noProvider = settings.Providers.Count == 0;
        if (!firstRun && !keyMissing && !noProvider) return;

        var parts = new List<string>();
        if (firstRun) parts.Add(Loc.Get("first_run", settings.OverlayHotkey));
        if (noProvider) parts.Add(Loc.Get("hint_no_provider"));
        else if (keyMissing) parts.Add(Loc.Get("hint_no_key", provider!.Name));
        _controller!.ShowNotice(string.Join(" ", parts));
    }

    /// <summary>
    /// A background tool must not vanish because of one bad moment: unexpected errors are logged and shown, the app
    /// keeps running. (Run-level errors are already caught around each run; this is the last line of defence.)
    /// </summary>
    private void InstallCrashGuards()
    {
        DispatcherUnhandledException += (_, e) =>
        {
            AppLog.Error("Unhandled UI exception.", e.Exception);
            _controller?.ShowNotice(Loc.Get("err_unexpected", e.Exception.GetType().Name));
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.Error($"Unhandled exception (terminating={e.IsTerminating}).", e.ExceptionObject as Exception);
            AppLog.Flush(); // the process may be gone in a moment: get the line onto disk first
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Error("Unobserved task exception.", e.Exception);
            e.SetObserved();
        };
    }

    private void ToggleAutostart()
    {
        try
        {
            // settings.toml stays the single source of truth; the registry follows it through the normal reload.
            SettingsWriter.UpdateFile(_config!.SettingsPath, "autostart", !_config.Current.Settings.Autostart);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Error("settings.toml could not be updated.", ex);
            _controller!.ShowNotice(Loc.Get("autostart_failed"));
            return;
        }

        ReloadConfig(manual: false);
    }

    private void ApplyTheme(AppSettings settings)
    {
        // INSTAPROMPT_THEME overrides settings.toml (handy for testing both looks).
        var text = Environment.GetEnvironmentVariable("INSTAPROMPT_THEME") ?? settings.Theme;
        _theme!.Set(Enum.TryParse<AppTheme>(text, ignoreCase: true, out var theme) ? theme : AppTheme.System, settings.Accent, settings.Appearance);
    }

    /// <summary>Registers the overlay, inline and all prompt hotkeys anew. Hotkeys another app already owns are reported.</summary>
    private void RegisterHotkeys(AppSettings settings, IReadOnlyList<PromptDefinition> prompts)
    {
        _hotkeys!.UnregisterAll();
        var failures = new List<string>();

        void Register(string gesture, string label, Action<long> callback)
        {
            try
            {
                var result = _hotkeys.Register(HotkeyGesture.Parse(gesture), callback);
                if (result.Success)
                {
                    AppLog.Info($"Hotkey registered: {gesture} ({label})");
                    return;
                }

                AppLog.Error($"Hotkey registration failed: {result.Error}");
            }
            catch (FormatException ex)
            {
                AppLog.Error($"Hotkey '{gesture}' ({label}) is invalid: {ex.Message}");
            }

            failures.Add(Loc.Get("err_hotkey_in_use", gesture, label));
        }

        Register(settings.OverlayHotkey, "Overlay", _controller!.OnOverlayHotkey);
        if (settings.UndoHistory > 0) Register(settings.UndoHotkey, Loc.Get("undo_label"), _controller.OnUndoHotkey);
        foreach (var prompt in prompts.Where(p => p.Hotkey is not null))
        {
            var captured = prompt;
            Register(prompt.Hotkey!, prompt.Name, ts => _controller.OnPromptHotkey(ts, captured));
        }

        if (failures.Count > 0)
        {
            var more = failures.Count > 2 ? Loc.Get("cfg_more", failures.Count - 2) : string.Empty;
            _tray?.ShowMessage("InstaPrompt", string.Join(Environment.NewLine, failures.Take(2)) + more, isError: true);
        }
    }

    private static void LogIssues(ReloadResult result)
    {
        foreach (var issue in result.Issues)
        {
            if (issue.IsError) AppLog.Error($"config: {issue}");
            else AppLog.Warn($"config: {issue}");
        }
    }

    /// <summary>Errors get a notification naming file and line; a manual reload also confirms success.</summary>
    private void ReportIssues(ReloadResult result, bool manual)
    {
        var errors = result.Issues.Where(i => i.IsError).ToList();
        if (errors.Count > 0)
        {
            var more = errors.Count > 1 ? Loc.Get("cfg_more", errors.Count - 1) : string.Empty;
            var message = Loc.Get("cfg_error", FormatIssue(errors[0]) + more);
            _controller!.ShowNotice(message);
            _tray!.ShowMessage("InstaPrompt", message, isError: true);
        }
        else if (result.Issues.Where(i => i.Show).ToList() is { Count: > 0 } notices)
        {
            // A value that fell back to its default (e.g. in [appearance]): the rest of the configuration is active.
            var more = notices.Count > 1 ? Loc.Get("cfg_more", notices.Count - 1) : string.Empty;
            var message = FormatIssue(notices[0]) + more;
            _controller!.ShowNotice(message);
            _tray!.ShowMessage("InstaPrompt", message, isError: false);
        }
        else if (manual)
        {
            _controller!.ShowNotice(Loc.Get("cfg_reloaded", _config!.Current.Prompts.Count));
        }
    }

    private static string FormatIssue(ConfigIssue issue)
        => issue.Line is { } line ? $"{issue.File}, {Loc.Get("cfg_line")} {line}: {issue.Message}" : $"{issue.File}: {issue.Message}";

    // ---- UI ----

    private void ApplyLook(OverlayWindow overlay, StatusWindow status)
    {
        overlay.ApplyLook(_theme!.IsDark, _theme.Backdrop, _theme.NativeCornerPx);
        status.ApplyLook(_theme.IsDark, _theme.Backdrop, _theme.NativeCornerPx);
    }

    private IReadOnlyList<TrayMenuItem> BuildMenu() =>
    [
        new TrayMenuItem(Loc.Get("tray_open_config"), () => Process.Start(new ProcessStartInfo(_configDir) { UseShellExecute = true })),
        new TrayMenuItem(Loc.Get("tray_reload"), () => ReloadConfig(manual: true)),
        new TrayMenuItem(Loc.Get("tray_autostart"), ToggleAutostart, Checked: _config!.Current.Settings.Autostart),
        new TrayMenuItem(string.Empty, () => { }, Separator: true),
        new TrayMenuItem(Loc.Get("tray_quit"), Shutdown),
    ];

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _clipboard?.Dispose();
        _http?.Dispose();
        _window?.Dispose();
        NativeTimer.EndHighResolution();
        _singleInstance?.Dispose();
        AppLog.Info("Exit.");
        AppLog.Flush();
        base.OnExit(e);
    }
}

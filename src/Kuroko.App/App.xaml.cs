using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using Kuroko.App.Keys;
using Kuroko.App.Overlay;
using Kuroko.App.Themes;
using Kuroko.Core.Config;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Hotkeys;
using Kuroko.Core.Localization;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;
using Kuroko.Platform.Autostart;
using Kuroko.Platform.Hotkeys;
using Kuroko.Platform.Native;
using Kuroko.Platform.Secrets;
using Kuroko.Platform.TextIntegration;
using Kuroko.Platform.Tray;

namespace Kuroko.App;

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
    private readonly CredentialKeyStore _keyStore = new();
    private ApiKeysWindow? _keysWindow;

    /// <summary>Single-instance locks of the builds under the old product names (Ghostwriter, InstaPrompt).</summary>
    private static readonly string[] LegacyMutexNames =
    [
        @"Local\Ghostwriter.Claude.5c0e7a52-9f3b-4d86-8f41-2b6d1e0a9c37",
        @"Local\InstaPrompt.Claude.5c0e7a52-9f3b-4d86-8f41-2b6d1e0a9c37",
    ];

    private static bool LegacyInstanceRunning() => LegacyMutexNames.Any(name =>
    {
        if (!Mutex.TryOpenExisting(name, out var mutex)) return false;
        mutex.Dispose();
        return true;
    });

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A name that cannot clash with another tool of the same name (which would make one of the two exit at start).
        // With KUROKO_CONFIG_DIR set (tests, a second copy side by side) the lock is per config folder.
        var mutexName = @"Local\Kuroko.Claude.5c0e7a52-9f3b-4d86-8f41-2b6d1e0a9c37";
        if (Environment.GetEnvironmentVariable("KUROKO_CONFIG_DIR") is { Length: > 0 } otherDir)
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

        // KUROKO_CONFIG_DIR points the app at another folder (tests, or a second copy side by side).
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var customDir = Environment.GetEnvironmentVariable("KUROKO_CONFIG_DIR");
        _configDir = customDir ?? Path.Combine(appData, "Kuroko");

        IReadOnlyList<string> migration = [];
        if (customDir is null)
        {
            // An old build that is still running holds its config folder and the same hotkeys: moving its files away now
            // would leave both apps half configured, so Kuroko asks for the old one to be closed first.
            if (LegacyInstanceRunning())
            {
                MessageBox.Show(Loc.Get("legacy_running"), "Kuroko", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            migration = LegacyConfigMigration.Run(
                _configDir, LegacyConfigMigration.LegacyFolderNames.Select(name => Path.Combine(appData, name)));
        }

        AppLog.Init(_configDir);
        var started = Stopwatch.GetTimestamp();
        AppLog.Info($"Start, version {AppVersion.Current}, elevated={TargetInfo.SelfIsElevated}");
        foreach (var line in migration) AppLog.Warn(line);
        _autostart.RemoveLegacyEntries();
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

        // Both files are read before any window exists; a broken file never prevents the start.
        _config = new ConfigManager(_configDir, Environment.GetEnvironmentVariable, _keyStore);
        var firstRun = !File.Exists(_config.SettingsPath);
        var loaded = _config.Load();
        LogIssues(loaded);
        Lap("config");

        _theme = new ThemeService(this);
        _window = new MessageWindow
        {
            HandlerFailed = ex => _controller?.ShowNotice(Loc.Get("err_unexpected", ex.GetType().Name)),
        };
        _clipboard = new ClipboardService(_window);
        _hotkeys = new HotkeyManager(_window);
        _tray = new TrayIcon(_window, "Kuroko") { MenuProvider = BuildMenu };
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

        // Marker characters are read per run, so changing them in settings.toml takes effect without a restart.
        var runner = new ProviderPromptRunner(
            () => _providers!,
            () => (_config.Current.Settings.MarkerStart, _config.Current.Settings.MarkerEnd));
        _controller = new AppController(
            new TextAccessService(_clipboard), overlay, status, runner,
            prompts: () => _config.Current.Prompts,
            warmUp: () => _ = _providers!.WarmUpAsync(),
            hotkeys: _hotkeys,
            clipboard: _clipboard);

        ApplyConfig(loaded, initial: true);
        Lap("apply config+hotkeys");
        if (loaded.HasErrors || loaded.Issues.Any(i => i.Show)) ReportIssues(loaded, manual: false);
        else ShowStartupHint(firstRun);

        // From here on, edits to settings.toml / prompts.toml are picked up while the app runs.
        _watcher = new ConfigWatcher(_configDir, ReloadQuietPeriod, SettingsLoader.FileName, PromptsLoader.FileName);
        _watcher.Changed += () => Dispatcher.BeginInvoke(() => ReloadConfig(manual: false));

        AppLog.Info($"Ready after {Stopwatch.GetElapsedTime(started).TotalMilliseconds:F0} ms, {_config.Current.Prompts.Count} prompts "
            + $"(phases in ms: {string.Join(", ", laps)})");
        Kuroko.App.Diagnostics.MemoryDiagnostics.StartIfRequested();
        StartIdleTrimming();
    }

    // ---- idle memory trimming ----

    private static readonly TimeSpan TrimRetryWhileBusy = TimeSpan.FromSeconds(2);
    private System.Windows.Threading.DispatcherTimer? _trimTimer;

    /// <summary>
    /// After a quiet period (idle_trim_seconds in settings.toml) the working set is trimmed once. Only done while
    /// nothing is going on, and only once per quiet period. KUROKO_TRIM_SECONDS overrides the setting (tests).
    /// The timer is one-shot and re-armed by every hotkey, so the app does not wake up while it idles.
    /// </summary>
    private void StartIdleTrimming()
    {
        _trimTimer = new System.Windows.Threading.DispatcherTimer();
        _trimTimer.Tick += (_, _) => TrimIfIdle();
        _controller!.Activity += ScheduleIdleTrim;
        ScheduleIdleTrim();
    }

    private int IdleTrimSeconds => int.TryParse(Environment.GetEnvironmentVariable("KUROKO_TRIM_SECONDS"), out var forced)
        ? forced
        : _config!.Current.Settings.IdleTrimSeconds;

    /// <summary>Arms the trim for idle_trim_seconds from now (also after a reload, so a changed setting takes effect).</summary>
    private void ScheduleIdleTrim()
    {
        if (_trimTimer is null) return;
        _trimTimer.Stop();
        var seconds = IdleTrimSeconds;
        if (seconds <= 0) return;

        var idle = Environment.TickCount64 - _controller!.LastActivityTick;
        _trimTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, seconds * 1000L - idle));
        _trimTimer.Start();
    }

    private void TrimIfIdle()
    {
        _trimTimer!.Stop();
        var seconds = IdleTrimSeconds;
        if (seconds <= 0) return;

        var controller = _controller!;
        var idle = Environment.TickCount64 - controller.LastActivityTick;
        if (controller.IsBusy || idle < seconds * 1000L)
        {
            // Still working (overlay open, run active) or a hotkey came in just now: look again shortly.
            _trimTimer.Interval = controller.IsBusy ? TrimRetryWhileBusy : TimeSpan.FromMilliseconds(seconds * 1000L - idle);
            _trimTimer.Start();
            return;
        }

        var (before, after) = Kuroko.Platform.Native.WorkingSetTrimmer.Trim();
        AppLog.Info($"Idle for {idle / 1000} s: working set trimmed {before:F0} MB -> {after:F0} MB");
    }

    // ---- configuration ----

    private void ShowKeysWindow()
    {
        if (_keysWindow is { } open)
        {
            open.Activate();
            return;
        }

        _keysWindow = new ApiKeysWindow(_keyStore, () => _config!.Current.Settings.Providers, () =>
        {
            // A stored or removed key takes effect at once; only errors are reported.
            var result = _config!.Reload(force: true);
            LogIssues(result);
            ApplyConfig(result, initial: false);
            ReportIssues(result, manual: false);
        });
        _keysWindow.Closed += (_, _) => _keysWindow = null;
        _keysWindow.Show();
        _keysWindow.Activate();
    }

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
            ScheduleIdleTrim(); // idle_trim_seconds may have changed
            _controller.Position = Enum.TryParse<OverlayPosition>(settings.OverlayPosition, ignoreCase: true, out var position)
                ? position
                : OverlayPosition.Caret;
            _controller.ResultCopyHotkey = settings.ResultCopyHotkey;
            _controller.OverlaySpot = ParseSpot(settings.OverlayFixedPosition, CardSpot.TopThird);
            _controller.OverlayScreenMargin = settings.OverlayScreenMargin;
            _controller.SetOverlayLayout(settings.OverlayWidth, settings.OverlayMinHeight, settings.OverlayMaxHeight, settings.OverlayFontSize);
            _controller.ResultPlacement = Enum.TryParse<ResultPlacement>(settings.ResultPosition, ignoreCase: true, out var placement)
                ? placement
                : ResultPlacement.Fixed;
            _controller.ResultSpot = ParseSpot(settings.ResultFixedPosition, CardSpot.BottomThird);
            _controller.ResultScreenMargin = settings.ResultScreenMargin;
            _controller.ResultFontSize = settings.ResultFontSize;
            _controller.SetResultSize(settings.ResultWidth, settings.ResultMinHeight, settings.ResultMaxHeight);

            // A fresh registry for the new provider settings; the HttpClient (and its warm connections) is kept.
            _providers = new ProviderRegistry(settings.Providers, settings.DefaultProvider, _http);
            _ = _providers.WarmUpAsync();
        }

        if (initial || result.Changed)
        {
            // A result card on screen holds Esc and the copy hotkey temporarily. They are released while the configured
            // hotkeys are registered anew (a prompt may just have been given the card's copy key) and taken again afterwards.
            _controller!.ReleaseResultHotkeys();
            RegisterHotkeys(settings, prompts);
            _controller.RestoreResultHotkeys();
        }

        if (initial || result.SettingsChanged)
        {
            _tray!.SetTooltip($"Kuroko - {settings.OverlayHotkey}");

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
        // KUROKO_THEME overrides settings.toml (handy for testing both looks).
        var text = Environment.GetEnvironmentVariable("KUROKO_THEME") ?? settings.Theme;
        _theme!.Set(Enum.TryParse<AppTheme>(text, ignoreCase: true, out var theme) ? theme : AppTheme.System, settings.Accent, settings.Appearance);
    }

    private static CardSpot ParseSpot(string name, CardSpot fallback)
        => Enum.TryParse<CardSpot>(name.Replace("-", string.Empty), ignoreCase: true, out var spot) ? spot : fallback;

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
        // The result-copy hotkey is not registered here: it only exists while a result card is on screen (AppController).
        foreach (var prompt in prompts.Where(p => p.Hotkey is not null))
        {
            var captured = prompt;
            Register(prompt.Hotkey!, prompt.Name, ts => _controller.OnPromptHotkey(ts, captured));
        }

        if (failures.Count > 0)
        {
            var more = failures.Count > 2 ? Loc.Get("cfg_more", failures.Count - 2) : string.Empty;
            _tray?.ShowMessage("Kuroko", string.Join(Environment.NewLine, failures.Take(2)) + more, isError: true);
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
            _tray!.ShowMessage("Kuroko", message, isError: true);
        }
        else if (result.Issues.Where(i => i.Show).ToList() is { Count: > 0 } notices)
        {
            // A value that fell back to its default (e.g. in [appearance]): the rest of the configuration is active.
            var more = notices.Count > 1 ? Loc.Get("cfg_more", notices.Count - 1) : string.Empty;
            var message = FormatIssue(notices[0]) + more;
            _controller!.ShowNotice(message);
            _tray!.ShowMessage("Kuroko", message, isError: false);
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
        overlay.ApplyLook(_theme!.IsDark, _theme.Backdrop, _theme.NativeCornerPx, _theme.CustomCornerRadius);
        status.ApplyLook(_theme.IsDark, _theme.Backdrop, _theme.NativeCornerPx, _theme.CustomCornerRadius);
    }

    private IReadOnlyList<TrayMenuItem> BuildMenu() =>
    [
        new TrayMenuItem(Loc.Get("tray_open_config"), () => Process.Start(new ProcessStartInfo(_configDir) { UseShellExecute = true })),
        new TrayMenuItem(Loc.Get("tray_reload"), () => ReloadConfig(manual: true)),
        new TrayMenuItem(Loc.Get("tray_keys"), ShowKeysWindow),
        new TrayMenuItem(Loc.Get("tray_autostart"), ToggleAutostart, Checked: _config!.Current.Settings.Autostart),
        new TrayMenuItem(string.Empty, () => { }, Separator: true),
        new TrayMenuItem(Loc.Get("tray_about"), ShowAbout),
        new TrayMenuItem(Loc.Get("tray_quit"), Shutdown),
    ];

    private static void ShowAbout()
        => MessageBox.Show(Loc.Get("about_text", AppVersion.Current), "Kuroko", MessageBoxButton.OK, MessageBoxImage.Information);

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _clipboard?.Dispose();
        _http?.Dispose();
        _window?.Dispose();
        NativeTimer.SetHighResolution(false);
        _singleInstance?.Dispose();
        AppLog.Info("Exit.");
        AppLog.Flush();
        base.OnExit(e);
    }
}

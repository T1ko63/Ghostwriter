namespace Ghostwriter.Core.Config;

/// <summary>Calls an action once after a burst of triggers has been quiet for the given time.</summary>
public sealed class Debouncer : IDisposable
{
    private readonly Timer _timer;
    private readonly TimeSpan _quiet;

    public Debouncer(TimeSpan quiet, Action action)
    {
        _quiet = quiet;
        _timer = new Timer(_ =>
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                // A failing handler must not take the process down from a timer thread.
            }
        });
    }

    public void Trigger() => _timer.Change(_quiet, Timeout.InfiniteTimeSpan);

    public void Dispose() => _timer.Dispose();
}

/// <summary>
/// Watches the config folder and raises <see cref="Changed"/> (on a thread-pool thread) once per burst of
/// changes. Editors save in different ways (in place, via temp file and rename), and one save often raises
/// several events; the debounce turns that into a single reload.
/// </summary>
public sealed class ConfigWatcher : IDisposable
{
    private readonly FileSystemWatcher _watcher;
    private readonly Debouncer _debouncer;
    private readonly HashSet<string> _files;

    public ConfigWatcher(string directory, TimeSpan quietPeriod, params string[] fileNames)
    {
        _files = new HashSet<string>(fileNames, StringComparer.OrdinalIgnoreCase);
        _debouncer = new Debouncer(quietPeriod, () => Changed?.Invoke());

        _watcher = new FileSystemWatcher(directory)
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
            IncludeSubdirectories = false,
        };
        _watcher.Changed += (_, e) => OnFile(e.Name);
        _watcher.Created += (_, e) => OnFile(e.Name);
        _watcher.Deleted += (_, e) => OnFile(e.Name);
        _watcher.Renamed += (_, e) =>
        {
            OnFile(e.Name);
            OnFile(e.OldName);
        };

        // Buffer overflow or a lost folder: whatever happened, look again.
        _watcher.Error += (_, _) => _debouncer.Trigger();
        _watcher.EnableRaisingEvents = true;
    }

    public event Action? Changed;

    private void OnFile(string? name)
    {
        if (name is not null && _files.Contains(Path.GetFileName(name))) _debouncer.Trigger();
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _debouncer.Dispose();
    }
}

using System.Collections.Concurrent;
using System.Text;

namespace InstaPrompt.Core.Diagnostics;

/// <summary>
/// Minimal file logger. Never pass user text, prompts or API keys to it: log lengths and timings only.
/// Lines are queued and written by a background thread, so logging never costs disk I/O on the UI thread
/// (which matters in the hotkey path).
/// </summary>
public static class AppLog
{
    private static readonly BlockingCollection<string> Queue = new(boundedCapacity: 10_000);
    private static string? _path;
    private static Thread? _writer;

    public static string? FilePath => _path;

    public static void Init(string directory, string fileName = "instaprompt.log")
    {
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, fileName);
        try
        {
            // Keep the log small: start over once it grows past 1 MB.
            if (File.Exists(_path) && new FileInfo(_path).Length > 1_000_000)
            {
                File.Delete(_path);
            }
        }
        catch (IOException)
        {
            // Another instance may hold the file; logging is best effort.
        }

        if (_writer is not null) return;
        _writer = new Thread(WriteLoop) { IsBackground = true, Name = "InstaPrompt log writer", Priority = ThreadPriority.BelowNormal };
        _writer.Start();
    }

    public static void Info(string message) => Write("INFO ", message);

    public static void Warn(string message) => Write("WARN ", message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message} | {exception.GetType().Name}: {exception.Message}");

    /// <summary>Writes out everything still queued (call on exit, or before a fatal error ends the process).</summary>
    public static void Flush(int timeoutMs = 1000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Queue.Count > 0 && Environment.TickCount64 < deadline) Thread.Sleep(5);
        Thread.Sleep(15); // let the writer finish the line it is currently flushing
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff} {level} {message}{Environment.NewLine}";
        System.Diagnostics.Debug.Write(line);
        if (_path is not null) Queue.TryAdd(line); // a full queue drops the line rather than blocking the caller
    }

    private static void WriteLoop()
    {
        try
        {
            using var stream = new FileStream(_path!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            foreach (var line in Queue.GetConsumingEnumerable())
            {
                writer.Write(line);
                if (Queue.Count == 0) writer.Flush(); // flush once a burst is through, so the file is readable while the app runs
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            // Best effort only: if the file cannot be written, the app must not suffer for it.
        }
    }
}

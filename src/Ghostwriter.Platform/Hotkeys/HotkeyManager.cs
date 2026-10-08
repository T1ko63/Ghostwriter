using System.Diagnostics;
using System.Runtime.InteropServices;
using Ghostwriter.Core.Hotkeys;
using Ghostwriter.Platform.Native;
using static Ghostwriter.Platform.Native.NativeMethods;

namespace Ghostwriter.Platform.Hotkeys;

public readonly record struct HotkeyRegistration(bool Success, int Id, string? Error)
{
    public static HotkeyRegistration Ok(int id) => new(true, id, null);
    public static HotkeyRegistration Fail(string error) => new(false, 0, error);
}

/// <summary>Global hotkeys via RegisterHotKey. Callbacks run on the window's (UI) thread.</summary>
public sealed class HotkeyManager : IDisposable
{
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action<long>> _callbacks = new();
    private int _nextId = 1;

    public HotkeyManager(MessageWindow window)
    {
        _window = window;
        window.AddHandler(OnMessage);
    }

    /// <summary>
    /// Registers a gesture. The callback receives the <see cref="Stopwatch.GetTimestamp"/> value taken
    /// when the hotkey message arrived, so latency can be measured from that point.
    /// </summary>
    public HotkeyRegistration Register(HotkeyGesture gesture, Action<long> callback)
    {
        var id = _nextId++;
        if (!RegisterHotKey(_window.Handle, id, (uint)gesture.Modifiers | MOD_NOREPEAT, (uint)gesture.VirtualKey))
        {
            var error = Marshal.GetLastWin32Error();
            return HotkeyRegistration.Fail(error == ERROR_HOTKEY_ALREADY_REGISTERED
                ? $"{gesture} is already in use by another application."
                : $"{gesture} could not be registered (Win32 error {error}).");
        }

        _callbacks[id] = callback;
        return HotkeyRegistration.Ok(id);
    }

    public void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys) UnregisterHotKey(_window.Handle, id);
        _callbacks.Clear();
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg != WM_HOTKEY) return false;
        var timestamp = Stopwatch.GetTimestamp();
        if (_callbacks.TryGetValue((int)wParam, out var callback)) callback(timestamp);
        return true;
    }

    public void Dispose() => UnregisterAll();
}

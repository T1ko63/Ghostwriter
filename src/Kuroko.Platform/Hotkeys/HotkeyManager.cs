using System.Diagnostics;
using System.Runtime.InteropServices;
using Kuroko.Core.Hotkeys;
using Kuroko.Platform.Native;
using static Kuroko.Platform.Native.NativeMethods;

namespace Kuroko.Platform.Hotkeys;

public readonly record struct HotkeyRegistration(bool Success, int Id, string? Error)
{
    public static HotkeyRegistration Ok(int id) => new(true, id, null);
    public static HotkeyRegistration Fail(string error) => new(false, 0, error);
}

/// <summary>Registering and releasing single hotkeys; <see cref="HotkeyManager"/> is the real one, tests use a fake.</summary>
public interface IHotkeyRegistry
{
    /// <inheritdoc cref="HotkeyManager.Register"/>
    HotkeyRegistration Register(HotkeyGesture gesture, Action<long> callback, bool temporary = false);

    /// <inheritdoc cref="HotkeyManager.Unregister"/>
    void Unregister(int id);
}

/// <summary>Global hotkeys via RegisterHotKey. Callbacks run on the window's (UI) thread.</summary>
public sealed class HotkeyManager : IHotkeyRegistry, IDisposable
{
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action<long>> _callbacks = new();

    // Registered only for a short while by their owner (e.g. Esc while the result card is visible) and released by it
    // with Unregister. UnregisterAll, used when the configuration is reloaded, leaves these alone.
    private readonly HashSet<int> _temporary = new();
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
    /// <param name="temporary">
    /// For a hotkey that exists only while something is on screen: it survives <see cref="UnregisterAll"/> and must be
    /// released with <see cref="Unregister"/>. Unlike the configured hotkeys it may be a bare key such as Esc.
    /// </param>
    public HotkeyRegistration Register(HotkeyGesture gesture, Action<long> callback, bool temporary = false)
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
        if (temporary) _temporary.Add(id);
        return HotkeyRegistration.Ok(id);
    }

    /// <summary>Releases one hotkey (also after <see cref="HotkeyRegistration.Success"/> of a temporary one). Unknown ids are ignored.</summary>
    public void Unregister(int id)
    {
        if (!_callbacks.Remove(id)) return;
        _temporary.Remove(id);
        UnregisterHotKey(_window.Handle, id);
    }

    /// <summary>Releases all configured hotkeys (not the temporary ones, see <see cref="Register"/>).</summary>
    public void UnregisterAll()
    {
        foreach (var id in _callbacks.Keys.Where(id => !_temporary.Contains(id)).ToList()) Unregister(id);
    }

    private bool OnMessage(uint msg, nint wParam, nint lParam, out nint result)
    {
        result = 0;
        if (msg != WM_HOTKEY) return false;
        var timestamp = Stopwatch.GetTimestamp();
        if (_callbacks.TryGetValue((int)wParam, out var callback)) callback(timestamp);
        return true;
    }

    public void Dispose()
    {
        foreach (var id in _callbacks.Keys.ToList()) Unregister(id);
    }
}

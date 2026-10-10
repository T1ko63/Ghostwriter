using Kuroko.Core.Diagnostics;
using Kuroko.Core.Hotkeys;
using Kuroko.Platform.Hotkeys;

namespace Kuroko.App;

/// <summary>
/// One hotkey that is registered only while something is on screen: Esc of a run, Esc and copy of the result card, copy
/// of the error pill. Without a registry nothing is ever registered.
/// </summary>
/// <param name="failureLog">Start of the log line when the key is taken, e.g. "Esc could not be registered for the run".</param>
internal sealed class TemporaryHotkey(IHotkeyRegistry? registry, string failureLog)
{
    private const int VK_ESCAPE = 0x1B;

    public static HotkeyGesture Escape { get; } = new(HotkeyModifiers.None, VK_ESCAPE);

    private int _id;

    public bool IsHeld => _id != 0;

    /// <summary>Registers <paramref name="gesture"/>, releasing what this slot held before; false when the key is taken or there is no registry.</summary>
    public bool Register(HotkeyGesture gesture, Action<long> pressed)
    {
        if (registry is null) return false;
        Release();
        var result = registry.Register(gesture, pressed, temporary: true);
        if (result.Success) _id = result.Id;
        else AppLog.Warn($"{failureLog}: {result.Error}");
        return result.Success;
    }

    public void Release()
    {
        if (registry is null || _id == 0) return;
        registry.Unregister(_id);
        _id = 0;
    }
}

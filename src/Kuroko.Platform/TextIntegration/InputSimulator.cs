using System.Runtime.InteropServices;
using static Kuroko.Platform.Native.NativeMethods;

namespace Kuroko.Platform.TextIntegration;

/// <summary>Keyboard injection via SendInput.</summary>
public sealed class InputSimulator : IKeyboard
{
    public const ushort VK_CONTROL = 0x11;
    public const ushort VK_A = 0x41;
    public const ushort VK_C = 0x43;
    public const ushort VK_V = 0x56;

    // Individual left/right keys, because that is what GetAsyncKeyState reports reliably.
    private static readonly ushort[] ModifierKeys = [0xA0, 0xA1, 0xA2, 0xA3, 0xA4, 0xA5, 0x5B, 0x5C];

    /// <summary>
    /// Sends Ctrl+key as one atomic batch. Any modifier the user is still physically holding from the
    /// global hotkey (e.g. Alt of Ctrl+Alt+K) is released first, otherwise the target would see
    /// Ctrl+Alt+C instead of Ctrl+C. The user's later physical key-up is harmless.
    /// </summary>
    public bool CtrlChord(ushort key)
    {
        var events = new List<INPUT>(10);
        foreach (var modifier in ModifierKeys)
        {
            if ((GetAsyncKeyState(modifier) & 0x8000) != 0) events.Add(Key(modifier, up: true));
        }

        events.Add(Key(VK_CONTROL, up: false));
        events.Add(Key(key, up: false));
        events.Add(Key(key, up: true));
        events.Add(Key(VK_CONTROL, up: true));

        var sent = SendInput((uint)events.Count, events.ToArray(), Marshal.SizeOf<INPUT>());
        return sent == events.Count;
    }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        U = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)MapVirtualKey(vk, 0),
                dwFlags = up ? KEYEVENTF_KEYUP : 0,
            },
        },
    };
}

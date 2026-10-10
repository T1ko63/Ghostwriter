using System.Diagnostics.CodeAnalysis;

namespace Kuroko.Core.Hotkeys;

/// <summary>Modifier flags. Values intentionally match the Win32 MOD_* constants.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 0x1,
    Ctrl = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

/// <summary>
/// A parsed hotkey such as "Ctrl+Alt+K". Pure data, no OS calls, so it can be validated
/// (duplicates, missing modifiers) in the config layer and unit-tested.
/// </summary>
public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, int VirtualKey)
{
    private static readonly Dictionary<string, int> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Tab"] = 0x09,
        ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Backspace"] = 0x08,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Del"] = 0x2E,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        [";"] = 0xBA, ["="] = 0xBB, [","] = 0xBC, ["-"] = 0xBD, ["."] = 0xBE, ["/"] = 0xBF,
        ["`"] = 0xC0, ["["] = 0xDB, ["\\"] = 0xDC, ["]"] = 0xDD, ["'"] = 0xDE,
    };

    private static readonly Dictionary<string, HotkeyModifiers> ModifierNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = HotkeyModifiers.Ctrl, ["Control"] = HotkeyModifiers.Ctrl, ["Strg"] = HotkeyModifiers.Ctrl,
        ["Alt"] = HotkeyModifiers.Alt,
        ["Shift"] = HotkeyModifiers.Shift, ["Umschalt"] = HotkeyModifiers.Shift,
        ["Win"] = HotkeyModifiers.Win, ["Windows"] = HotkeyModifiers.Win,
    };

    public static HotkeyGesture Parse(string text)
        => TryParse(text, out var gesture, out var error)
            ? gesture
            : throw new FormatException(error);

    public static bool TryParse(string? text, out HotkeyGesture gesture, [NotNullWhen(false)] out string? error)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Hotkey is empty.";
            return false;
        }

        // "Ctrl++" style (plus as key) is not supported; the key list has no '+'.
        var parts = text.Split('+', StringSplitOptions.TrimEntries);
        if (parts.Any(p => p.Length == 0))
        {
            error = $"Hotkey '{text}' is malformed.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!ModifierNames.TryGetValue(parts[i], out var modifier))
            {
                error = $"Unknown modifier '{parts[i]}' in hotkey '{text}'.";
                return false;
            }

            if (modifiers.HasFlag(modifier))
            {
                error = $"Modifier '{parts[i]}' appears twice in hotkey '{text}'.";
                return false;
            }

            modifiers |= modifier;
        }

        var keyName = parts[^1];
        if (!TryParseKey(keyName, out var vk))
        {
            error = $"Unknown key '{keyName}' in hotkey '{text}'.";
            return false;
        }

        var isFunctionKey = vk is >= 0x70 and <= 0x87;
        if (modifiers == HotkeyModifiers.None && !isFunctionKey)
        {
            error = $"Hotkey '{text}' needs at least one modifier (Ctrl, Alt, Shift or Win).";
            return false;
        }

        gesture = new HotkeyGesture(modifiers, vk);
        error = null;
        return true;
    }

    private static bool TryParseKey(string name, out int vk)
    {
        vk = 0;
        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            vk = char.ToUpperInvariant(name[0]);
            return true;
        }

        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            vk = name[0];
            return true;
        }

        if ((name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var n) && n is >= 1 and <= 24)
        {
            vk = 0x70 + n - 1;
            return true;
        }

        return NamedKeys.TryGetValue(name, out vk);
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(KeyName(VirtualKey));
        return string.Join('+', parts);
    }

    private static string KeyName(int vk)
    {
        if (vk is >= 0x41 and <= 0x5A or >= 0x30 and <= 0x39) return ((char)vk).ToString();
        if (vk is >= 0x70 and <= 0x87) return $"F{vk - 0x70 + 1}";
        foreach (var (name, code) in NamedKeys)
        {
            if (code == vk) return name;
        }

        return $"0x{vk:X2}";
    }
}

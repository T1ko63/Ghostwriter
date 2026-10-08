using Ghostwriter.Core.Hotkeys;

namespace Ghostwriter.Tests;

public class HotkeyGestureTests
{
    [Theory]
    [InlineData("Ctrl+Alt+K", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 'K')]
    [InlineData("ctrl+shift+1", HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, '1')]
    [InlineData("Strg + Alt + p", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 'P')]
    [InlineData("Win+Space", HotkeyModifiers.Win, 0x20)]
    [InlineData("Alt+F4", HotkeyModifiers.Alt, 0x73)]
    public void Parses_valid_gestures(string text, HotkeyModifiers modifiers, int vk)
    {
        Assert.True(HotkeyGesture.TryParse(text, out var gesture, out var error), error);
        Assert.Equal(modifiers, gesture.Modifiers);
        Assert.Equal(vk, gesture.VirtualKey);
    }

    [Fact]
    public void Function_key_without_modifier_is_allowed()
    {
        Assert.True(HotkeyGesture.TryParse("F9", out var gesture, out _));
        Assert.Equal(HotkeyModifiers.None, gesture.Modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("K")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+K")]
    [InlineData("Hyper+K")]
    [InlineData("Ctrl+Banana")]
    [InlineData("Ctrl+F25")]
    public void Rejects_invalid_gestures(string text)
    {
        Assert.False(HotkeyGesture.TryParse(text, out _, out var error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Equivalent_spellings_are_equal()
    {
        Assert.Equal(HotkeyGesture.Parse("Alt+Ctrl+k"), HotkeyGesture.Parse("Strg+Alt+K"));
    }

    [Fact]
    public void ToString_is_canonical_and_round_trips()
    {
        var gesture = HotkeyGesture.Parse("shift+alt+ctrl+f5");
        Assert.Equal("Ctrl+Alt+Shift+F5", gesture.ToString());
        Assert.Equal(gesture, HotkeyGesture.Parse(gesture.ToString()));
    }
}

using Kuroko.Core.Config;
using Kuroko.Core.Hotkeys;
using Kuroko.Core.Localization;

namespace Kuroko.Tests;

public class LocTests
{
    [Theory]
    [InlineData("de", "Ctrl+Alt+C kopiert das Ergebnis")]
    [InlineData("en", "Ctrl+Alt+C copies the result")]
    public void Rescue_hint_names_the_copy_hotkey(string language, string expected)
    {
        var previous = Loc.Language;
        try
        {
            Loc.Language = language;
            Assert.True(HotkeyGesture.TryParse(AppSettings.DefaultResultCopyHotkey, out var gesture, out _));
            Assert.StartsWith(expected, Loc.Get("rescue_hotkey", gesture));
        }
        finally
        {
            Loc.Language = previous;
        }
    }

    [Theory]
    [InlineData("rescue_tray")]
    [InlineData("tray_copy_last")]
    public void Rescue_texts_exist_in_both_languages(string key)
    {
        var previous = Loc.Language;
        try
        {
            Loc.Language = "de";
            var de = Loc.Get(key);
            Loc.Language = "en";
            var en = Loc.Get(key);
            Assert.NotEqual(key, de);
            Assert.NotEqual(key, en);
            Assert.NotEqual(de, en);
        }
        finally
        {
            Loc.Language = previous;
        }
    }
}

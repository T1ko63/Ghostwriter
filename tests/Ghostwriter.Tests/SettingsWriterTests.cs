using Ghostwriter.Core.Config;

namespace Ghostwriter.Tests;

public class SettingsWriterTests
{
    [Fact]
    public void Replaces_the_value_and_keeps_the_trailing_comment()
    {
        var result = SettingsWriter.SetBool("theme = \"dark\"\nautostart = false   # beim Anmelden starten\n", "autostart", true);
        Assert.Equal("theme = \"dark\"\nautostart = true   # beim Anmelden starten\n", result);
    }

    [Fact]
    public void Leaves_everything_else_untouched_including_comments_and_tables()
    {
        const string original = "# Kommentar\noverlay_hotkey = \"Ctrl+Shift+Space\"\nautostart = true\n\n[providers.a]\nautostart = \"not the global one\"\n";
        var result = SettingsWriter.SetBool(original, "autostart", false);
        Assert.Equal(original.Replace("autostart = true", "autostart = false"), result);
    }

    [Fact]
    public void Never_touches_a_same_named_key_inside_a_table()
    {
        const string original = "theme = \"dark\"\n[providers.x]\nautostart = true\n";
        var result = SettingsWriter.SetBool(original, "autostart", false);
        Assert.Contains("[providers.x]\nautostart = true", result);           // table value unchanged
        Assert.StartsWith("theme = \"dark\"\nautostart = false\n", result);     // a new top-level line was added
    }

    [Fact]
    public void Missing_key_is_inserted_before_the_first_table()
    {
        var result = SettingsWriter.SetBool("theme = \"dark\"\n\n[providers.x]\ntype = \"gemini\"\n", "autostart", true);
        Assert.Equal("theme = \"dark\"\nautostart = true\n\n[providers.x]\ntype = \"gemini\"\n", result);
    }

    [Fact]
    public void Missing_key_in_a_file_without_tables_is_appended()
    {
        Assert.Equal("theme = \"dark\"\nautostart = true", SettingsWriter.SetBool("theme = \"dark\"", "autostart", true));
    }

    [Fact]
    public void Empty_file_gets_the_key()
    {
        Assert.Equal("autostart = true\n", SettingsWriter.SetBool(string.Empty, "autostart", true));
    }

    [Fact]
    public void Crlf_line_endings_are_preserved()
    {
        var result = SettingsWriter.SetBool("theme = \"dark\"\r\nautostart = false\r\n", "autostart", true);
        Assert.Equal("theme = \"dark\"\r\nautostart = true\r\n", result);
    }

    [Fact]
    public void Setting_the_current_value_changes_nothing()
    {
        const string original = "autostart = true\n";
        Assert.Equal(original, SettingsWriter.SetBool(original, "autostart", true));
    }

    [Fact]
    public void Result_is_still_valid_settings_and_reads_back_the_value()
    {
        var generated = DefaultSettings.Create(_ => null);
        var on = SettingsWriter.SetBool(generated, "autostart", true);

        Assert.True(SettingsLoader.Parse(on, _ => null).Settings!.Autostart);
        Assert.False(SettingsLoader.Parse(SettingsWriter.SetBool(on, "autostart", false), _ => null).Settings!.Autostart);
    }

    [Fact]
    public void UpdateFile_writes_only_when_something_changes()
    {
        var path = Path.Combine(Path.GetTempPath(), "ip-writer-" + Guid.NewGuid().ToString("N") + ".toml");
        try
        {
            File.WriteAllText(path, "autostart = false\n");
            Assert.True(SettingsWriter.UpdateFile(path, "autostart", true));
            Assert.Equal("autostart = true\n", File.ReadAllText(path));
            Assert.False(SettingsWriter.UpdateFile(path, "autostart", true));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UpdateFile_keeps_utf8_without_bom_and_leaves_no_other_files_behind()
    {
        var dir = Path.Combine(Path.GetTempPath(), "gw-writer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "settings.toml");
        try
        {
            File.WriteAllText(path, "# Größe ändern\r\nautostart = false\r\n");
            Assert.True(SettingsWriter.UpdateFile(path, "autostart", true));

            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes is [0xEF, 0xBB, 0xBF, ..]);
            Assert.Equal("# Größe ändern\r\nautostart = true\r\n", File.ReadAllText(path));
            Assert.Equal([path], Directory.GetFiles(dir));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}

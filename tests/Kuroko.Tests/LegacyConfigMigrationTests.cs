using Kuroko.Core.Config;

namespace Kuroko.Tests;

public sealed class LegacyConfigMigrationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "kuroko-migration-" + Guid.NewGuid().ToString("N"));

    private string NewDir => Path.Combine(_root, "Kuroko");
    private string Ghostwriter => Path.Combine(_root, "Ghostwriter");
    private string InstaPrompt => Path.Combine(_root, "InstaPrompt");

    public LegacyConfigMigrationTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static void Write(string dir, string name, string text)
    {
        var path = Path.Combine(dir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Read(string dir, string name) => File.ReadAllText(Path.Combine(dir, name));

    private IReadOnlyList<string> Run() => LegacyConfigMigration.Run(NewDir, [Ghostwriter, InstaPrompt]);

    [Fact]
    public void Nothing_to_do_without_old_folders()
    {
        Assert.Empty(Run());
        Assert.False(Directory.Exists(NewDir));
    }

    [Fact]
    public void The_old_folder_becomes_the_new_one_when_there_is_none_yet()
    {
        Write(Ghostwriter, "settings.toml", "gw settings");
        Write(Ghostwriter, "prompts.toml", "gw prompts");
        Write(Ghostwriter, Path.Combine("sub", "note.txt"), "note");

        var log = Run();

        Assert.Single(log);
        Assert.False(Directory.Exists(Ghostwriter));
        Assert.Equal("gw settings", Read(NewDir, "settings.toml"));
        Assert.Equal("gw prompts", Read(NewDir, "prompts.toml"));
        Assert.Equal("note", Read(NewDir, Path.Combine("sub", "note.txt")));
    }

    [Fact]
    public void Existing_files_in_the_new_folder_are_never_overwritten()
    {
        Write(NewDir, "settings.toml", "kuroko settings");
        Write(Ghostwriter, "settings.toml", "gw settings");
        Write(Ghostwriter, "prompts.toml", "gw prompts");

        Run();

        Assert.Equal("kuroko settings", Read(NewDir, "settings.toml"));
        Assert.Equal("gw prompts", Read(NewDir, "prompts.toml"));
        // The old copy that was not taken over stays where it was.
        Assert.Equal("gw settings", Read(Ghostwriter, "settings.toml"));
        Assert.False(File.Exists(Path.Combine(Ghostwriter, "prompts.toml")));
    }

    [Fact]
    public void Ghostwriter_wins_over_InstaPrompt_and_InstaPrompt_fills_the_gaps()
    {
        Write(Ghostwriter, "settings.toml", "gw settings");
        Write(InstaPrompt, "settings.toml", "ip settings");
        Write(InstaPrompt, "prompts.toml", "ip prompts");

        var log = Run();

        Assert.Equal(2, log.Count);
        Assert.Equal("gw settings", Read(NewDir, "settings.toml"));
        Assert.Equal("ip prompts", Read(NewDir, "prompts.toml"));
        Assert.False(Directory.Exists(Ghostwriter));
        Assert.Equal("ip settings", Read(InstaPrompt, "settings.toml"));
        Assert.Contains(log, line => line.Contains("1 kept", StringComparison.Ordinal));
    }

    [Fact]
    public void An_old_folder_that_is_emptied_completely_is_removed()
    {
        Write(NewDir, "settings.toml", "kuroko settings");
        Write(InstaPrompt, Path.Combine("logs", "instaprompt.log"), "log");

        Run();

        Assert.False(Directory.Exists(InstaPrompt));
        Assert.Equal("log", Read(NewDir, Path.Combine("logs", "instaprompt.log")));
    }

    [Fact]
    public void A_second_run_changes_nothing()
    {
        Write(Ghostwriter, "settings.toml", "gw settings");
        Run();

        Assert.Empty(Run());
        Assert.Equal("gw settings", Read(NewDir, "settings.toml"));
    }
}

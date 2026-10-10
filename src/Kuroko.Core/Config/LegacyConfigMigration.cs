namespace Kuroko.Core.Config;

/// <summary>
/// One-time move of the config folders of the old product names (%APPDATA%\Ghostwriter, before that %APPDATA%\InstaPrompt)
/// into the Kuroko folder. Never overwrites: a file that already exists in the new folder stays as it is, and the old copy
/// stays in the old folder. An old folder is removed only once it is empty.
/// </summary>
public static class LegacyConfigMigration
{
    /// <summary>The old folder names under %APPDATA%, newest first: the newest one wins when both have the same file.</summary>
    public static IReadOnlyList<string> LegacyFolderNames { get; } = ["Ghostwriter", "InstaPrompt"];

    /// <summary>Moves the content of every existing <paramref name="legacyDirs"/> folder into <paramref name="newDir"/>.</summary>
    /// <returns>Lines for the log (written once the log is open); empty if there was nothing to do.</returns>
    public static IReadOnlyList<string> Run(string newDir, IEnumerable<string> legacyDirs)
    {
        var log = new List<string>();
        foreach (var legacyDir in legacyDirs)
        {
            try
            {
                if (!Directory.Exists(legacyDir)) continue;

                if (!Directory.Exists(newDir) && TryMoveWholeFolder(legacyDir, newDir))
                {
                    log.Add($"Config folder moved from {legacyDir}.");
                    continue;
                }

                var (moved, kept) = MergeInto(legacyDir, newDir);
                log.Add(kept == 0
                    ? $"Config files moved from {legacyDir} ({moved})."
                    : $"Config files moved from {legacyDir} ({moved}); {kept} kept there because they already exist in {newDir} or could not be moved.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Never block startup over this; the app simply starts with what is already in the new folder.
                log.Add($"Config folder could not be moved from {legacyDir} ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        return log;
    }

    private static bool TryMoveWholeFolder(string legacyDir, string newDir)
    {
        try
        {
            Directory.Move(legacyDir, newDir);
            return true;
        }
        catch (IOException)
        {
            // A file in the old folder may be open (an old instance writing its log). Fall back to file by file.
            return false;
        }
    }

    private static (int Moved, int Kept) MergeInto(string legacyDir, string newDir)
    {
        int moved = 0, kept = 0;
        Directory.CreateDirectory(newDir);

        foreach (var file in Directory.GetFiles(legacyDir, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(newDir, Path.GetRelativePath(legacyDir, file));
            if (File.Exists(target) || Directory.Exists(target))
            {
                kept++;
                continue;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(file, target, overwrite: false);
                moved++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                kept++;
            }
        }

        // Remove the folders that are empty now, deepest first, the old folder itself last.
        var folders = Directory.GetDirectories(legacyDir, "*", SearchOption.AllDirectories)
            .OrderByDescending(d => d.Length)
            .Append(legacyDir);
        foreach (var folder in folders)
        {
            try
            {
                if (!Directory.EnumerateFileSystemEntries(folder).Any()) Directory.Delete(folder);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An empty leftover folder does no harm.
            }
        }

        return (moved, kept);
    }
}

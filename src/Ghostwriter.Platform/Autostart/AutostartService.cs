using System.IO;
using Ghostwriter.Core.Diagnostics;
using Microsoft.Win32;

namespace Ghostwriter.Platform.Autostart;

/// <summary>Start with Windows via the per-user Run key (no admin rights, nothing outside the user's profile).</summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly string _valueName;

    /// <summary>
    /// The default value name is deliberately not just "Ghostwriter": other tools with the same working title
    /// register under that name, and writing it would silently replace their autostart entry.
    /// </summary>
    public const string DefaultValueName = "Ghostwriter (Claude)";

    /// <summary>The entry written under the old product name; removed so the old exe does not start alongside.</summary>
    private const string LegacyValueName = "InstaPrompt (Claude)";

    public AutostartService(string valueName = DefaultValueName) => _valueName = valueName;

    /// <summary>The registered command line, or null if autostart is off.</summary>
    public string? RegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(_valueName) as string;
    }

    /// <summary>
    /// Makes the registry match the wish: adds or removes the entry, and refreshes it when the program was
    /// moved. Returns false (and logs) if the registry cannot be written.
    /// </summary>
    public bool Apply(bool enabled, string exePath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            var current = key.GetValue(_valueName) as string;

            if (_valueName == DefaultValueName && key.GetValue(LegacyValueName) is not null)
            {
                key.DeleteValue(LegacyValueName, throwOnMissingValue: false);
                AppLog.Info("Old InstaPrompt autostart entry removed.");
            }

            if (!enabled)
            {
                if (current is not null)
                {
                    key.DeleteValue(_valueName, throwOnMissingValue: false);
                    AppLog.Info("Autostart disabled.");
                }

                return true;
            }

            var wanted = $"\"{exePath}\"";
            if (!string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(_valueName, wanted, RegistryValueKind.String);
                AppLog.Info($"Autostart enabled ({exePath}).");
            }

            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
        {
            AppLog.Error("Autostart could not be changed.", ex);
            return false;
        }
    }
}

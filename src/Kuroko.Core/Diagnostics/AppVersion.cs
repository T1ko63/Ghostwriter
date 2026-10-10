using System.Reflection;

namespace Kuroko.Core.Diagnostics;

/// <summary>The version from <c>Directory.Build.props</c>, as shown in the log and under "About Kuroko".</summary>
public static class AppVersion
{
    /// <summary>For example "1.0.0+3f2a9c1" when the build knew its git commit, otherwise "1.0.0".</summary>
    public static string Current { get; } = Format(
        (Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly)
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    /// <summary>Shortens the full commit hash the SDK appends ("1.0.0+&lt;40 hex digits&gt;") to seven digits.</summary>
    public static string Format(string? informationalVersion)
    {
        if (string.IsNullOrWhiteSpace(informationalVersion)) return "?";
        var plus = informationalVersion.IndexOf('+');
        if (plus < 0 || informationalVersion.Length - plus - 1 <= 7) return informationalVersion;
        return informationalVersion[..(plus + 8)];
    }
}

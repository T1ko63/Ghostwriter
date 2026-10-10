using System.Diagnostics;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Providers;

namespace Kuroko.App;

/// <summary>The latency lines in the log: from the hotkey (or the choice in the overlay) to what the user sees.</summary>
internal sealed class LatencyLog(IDesktop desktop)
{
    /// <summary>Logs hotkey -> first rendered frame of the overlay, which is what the user perceives as "appears".</summary>
    public void OverlayShown(long hotkeyTimestamp, string app, string anchorKind)
    {
        var shownCall = Stopwatch.GetElapsedTime(hotkeyTimestamp).TotalMilliseconds;
        desktop.AfterNextFrame(() => AppLog.Info($"[{app}] overlay at {anchorKind}: Show() returned after {shownCall:F1} ms, "
            + $"first frame after {Stopwatch.GetElapsedTime(hotkeyTimestamp).TotalMilliseconds:F1} ms"));
    }

    /// <summary>Logs the time from hotkey/choice to the first text of the card, once layout is done and once the frame is rendered.</summary>
    public void FirstText(string app, string prompt, long origin, string since)
    {
        var layout = Stopwatch.GetElapsedTime(origin).TotalMilliseconds;
        desktop.AfterNextFrame(() => AppLog.Info($"[{app}] '{prompt}' first text in the card: layout done {layout:F0} ms, "
            + $"first frame {Stopwatch.GetElapsedTime(origin).TotalMilliseconds:F0} ms since {since}"));
    }

    /// <summary>
    /// Logs the stages of the AI request, if one was sent. "request sent" is the number the user feels: from hotkey/choice
    /// to the moment the request left the app.
    /// </summary>
    public static void Request(LlmTimings timings, long origin, string since)
    {
        if (timings.Sent == 0) return;
        AppLog.Info($"  timing since {since}: request sent {LlmTimings.Ms(origin, timings.Sent):F0} ms, "
            + $"response headers +{LlmTimings.Ms(timings.Sent, timings.Headers):F0} ms, first text +{LlmTimings.Ms(timings.Headers, timings.FirstToken):F0} ms, "
            + $"answer complete +{LlmTimings.Ms(timings.FirstToken, timings.Done):F0} ms (attempts: {timings.Attempts})");
    }
}

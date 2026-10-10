using Kuroko.App.Overlay;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Localization;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;
using Kuroko.Platform.TextIntegration;

namespace Kuroko.App;

/// <summary>What the status pill says when reading, asking the AI or pasting failed, and how such a failure is logged.</summary>
internal static class FailureMessages
{
    public static string Describe(CaptureResult result) => result.Failure switch
    {
        CaptureFailure.ElevatedTarget => Loc.Get("err_elevated", result.Detail ?? "?"),
        CaptureFailure.PasswordField => Loc.Get("err_password"),
        CaptureFailure.UnsupportedApp => Loc.Get("err_terminal"),
        CaptureFailure.NoText => Loc.Get("err_no_text"),
        CaptureFailure.TooLong => Loc.Get("err_too_long"),
        CaptureFailure.ReadOnlyField => Loc.Get("err_read_only"),
        CaptureFailure.ClipboardBusy => Loc.Get("err_clipboard_busy"),
        CaptureFailure.InputBlocked => Loc.Get("err_input_blocked"),
        _ => Loc.Get("err_no_window"),
    };

    public static string Describe(LlmException ex)
    {
        var detail = string.IsNullOrWhiteSpace(ex.Detail) ? string.Empty : $" ({ex.Detail})";
        return ex.Kind switch
        {
            LlmErrorKind.Auth => Loc.Get("llm_auth", ex.Provider, detail),
            LlmErrorKind.RateLimit => Loc.Get("llm_rate_limit", ex.Provider, detail),
            LlmErrorKind.ModelNotFound => Loc.Get("llm_model", ex.Provider, detail),
            LlmErrorKind.BadRequest => Loc.Get("llm_bad_request", ex.Provider, detail),
            LlmErrorKind.Server => Loc.Get("llm_server", ex.Provider, detail),
            LlmErrorKind.Network => Loc.Get("llm_network", ex.Provider),
            LlmErrorKind.Timeout => Loc.Get("llm_timeout", ex.Provider),
            LlmErrorKind.Blocked => Loc.Get("llm_blocked", ex.Provider),
            LlmErrorKind.Truncated => Loc.Get("llm_truncated", ex.Provider),
            LlmErrorKind.EmptyResponse => Loc.Get("llm_empty", ex.Provider),
            LlmErrorKind.Config => Loc.Get("llm_config", detail.Trim(' ', '(', ')')),
            _ => Loc.Get("llm_protocol", ex.Provider, detail),
        };
    }

    public static string Describe(ReplaceResult result) => result.Failure switch
    {
        ReplaceFailure.TargetChanged => Loc.Get("err_target_changed"),
        ReplaceFailure.ClipboardBusy => Loc.Get("err_clipboard_busy"),
        ReplaceFailure.InputBlocked => Loc.Get("err_input_blocked"),
        ReplaceFailure.PasteNotAcknowledged => Loc.Get("err_paste_ack"),
        ReplaceFailure.SelectAllFailed => Loc.Get("err_select_all"),
        _ => Loc.Get("err_unexpected", result.Failure),
    };

    public static string Describe(MarkerException ex) => ex.Status == MarkerStatus.Empty
        ? Loc.Get("err_marker_empty")
        : Loc.Get("err_marker_unclosed", ex.Start, ex.End);

    public static string Unexpected(Exception ex) => Loc.Get("err_unexpected", ex.GetType().Name);

    /// <summary>Logs <paramref name="logLine"/> for the session's app and shows <paramref name="message"/> in the error pill.</summary>
    public static void Fail(IStatusView status, string message, RunSession session, string logLine)
    {
        AppLog.Info($"[{session.Target?.ProcessName}] {logLine}");
        status.ShowError(message, session.Anchor);
    }

    /// <summary>
    /// Logs a failed run and shows it in the error pill, which also replaces a card that is up, so no half answer stays.
    /// Nothing was pasted in any of these cases: the original text is untouched.
    /// </summary>
    public static void Report(IStatusView status, Exception ex, PromptDefinition prompt, TargetInfo target, Anchor anchor)
    {
        switch (ex)
        {
            case MarkerException marker:
                // An unclosed or empty marker: nothing was sent.
                AppLog.Info($"[{target.ProcessName}] '{prompt.Name}': marker {marker.Status}");
                status.ShowError(Describe(marker), anchor);
                break;
            case LlmException llm:
                // A rejected request may quote parts of the user's text back; that detail is shown, but not written to the log.
                var logDetail = llm.Kind == LlmErrorKind.BadRequest ? string.Empty : llm.Detail;
                AppLog.Warn($"AI call failed: {llm.Provider}: {llm.Kind} (HTTP {llm.StatusCode?.ToString() ?? "-"}) {logDetail}");
                status.ShowError(Describe(llm), anchor);
                break;
            default:
                AppLog.Error("Run failed.", ex);
                status.ShowError(Unexpected(ex), anchor);
                break;
        }
    }
}

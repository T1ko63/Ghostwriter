using Kuroko.App.Overlay;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Hotkeys;
using Kuroko.Core.Localization;
using Kuroko.Platform.Hotkeys;
using Kuroko.Platform.TextIntegration;

namespace Kuroko.App;

/// <summary>
/// The last complete answer (pasted, shown in a card, or not pasted because the paste failed) and the ways to copy it:
/// the tray entry, and the error pill after a failed paste with its temporary copy hotkey. Memory only: never on disk,
/// never in the log; replaced by the next answer and gone when the app quits.
/// </summary>
internal sealed class LastResult
{
    private readonly IStatusView _status;
    private readonly IHotkeyRegistry? _hotkeys;
    private readonly IClipboard? _clipboard;
    private readonly Func<string> _copyHotkey;
    private readonly TemporaryHotkey _rescueCopy;

    private string? _text;
    private Anchor _rescueAnchor;
    private bool _rescueReleasedForReload;

    /// <param name="copyHotkey">result_copy_hotkey, read whenever the error pill opens or the hotkeys are restored after a reload.</param>
    public LastResult(IStatusView status, IHotkeyRegistry? hotkeys, IClipboard? clipboard, Func<string> copyHotkey)
    {
        _status = status;
        _hotkeys = hotkeys;
        _clipboard = clipboard;
        _copyHotkey = copyHotkey;
        _rescueCopy = new TemporaryHotkey(hotkeys, "The copy hotkey could not be registered for the error pill");
        _status.ActionEnded += _rescueCopy.Release;
    }

    /// <summary>True once an answer has arrived.</summary>
    public bool HasValue => _text is not null;

    public void Remember(string answer) => _text = answer;

    /// <summary>
    /// The answer is complete but could not be pasted: the error pill says so and offers to copy it with
    /// result_copy_hotkey, registered only while this pill is up. The tray entry works as well, also later.
    /// </summary>
    public void ShowRescue(string message, Anchor anchor)
    {
        _rescueAnchor = anchor;
        var parsed = HotkeyGesture.TryParse(_copyHotkey(), out var gesture, out _);
        _status.ShowErrorWithAction(
            $"{message} {(parsed && _hotkeys is not null ? Loc.Get("rescue_hotkey", gesture) : Loc.Get("rescue_tray"))}", anchor);

        // Registered after the pill is up: showing it ends any earlier action pill, which releases that pill's hotkey.
        if (parsed && !_rescueCopy.Register(gesture, OnRescueCopy) && _hotkeys is not null)
        {
            _status.ShowErrorWithAction($"{message} {Loc.Get("rescue_tray")}", anchor);
        }
    }

    private void OnRescueCopy(long hotkeyTimestamp)
    {
        AppLog.Info("hotkey: copy result (error pill)");
        if (!_status.IsActionShown) return;
        Copy(_rescueAnchor, showPill: true);
    }

    /// <summary>
    /// Puts the last answer on the clipboard as a normal copy: the user asked for it, so unlike what Kuroko puts there
    /// itself while reading and pasting, it may show up in the clipboard history.
    /// </summary>
    public void Copy(Anchor anchor, bool showPill)
    {
        if (_text is null) return;
        if (_clipboard is null || !_clipboard.TrySetText(_text, hidden: false))
        {
            AppLog.Info("Copying the last result failed: clipboard busy.");
            if (showPill) _status.ShowError(Loc.Get("err_clipboard_busy"), anchor);
            return;
        }

        AppLog.Info($"Last result copied: {_text.Length} chars.");
        if (showPill) _status.ShowInfo(Loc.Get("result_copied"), anchor); // also ends the error pill, which releases its hotkey
    }

    /// <summary>Releases the copy hotkey of the error pill for a configuration reload.</summary>
    public void ReleaseForReload()
    {
        _rescueReleasedForReload = _rescueCopy.IsHeld;
        _rescueCopy.Release();
    }

    /// <summary>Registers the copy hotkey again (with the current result_copy_hotkey) if the error pill held it and is still up.</summary>
    public void RestoreAfterReload()
    {
        if (_rescueReleasedForReload && _status.IsActionShown && HotkeyGesture.TryParse(_copyHotkey(), out var gesture, out _))
        {
            _rescueCopy.Register(gesture, OnRescueCopy);
        }

        _rescueReleasedForReload = false;
    }
}

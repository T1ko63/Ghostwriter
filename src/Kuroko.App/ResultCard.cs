using System.Text;
using Kuroko.App.Overlay;
using Kuroko.Core.Diagnostics;
using Kuroko.Core.Hotkeys;
using Kuroko.Core.Localization;
using Kuroko.Platform.Hotkeys;
using Kuroko.Platform.TextIntegration;

namespace Kuroko.App;

/// <summary>
/// The result card of an overlay-output prompt while it is live: the text streamed into it, its Esc and copy hotkeys and
/// the watch on the foreground window. Closing it cancels a request that is still streaming.
/// </summary>
internal sealed class ResultCard
{
    private readonly IStatusView _status;
    private readonly IDesktop _desktop;
    private readonly IHotkeyRegistry? _hotkeys;
    private readonly IClipboard? _clipboard;
    private readonly RunState _run;
    private readonly Func<string> _copyHotkey;
    private readonly TemporaryHotkey _esc;
    private readonly TemporaryHotkey _copy;

    private bool _complete;
    private readonly StringBuilder _text = new();
    private Anchor _anchor;
    private IDisposable? _watcher;

    /// <param name="copyHotkey">result_copy_hotkey, read when the card opens and after a configuration reload.</param>
    public ResultCard(IStatusView status, IDesktop desktop, IHotkeyRegistry? hotkeys, IClipboard? clipboard, RunState run, Func<string> copyHotkey)
    {
        _status = status;
        _desktop = desktop;
        _hotkeys = hotkeys;
        _clipboard = clipboard;
        _run = run;
        _copyHotkey = copyHotkey;
        _esc = new TemporaryHotkey(hotkeys, "Esc could not be registered for the result card");
        _copy = new TemporaryHotkey(hotkeys, "The copy hotkey could not be registered for the result card");
        _status.ResultClosed += OnClosed;
    }

    /// <summary>
    /// True from the moment the answer is requested until the card is gone. Esc, the copy hotkey and the watch on the
    /// foreground window exist exactly as long as this is true. Unlike a run, a card that is only being read is not "busy".
    /// </summary>
    public bool IsLive { get; private set; }

    public void Open(Anchor anchor, TargetInfo target)
    {
        IsLive = true;
        _complete = false;
        _text.Clear();
        _anchor = anchor;
        RegisterHotkeys();

        // Installed last: it reports at once if the foreground window has already changed, which closes the card again.
        var watcher = _desktop.WatchForeground(target.Window, OnForegroundChanged);
        if (IsLive) _watcher = watcher;
        else watcher?.Dispose();
    }

    public void Append(string piece)
    {
        _text.Append(piece);
        _status.AppendResult(piece);
    }

    /// <summary>The answer is complete: from now on it can be copied. Returns the whole answer.</summary>
    public string Complete()
    {
        _complete = true;
        return _text.ToString();
    }

    /// <summary>
    /// Closes the card if there is one: stops a request that is still streaming, hides the window and (through
    /// <see cref="OnClosed"/>) releases Esc, the copy hotkey and the foreground watch. Safe to call at any time.
    /// </summary>
    public void Close()
    {
        if (!IsLive) return;

        var run = _run.Detach();
        if (run is not null)
        {
            run.Cancel();
            _run.Busy = false;
        }

        AppLog.Info(run is null ? "Result card closed." : "Result card closed while the answer was streaming: request cancelled.");
        _status.HideStatus();
        OnClosed();
    }

    /// <summary>The window left result mode (closed, or replaced by an error or info pill): nothing of the card may stay registered.</summary>
    private void OnClosed()
    {
        if (!IsLive) return;
        IsLive = false;
        _complete = false;
        _text.Clear();
        UnregisterHotkeys();
        _watcher?.Dispose();
        _watcher = null;
    }

    private void OnForegroundChanged()
    {
        if (!IsLive) return;
        AppLog.Info("Foreground window changed: closing the result card.");
        Close();
    }

    private void OnEsc(long hotkeyTimestamp)
    {
        AppLog.Info("hotkey: Esc (result card)");
        Close();
    }

    private void OnCopy(long hotkeyTimestamp)
    {
        AppLog.Info("hotkey: copy result");
        if (!IsLive) return;
        if (!_complete)
        {
            AppLog.Info("Copy ignored: the answer is still streaming.");
            return;
        }

        var text = _text.ToString();
        if (_clipboard is null || !_clipboard.TrySetText(text, hidden: false))
        {
            _status.ShowError(Loc.Get("err_clipboard_busy"), _anchor);
            return;
        }

        AppLog.Info($"Result copied: {text.Length} chars.");
        _status.ShowInfo(Loc.Get("result_copied"), _anchor); // also ends result mode, which releases the hotkeys
    }

    private void RegisterHotkeys()
    {
        if (_hotkeys is null) return;
        UnregisterHotkeys();

        _esc.Register(TemporaryHotkey.Escape, OnEsc);
        if (!HotkeyGesture.TryParse(_copyHotkey(), out var gesture, out _)) return;
        _copy.Register(gesture, OnCopy);
    }

    private void UnregisterHotkeys()
    {
        _esc.Release();
        _copy.Release();
    }

    /// <summary>Releases Esc and the copy hotkey for a configuration reload.</summary>
    public void ReleaseForReload() => UnregisterHotkeys();

    /// <summary>Registers Esc and the copy hotkey (with the current result_copy_hotkey) again if the card is still up.</summary>
    public void RestoreAfterReload()
    {
        if (IsLive) RegisterHotkeys();
    }
}

using Kuroko.Core.Diagnostics;
using Kuroko.Platform.Hotkeys;

namespace Kuroko.App;

/// <summary>
/// The busy flag, the cancellation of the active run and Esc during that run. Only one run is active at a time; whoever
/// holds <see cref="Current"/> owns the busy flag, and a run that finds itself no longer current stays out of the way.
/// </summary>
internal sealed class RunState
{
    private readonly IDesktop _desktop;
    private readonly TemporaryHotkey _esc;
    private bool _busy;
    private bool _escReleasedForReload;

    public RunState(IDesktop desktop, IHotkeyRegistry? hotkeys)
    {
        _desktop = desktop;
        _esc = new TemporaryHotkey(hotkeys, "Esc could not be registered for the run");
    }

    /// <summary>Set while the overlay is up or a run is active; the 1 ms timer resolution is only held for that time.</summary>
    public bool Busy
    {
        get => _busy;
        set
        {
            _busy = value;
            _desktop.SetHighResolutionTimer(value);
        }
    }

    /// <summary>The cancellation of the active run; null when none is active.</summary>
    public CancellationTokenSource? Current { get; private set; }

    public void Start(CancellationTokenSource cts) => Current = cts;

    public bool IsCurrent(CancellationTokenSource cts) => ReferenceEquals(Current, cts);

    /// <summary>The same as a click on the progress pill: once the paste has been sent, a cancel is no longer honoured.</summary>
    public void Cancel() => Current?.Cancel();

    /// <summary>Takes the active run away from its flow: it notices that it is obsolete and stays out of the way of whatever comes next.</summary>
    public CancellationTokenSource? Detach()
    {
        var run = Current;
        Current = null;
        return run;
    }

    /// <summary>The run is over: nothing is active and the controller is free again.</summary>
    public void Finish()
    {
        Current = null;
        Busy = false;
    }

    // ---- Esc during a run ----

    /// <summary>Esc while a run reads the text or waits for the answer, before a card is up. Registered only for that time.</summary>
    public void HoldEsc()
    {
        if (!_esc.IsHeld) _esc.Register(TemporaryHotkey.Escape, OnEsc);
    }

    public void ReleaseEsc() => _esc.Release();

    private void OnEsc(long hotkeyTimestamp)
    {
        AppLog.Info("hotkey: Esc (run)");
        Cancel();
    }

    /// <summary>Releases Esc for a configuration reload.</summary>
    public void ReleaseForReload()
    {
        _escReleasedForReload = _esc.IsHeld;
        ReleaseEsc();
    }

    /// <summary>Registers Esc again if the run held it and is still waiting without a card.</summary>
    public void RestoreAfterReload(bool cardLive)
    {
        if (_escReleasedForReload && Current is not null && !cardLive) HoldEsc();
        _escReleasedForReload = false;
    }
}

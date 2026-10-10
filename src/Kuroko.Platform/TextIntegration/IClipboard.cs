namespace Kuroko.Platform.TextIntegration;

/// <summary>
/// The clipboard operations the text integration needs. <see cref="ClipboardService"/> is the Win32 one; tests use a fake,
/// so the decisions built on top (sentinel, restore, delayed paste) can be checked without touching the real clipboard.
/// </summary>
public interface IClipboard
{
    /// <summary>Changes on every clipboard update (GetClipboardSequenceNumber).</summary>
    uint SequenceNumber { get; }

    /// <summary>Waits until <see cref="SequenceNumber"/> differs from <paramref name="since"/>; false when the timeout passed first.
    /// A cancel of <paramref name="ct"/> ends the wait at once with an <see cref="OperationCanceledException"/>.</summary>
    Task<bool> WaitForChangeAsync(uint since, TimeSpan timeout, CancellationToken ct = default);

    /// <summary>The plain text on the clipboard, or null (none, or the clipboard stayed locked).</summary>
    string? TryGetText();

    /// <summary>Puts plain text on the clipboard. With <paramref name="hidden"/> it stays out of clipboard history/cloud sync.</summary>
    bool TrySetText(string text, bool hidden);

    /// <summary>Replaces the clipboard with a delayed-render text; null when the clipboard could not be opened.</summary>
    DelayedTextOffer? TryOfferDelayed(string text);

    /// <summary>Copies every format of the clipboard so <see cref="TryRestore"/> can put it back.</summary>
    bool TrySnapshot(out ClipboardSnapshot snapshot);

    bool TryRestore(ClipboardSnapshot snapshot);
}

/// <summary>Synthesised key presses. <see cref="InputSimulator"/> sends them with SendInput; tests record them.</summary>
public interface IKeyboard
{
    /// <summary>Sends Ctrl+<paramref name="key"/> (a virtual-key code, see <see cref="InputSimulator"/>); false when Windows did not take all events.</summary>
    bool CtrlChord(ushort key);
}

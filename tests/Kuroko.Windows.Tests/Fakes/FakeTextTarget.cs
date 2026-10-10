using System.Text;
using Kuroko.Platform.TextIntegration;

namespace Kuroko.Windows.Tests.Fakes;

/// <summary>
/// A pretend clipboard plus a pretend target app that reacts to the keys TextAccessService sends: Ctrl+A selects the
/// field, Ctrl+C copies the selection, Ctrl+V asks for the offered text. Every step is written to <see cref="Events"/>,
/// so tests can check the order (e.g. that the clipboard is restored only after the paste was rendered).
/// </summary>
internal sealed class FakeTextTarget : IClipboard, IKeyboard
{
    private const uint CfUnicodeText = 13;
    private readonly object _gate = new();
    private uint _sequence = 1;
    private DelayedTextOffer? _offer;

    public FakeTextTarget(string userClipboard = "user clipboard")
    {
        ClipboardText = userClipboard;
    }

    public List<string> Events { get; } = new();

    // ---- clipboard state and failure switches ----

    /// <summary>What is on the clipboard right now (the user's content, the sentinel, a copy, or an offered result).</summary>
    public string? ClipboardText { get; private set; }

    public bool SnapshotFails { get; set; }

    public bool SetTextFails { get; set; }

    public bool OfferFails { get; set; }

    public int Snapshots { get; private set; }

    public int Restores { get; private set; }

    // ---- target app state and behaviour ----

    public string FieldText { get; set; } = string.Empty;

    /// <summary>The selected text; empty means only a caret.</summary>
    public string Selection { get; set; } = string.Empty;

    /// <summary>Every SendInput call fails (UIPI, secure desktop).</summary>
    public bool InputBlocked { get; set; }

    /// <summary>Only Ctrl+V fails (e.g. the input was blocked between reading and pasting).</summary>
    public bool PasteBlocked { get; set; }

    /// <summary>The app ignores Ctrl+A (e.g. it is not a text field).</summary>
    public bool IgnoresSelectAll { get; set; }

    /// <summary>The app never asks for the pasted data (hung, or it does not take text).</summary>
    public bool IgnoresPaste { get; set; }

    /// <summary>Delay before the app answers Ctrl+C or Ctrl+V; zero answers synchronously.</summary>
    public TimeSpan ReactionDelay { get; set; }

    /// <summary>The app first empties the clipboard and fills it in a second update (seen in some editors).</summary>
    public bool CopiesInTwoSteps { get; set; }

    /// <summary>Called right after Ctrl+C was sent (e.g. to cancel while the read waits for the copy).</summary>
    public Action? OnCopySent { get; set; }

    /// <summary>Called right after Ctrl+V was sent (e.g. to cancel at exactly that moment).</summary>
    public Action? OnPasteSent { get; set; }

    public List<string> Keys { get; } = new();

    /// <summary>The text the app received through a paste, if any.</summary>
    public string? Pasted { get; private set; }

    // ---- IKeyboard ----

    public bool CtrlChord(ushort key)
    {
        var name = key switch
        {
            InputSimulator.VK_A => "A",
            InputSimulator.VK_C => "C",
            InputSimulator.VK_V => "V",
            _ => key.ToString(),
        };
        Keys.Add(name);
        Log($"key Ctrl+{name}");
        if (InputBlocked || (name == "V" && PasteBlocked)) return false;

        switch (name)
        {
            case "A":
                if (!IgnoresSelectAll) Selection = FieldText;
                break;
            case "C":
                var copied = Selection;
                if (copied.Length > 0) React(() => CopyFromApp(copied));
                OnCopySent?.Invoke();
                break;
            case "V":
                var offer = _offer;
                if (offer is not null && !IgnoresPaste) React(() => Render(offer));
                OnPasteSent?.Invoke();
                break;
        }

        return true;
    }

    private void React(Action action)
    {
        if (ReactionDelay == TimeSpan.Zero)
        {
            action();
            return;
        }

        _ = Task.Run(async () =>
        {
            await Task.Delay(ReactionDelay);
            action();
        });
    }

    private void CopyFromApp(string text)
    {
        if (!CopiesInTwoSteps)
        {
            Update(text, "app copied selection");
            return;
        }

        // The second update comes a little later, so the reader sees the empty clipboard first.
        Update(string.Empty, "app cleared clipboard");
        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            Update(text, "app copied selection");
        });
    }

    private void Render(DelayedTextOffer offer)
    {
        Pasted = offer.Text;
        Log("paste rendered");
        offer.MarkRendered();
    }

    // ---- IClipboard ----

    public uint SequenceNumber
    {
        get
        {
            lock (_gate) return _sequence;
        }
    }

    public async Task<bool> WaitForChangeAsync(uint since, TimeSpan timeout, CancellationToken ct = default)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (SequenceNumber == since)
        {
            ct.ThrowIfCancellationRequested();
            if (Environment.TickCount64 >= deadline) return false;
            await Task.Delay(1, ct);
        }

        return true;
    }

    public string? TryGetText()
    {
        lock (_gate) return ClipboardText;
    }

    public bool TrySetText(string text, bool hidden)
    {
        if (SetTextFails) return false;
        Update(text, text.StartsWith('​') ? "sentinel set" : $"text set (hidden={hidden})");
        return true;
    }

    public DelayedTextOffer? TryOfferDelayed(string text)
    {
        if (OfferFails) return null;
        var offer = new DelayedTextOffer(text);
        lock (_gate) _offer = offer;
        Update(null, "result offered");
        return offer;
    }

    public bool TrySnapshot(out ClipboardSnapshot snapshot)
    {
        snapshot = new ClipboardSnapshot();
        if (SnapshotFails) return false;
        Snapshots++;
        var text = TryGetText();
        if (text is not null) snapshot.Items.Add((CfUnicodeText, Encoding.Unicode.GetBytes(text)));
        Log("snapshot");
        return true;
    }

    public bool TryRestore(ClipboardSnapshot snapshot)
    {
        Restores++;
        var item = snapshot.Items.FirstOrDefault(i => i.Format == CfUnicodeText);
        lock (_gate) _offer = null;
        Update(item.Data is null ? null : Encoding.Unicode.GetString(item.Data), "restore");
        return true;
    }

    private void Update(string? text, string what)
    {
        lock (_gate)
        {
            ClipboardText = text;
            _sequence++;
        }

        Log(what);
    }

    private void Log(string what)
    {
        lock (Events) Events.Add(what);
    }
}

/// <summary>Scripted answers for the UIA and focus questions TextAccessService asks.</summary>
internal sealed class FakeFieldInspector : IFieldInspector
{
    public UiaFocusInfo? Info { get; set; }

    public bool Foreground { get; set; } = true;

    public bool? FocusWindowSame { get; set; } = true;

    public bool? ElementHasFocus { get; set; } = true;

    public bool? SelectionStill { get; set; } = true;

    public bool UiaSelectAllWorks { get; set; } = true;

    /// <summary>Whether a Ctrl+A is seen as a full selection afterwards.</summary>
    public bool KeyboardSelectAllVisible { get; set; } = true;

    public List<string> Calls { get; } = new();

    public Task<UiaFocusInfo?> InspectAsync(TargetInfo target, TimeSpan budget)
    {
        Calls.Add("inspect");
        return Task.FromResult(Info);
    }

    public Task<bool> SelectAllVerifiedAsync(FieldElement element, string expected, TimeSpan budget)
    {
        Calls.Add("uia select all");
        return Task.FromResult(UiaSelectAllWorks);
    }

    public Task<bool> WaitForSelectionAsync(FieldElement element, string expected, TimeSpan budget)
    {
        Calls.Add("wait for selection");
        return Task.FromResult(KeyboardSelectAllVisible);
    }

    public Task<bool?> HasFocusAsync(FieldElement element, TimeSpan budget)
    {
        Calls.Add("has focus");
        return Task.FromResult(ElementHasFocus);
    }

    public Task<bool?> SelectionStillAsync(FieldElement element, string expected, TimeSpan budget)
    {
        Calls.Add("selection still");
        return Task.FromResult(SelectionStill);
    }

    public bool IsForeground(nint window) => Foreground;

    public bool? FocusWindowMatches(TargetInfo target) => FocusWindowSame;
}

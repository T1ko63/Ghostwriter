using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Kuroko.App;
using Kuroko.App.Overlay;
using Kuroko.Core.Hotkeys;
using Kuroko.Core.Prompts;
using Kuroko.Core.Providers;
using Kuroko.Platform.Hotkeys;
using Kuroko.Platform.TextIntegration;

namespace Kuroko.Windows.Tests.Fakes;

/// <summary>Scripted reading and writing: each call can answer at once or wait until the test releases it.</summary>
internal sealed class FakeTextAccess : ITextAccess
{
    public CaptureResult? PreCheckResult { get; set; }

    /// <summary>Applied by <see cref="PreCheck"/> once a probe is given (UIA found a password field after the overlay opened).</summary>
    public CaptureResult? PreCheckWithProbeResult { get; set; }

    public FocusProbe Probe { get; set; } = new(null);

    /// <summary>Text the next capture returns; set <see cref="CaptureFailure"/> or <see cref="CaptureGate"/> for other outcomes.</summary>
    public string FieldText { get; set; } = "teh text";

    public CaptureFailure? CaptureFailure { get; set; }

    public Exception? CaptureThrows { get; set; }

    /// <summary>When set, a capture waits for this task (the test decides when reading ends).</summary>
    public TaskCompletionSource? CaptureGate { get; set; }

    public ReplaceFailure ReplaceFailure { get; set; } = ReplaceFailure.None;

    public Exception? ReplaceThrows { get; set; }

    public List<string> Calls { get; } = new();

    public List<(string Old, string New)> Replacements { get; } = new();

    public CancellationToken LastCaptureToken { get; private set; }

    public CaptureResult? PreCheck(TargetInfo? target, FocusProbe? probe, CaptureMode mode = CaptureMode.Replace)
    {
        Calls.Add($"precheck {mode}{(probe is null ? string.Empty : " with probe")}");
        return probe is null ? PreCheckResult : PreCheckWithProbeResult ?? PreCheckResult;
    }

    public Task<FocusProbe> ProbeAsync(TargetInfo target)
    {
        Calls.Add("probe");
        return Task.FromResult(Probe);
    }

    public async Task<CaptureResult> CaptureAsync(
        TargetInfo? target, ReadStrategy allowed, FocusProbe? probe = null, CaptureMode mode = CaptureMode.Replace, CancellationToken ct = default)
    {
        Calls.Add($"capture {mode}");
        LastCaptureToken = ct;
        if (CaptureGate is { } gate) await gate.Task;
        if (CaptureThrows is { } ex) throw ex;
        if (CaptureFailure is { } failure) return CaptureResult.Fail(failure);
        return CaptureResult.Ok(new TextCapture(FieldText, TextOrigin.WholeField, ReadStrategy.Uia, target!, TimeSpan.Zero));
    }

    public Task<ReplaceResult> ReplaceAsync(TextCapture capture, string newText, CancellationToken ct = default)
    {
        Calls.Add("replace");

        // Like the real service: a cancel that arrives before Ctrl+V is honoured.
        ct.ThrowIfCancellationRequested();
        if (ReplaceThrows is { } ex) throw ex;
        if (ReplaceFailure != ReplaceFailure.None) return Task.FromResult(new ReplaceResult(ReplaceFailure, TimeSpan.Zero));

        Replacements.Add((capture.Text, newText));
        FieldText = newText;
        return Task.FromResult(new ReplaceResult(ReplaceFailure.None, TimeSpan.Zero));
    }
}

/// <summary>An AI that answers when the test says so, honours the cancel token, and can switch to a fallback provider.</summary>
internal sealed class FakeRunner : IPromptRunner
{
    private TaskCompletionSource<string> _answer = NewAnswer();

    public List<string> Inputs { get; } = new();

    /// <summary>Reported through onFallback before answering.</summary>
    public ProviderFallback? Fallback { get; set; }

    /// <summary>Answer immediately with this text (no need to call <see cref="Answer"/>).</summary>
    public string? AutoAnswer { get; set; } = "the text";

    public CancellationToken LastToken { get; private set; }

    /// <summary>Called right before a complete answer is handed back (e.g. to press Esc at exactly that moment).</summary>
    public Action? BeforeReturn { get; set; }

    public bool Waiting { get; private set; }

    public void Answer(string text) => _answer.TrySetResult(text);

    public void Fail(Exception ex) => _answer.TrySetException(ex);

    public async Task<string> RunAsync(
        PromptDefinition prompt, string input, CancellationToken ct, LlmTimings? timings = null, Action<ProviderFallback>? onFallback = null)
    {
        Inputs.Add(input);
        LastToken = ct;
        ct.ThrowIfCancellationRequested();
        if (Fallback is { } used) onFallback?.Invoke(used);
        if (AutoAnswer is { } text)
        {
            BeforeReturn?.Invoke();
            return text;
        }

        var answer = _answer;
        Waiting = true;
        try
        {
            return await answer.Task.WaitAsync(ct);
        }
        finally
        {
            Waiting = false;
            _answer = NewAnswer();
        }
    }

    /// <summary>Streams the pieces handed in with <see cref="Piece"/>; <see cref="EndStream"/> completes it.</summary>
    public async IAsyncEnumerable<string> StreamAsync(
        PromptDefinition prompt, string input, [EnumeratorCancellation] CancellationToken ct, LlmTimings? timings = null,
        Action<ProviderFallback>? onFallback = null)
    {
        Inputs.Add(input);
        LastToken = ct;
        if (Fallback is { } used) onFallback?.Invoke(used);
        Waiting = true;
        try
        {
            while (true)
            {
                if (!await _pieces.Reader.WaitToReadAsync(ct)) yield break;
                yield return await _pieces.Reader.ReadAsync(ct);
            }
        }
        finally
        {
            Waiting = false;
        }
    }

    private readonly Channel<string> _pieces = Channel.CreateUnbounded<string>();

    public void Piece(string text) => _pieces.Writer.TryWrite(text);

    public void EndStream() => _pieces.Writer.TryComplete();

    public void FailStream(Exception ex) => _pieces.Writer.TryComplete(ex);

    private static TaskCompletionSource<string> NewAnswer() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class FakeOverlay : IOverlayView
{
    public event Action<PromptDefinition>? PromptChosen;

    public event Action? Cancelled;

    public event Action? Dismissed;

    public bool IsShown { get; private set; }

    public bool Activated { get; private set; }

    public Exception? ShowThrows { get; set; }

    public void SetLayout(double width, double minHeight, double maxHeight, double fontSize)
    {
    }

    public void Present(IReadOnlyList<PromptDefinition> prompts)
    {
    }

    public void ShowAnchored(Anchor anchor)
    {
        if (ShowThrows is { } ex) throw ex;
        IsShown = true;
    }

    public void ActivateForInput() => Activated = true;

    public void HideOverlay()
    {
        IsShown = false;
        Activated = false;
    }

    public void Choose(PromptDefinition prompt) => PromptChosen?.Invoke(prompt);

    public void Cancel() => Cancelled?.Invoke();

    public void Dismiss() => Dismissed?.Invoke();
}

/// <summary>Records what the pill shows and raises ActionEnded/ResultClosed the way StatusWindow does.</summary>
internal sealed class FakeStatus : IStatusView
{
    private bool _resultMode;

    public event Action? CancelRequested;

    public event Action? ResultClosed;

    public event Action? ActionEnded;

    public bool IsActionShown { get; private set; }

    public double ResultFontSize { get; set; } = 14;

    /// <summary>Every call, e.g. "progress: …", "error: …", "action: …", "info: …", "hide", "result: …", "append: …", "end".</summary>
    public List<string> Shown { get; } = new();

    public string? Last => Shown.Count == 0 ? null : Shown[^1];

    public string CardText { get; private set; } = string.Empty;

    public void SetResultSize(double width, double minHeight, double maxHeight)
    {
    }

    public void ShowProgress(string text, Anchor anchor)
    {
        Leave();
        Shown.Add($"progress: {text}");
    }

    public void UpdateProgress(string text, Anchor anchor) => Shown.Add($"update: {text}");

    public void ShowError(string text, Anchor anchor)
    {
        Leave();
        Shown.Add($"error: {text}");
    }

    public void ShowInfo(string text, Anchor anchor, bool longer = false)
    {
        Leave();
        Shown.Add($"info: {text}");
    }

    public void ShowErrorWithAction(string text, Anchor anchor)
    {
        Leave();
        Shown.Add($"action: {text}");
        IsActionShown = true;
    }

    public void HideStatus()
    {
        Leave();
        Shown.Add("hide");
    }

    public void BeginResult(string label, Anchor anchor)
    {
        Leave();
        _resultMode = true;
        CardText = string.Empty;
        Shown.Add($"result: {label}");
    }

    public void AppendResult(string piece)
    {
        if (!_resultMode) return;
        CardText += piece;
        Shown.Add($"append: {piece}");
    }

    public void EndResult()
    {
        if (_resultMode) Shown.Add("end");
    }

    public void RunOnUi(Action action) => action();

    /// <summary>A click on the pill.</summary>
    public void Click() => CancelRequested?.Invoke();

    /// <summary>The pill timed out.</summary>
    public void Expire() => HideStatus();

    private void Leave()
    {
        if (IsActionShown)
        {
            IsActionShown = false;
            ActionEnded?.Invoke();
        }

        if (_resultMode)
        {
            _resultMode = false;
            ResultClosed?.Invoke();
        }
    }
}

internal sealed class FakeHotkeys : IHotkeyRegistry
{
    private readonly Dictionary<int, (HotkeyGesture Gesture, Action<long> Callback)> _registered = new();
    private int _next = 100;

    /// <summary>Gestures that another app already holds.</summary>
    public HashSet<HotkeyGesture> Taken { get; } = new();

    public static HotkeyGesture Esc { get; } = new(HotkeyModifiers.None, 0x1B);

    public static HotkeyGesture Copy { get; } = HotkeyGesture.Parse(Kuroko.Core.Config.AppSettings.DefaultResultCopyHotkey);

    public HotkeyRegistration Register(HotkeyGesture gesture, Action<long> callback, bool temporary = false)
    {
        if (Taken.Contains(gesture) || _registered.Values.Any(r => r.Gesture == gesture)) return HotkeyRegistration.Fail($"{gesture} taken");
        var id = _next++;
        _registered[id] = (gesture, callback);
        return HotkeyRegistration.Ok(id);
    }

    public void Unregister(int id) => _registered.Remove(id);

    public bool IsRegistered(HotkeyGesture gesture) => _registered.Values.Any(r => r.Gesture == gesture);

    public int Count => _registered.Count;

    /// <summary>Presses the key; false when nothing is registered for it (the key then goes to the app under it).</summary>
    public bool Press(HotkeyGesture gesture)
    {
        var match = _registered.Values.Where(r => r.Gesture == gesture).ToList();
        foreach (var (_, callback) in match) callback(System.Diagnostics.Stopwatch.GetTimestamp());
        return match.Count > 0;
    }
}

internal sealed class FakeDesktop : IDesktop
{
    private Action? _foregroundChanged;

    public TargetInfo? Target { get; set; } = new(100, 101, "Notepad", 1, "notepad", false, false, false, 7, null);

    public bool FocusComesBack { get; set; } = true;

    public int FocusRestores { get; private set; }

    public bool HighResolution { get; private set; }

    public bool Watching => _foregroundChanged is not null;

    public TargetInfo? CaptureTarget() => Target;

    public Task<bool> RestoreFocusAsync(TargetInfo target)
    {
        FocusRestores++;
        return Task.FromResult(FocusComesBack);
    }

    public void SetHighResolutionTimer(bool on) => HighResolution = on;

    public IDisposable? WatchForeground(nint window, Action changed)
    {
        _foregroundChanged = changed;
        return new Unwatch(this);
    }

    public void AfterNextFrame(Action action)
    {
    }

    /// <summary>Another window came to the front.</summary>
    public void SwitchWindow() => _foregroundChanged?.Invoke();

    private sealed class Unwatch(FakeDesktop desktop) : IDisposable
    {
        public void Dispose() => desktop._foregroundChanged = null;
    }
}

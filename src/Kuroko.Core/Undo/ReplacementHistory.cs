namespace Kuroko.Core.Undo;

/// <summary>Identifies the field a replacement was made in: top-level window, its process, and the focused child window.</summary>
public readonly record struct FieldId(long Window, uint ProcessId, long FocusWindow)
{
    /// <summary>Same window and process; the focused child must match too unless one side does not know it.</summary>
    public bool Matches(FieldId other)
        => Window == other.Window && ProcessId == other.ProcessId
           && (FocusWindow == 0 || other.FocusWindow == 0 || FocusWindow == other.FocusWindow);
}

/// <summary>One replacement: what was there before, what the app put there, and where.</summary>
public sealed record ReplacementRecord(string Original, string Result, FieldId Field);

public enum UndoOutcome
{
    /// <summary>The history is empty.</summary>
    NothingToUndo,

    /// <summary>There is history, but none for the window/field that has the focus now.</summary>
    OtherField,

    /// <summary>The field no longer contains the stored result (it was edited, or the app changed it).</summary>
    TextChanged,

    /// <summary>The stored result occurs more than once, so the place to restore is not certain.</summary>
    Ambiguous,

    /// <summary>Safe to restore: write <see cref="UndoPlan.NewText"/> over the text that was read.</summary>
    Restore,
}

public sealed record UndoPlan(UndoOutcome Outcome, ReplacementRecord? Record = null, string? NewText = null);

/// <summary>
/// The last N replacements, newest last. Memory only: never written to disk and never logged. Decides, from the
/// current focus and the current field text, whether and how a replacement may be undone. Knows nothing about Win32.
/// </summary>
public sealed class ReplacementHistory
{
    private readonly List<ReplacementRecord> _items = [];
    private int _capacity;

    public ReplacementHistory(int capacity = 10) => _capacity = Math.Max(0, capacity);

    public int Count => _items.Count;

    /// <summary>How many replacements are kept (0 = none, undo is off). Lowering it drops the oldest ones.</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Max(0, value);
            Trim();
        }
    }

    public void Add(ReplacementRecord record)
    {
        // A replacement that changed nothing, or produced nothing, has nothing to restore.
        if (_capacity == 0 || record.Result.Length == 0 || record.Original == record.Result) return;
        _items.Add(record);
        Trim();
    }

    public void Clear() => _items.Clear();

    /// <summary>Removes a record after it was undone successfully. A refused undo leaves the history alone.</summary>
    public void Commit(ReplacementRecord record) => _items.Remove(record);

    /// <summary>
    /// Looks at the newest replacement made in <paramref name="field"/> and checks it against
    /// <paramref name="currentText"/> (the selection or whole field as just read).
    /// </summary>
    public UndoPlan Plan(FieldId field, string currentText)
    {
        if (_items.Count == 0) return new UndoPlan(UndoOutcome.NothingToUndo);

        var record = _items.LastOrDefault(r => r.Field.Matches(field));
        if (record is null) return new UndoPlan(UndoOutcome.OtherField);

        // Apps differ in how they store line breaks: look for the result in each common form.
        var lf = Normalize(record.Result);
        var forms = new[] { record.Result, lf, lf.Replace("\n", "\r\n"), lf.Replace("\n", "\r") }.Distinct().ToArray();
        var found = forms.Select(form => (Form: form, Index: IndexOf(currentText, form))).Where(f => f.Index.Count > 0).ToArray();
        if (found.Length == 0) return new UndoPlan(UndoOutcome.TextChanged, record);

        var (match, indexes) = found[0];
        if (found.Length > 1 || indexes.Count > 1) return new UndoPlan(UndoOutcome.Ambiguous, record);

        var at = indexes[0];
        var restored = string.Concat(currentText.AsSpan(0, at), record.Original, currentText.AsSpan(at + match.Length));
        return new UndoPlan(UndoOutcome.Restore, record, restored);
    }

    private void Trim()
    {
        if (_items.Count > _capacity) _items.RemoveRange(0, _items.Count - _capacity);
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static List<int> IndexOf(string text, string value)
    {
        var hits = new List<int>();
        for (var at = text.IndexOf(value, StringComparison.Ordinal); at >= 0; at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal))
        {
            hits.Add(at);
            if (hits.Count > 1) break; // two is already "ambiguous"
        }

        return hits;
    }
}

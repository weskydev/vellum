namespace Vellum.Controls;

/// <summary>
/// Our own Ctrl+Z / Ctrl+Y. The rich text box would also undo our coloring, so its
/// built-in history is off and edits are recorded here as plain text changes.
/// Typing is grouped by words, Backspace / Delete runs are grouped too.
/// </summary>
internal sealed class UndoHistory
{
    public sealed record Edit(int Start, string Removed, string Inserted, DateTime Time);

    private const int Limit = 2000;
    private static readonly TimeSpan GroupWindow = TimeSpan.FromSeconds(1.5);

    private readonly List<Edit> _undo = [];
    private readonly List<Edit> _redo = [];
    private bool _sealed;

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>The next edit starts its own undo step (after moving the caret, pasting, etc).</summary>
    public void Seal() => _sealed = true;

    public void Record(string before, string after, bool group)
    {
        int min = Math.Min(before.Length, after.Length);
        int prefix = 0;
        while (prefix < min && before[prefix] == after[prefix]) prefix++;
        int suffix = 0;
        while (suffix < min - prefix && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix]) suffix++;

        string removed = before.Substring(prefix, before.Length - prefix - suffix);
        string inserted = after.Substring(prefix, after.Length - prefix - suffix);
        if (removed.Length == 0 && inserted.Length == 0) return;

        _redo.Clear();
        DateTime now = DateTime.UtcNow;

        if (group && !_sealed && _undo.Count > 0 && now - _undo[^1].Time < GroupWindow)
        {
            Edit last = _undo[^1];

            // Typing: characters right after the previous ones (fast typing can arrive a few at a time);
            // a new word or line starts a new step
            bool typing = removed.Length == 0 && last.Removed.Length == 0 && inserted.Length is > 0 and <= 8
                && !inserted.Contains('\r') && !last.Inserted.Contains('\r')
                && prefix == last.Start + last.Inserted.Length
                && !(last.Inserted.Length > 0 && char.IsWhiteSpace(last.Inserted[^1]) && !char.IsWhiteSpace(inserted[0]));
            if (typing)
            {
                _undo[^1] = last with { Inserted = last.Inserted + inserted, Time = now };
                return;
            }

            // Backspace run
            if (inserted.Length == 0 && last.Inserted.Length == 0 && removed.Length == 1 && prefix + 1 == last.Start)
            {
                _undo[^1] = last with { Start = prefix, Removed = removed + last.Removed, Time = now };
                return;
            }

            // Delete run
            if (inserted.Length == 0 && last.Inserted.Length == 0 && removed.Length == 1 && prefix == last.Start)
            {
                _undo[^1] = last with { Removed = last.Removed + removed, Time = now };
                return;
            }
        }

        _sealed = false;
        _undo.Add(new Edit(prefix, removed, inserted, now));
        if (_undo.Count > Limit) _undo.RemoveAt(0);
    }

    public Edit? TakeUndo()
    {
        if (_undo.Count == 0) return null;
        Edit e = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(e);
        _sealed = true;
        return e;
    }

    public Edit? TakeRedo()
    {
        if (_redo.Count == 0) return null;
        Edit e = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(e);
        _sealed = true;
        return e;
    }
}

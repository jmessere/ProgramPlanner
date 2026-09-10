namespace WmsResourcePlanner.Application.Services;

/// <summary>
/// A minimal, generic undo stack (SPEC.md Phase 22 / section 55). Mutating
/// services push a delegate that reverts their most recent change; the UI
/// calls UndoAsync() (bound to Ctrl+Z) to pop and invoke the most recent
/// one. Registered as scoped so undo history is per user session/circuit.
/// </summary>
public class UndoService
{
    private const int MaxDepth = 25;
    private readonly List<(string Description, Func<Task> Undo)> _actions = new();

    public bool CanUndo => _actions.Count > 0;

    public string? LastDescription => _actions.Count > 0 ? _actions[^1].Description : null;

    public void Push(string description, Func<Task> undoAction)
    {
        _actions.Add((description, undoAction));
        if (_actions.Count > MaxDepth)
        {
            _actions.RemoveAt(0);
        }
    }

    /// <summary>Reverts the most recent recorded change, if any. Returns true if an undo was performed.</summary>
    public async Task<bool> UndoAsync()
    {
        if (_actions.Count == 0) return false;
        var (_, undo) = _actions[^1];
        _actions.RemoveAt(_actions.Count - 1);
        await undo();
        return true;
    }
}

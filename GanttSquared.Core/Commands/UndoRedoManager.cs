namespace GanttSquared.Core.Commands;

/// <summary>
/// Executes commands and keeps the undo/redo stacks. This is the single entry point
/// the UI layer should use to mutate the project so every change is undoable.
/// </summary>
public sealed class UndoRedoManager
{
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();

    /// <summary>Raised after every Do/Undo/Redo/Clear - the UI's one hook for "something changed, refresh yourself" (e.g. MainViewModel.RebuildTree).</summary>
    public event EventHandler? StateChanged;

    /// <summary>True if there's a command to undo.</summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>True if there's a command to redo (cleared by every new <see cref="Do"/>).</summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>The <see cref="IUndoableCommand.Description"/> of what <see cref="Undo"/> would undo next, or null if <see cref="CanUndo"/> is false.</summary>
    public string? NextUndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;

    /// <summary>The <see cref="IUndoableCommand.Description"/> of what <see cref="Redo"/> would redo next, or null if <see cref="CanRedo"/> is false.</summary>
    public string? NextRedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    /// <summary>Executes a command, pushes it onto the undo stack, and clears the redo stack (a fresh edit invalidates any previously-undone future).</summary>
    public void Do(IUndoableCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Undoes the most recent command and moves it to the redo stack. A no-op if <see cref="CanUndo"/> is false.</summary>
    public void Undo()
    {
        if (_undoStack.Count == 0)
            return;

        var command = _undoStack.Pop();
        command.Undo();
        _redoStack.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Re-executes the most recently undone command and moves it back to the undo stack. A no-op if <see cref="CanRedo"/> is false.</summary>
    public void Redo()
    {
        if (_redoStack.Count == 0)
            return;

        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Discards both stacks without undoing anything - for when a fresh/loaded project makes the prior history meaningless (New/Open).</summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}

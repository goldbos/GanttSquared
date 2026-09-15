namespace GanttSquared.Core.Commands;

/// <summary>
/// A single reversible edit to a <see cref="Model.ProjectModel"/>. Every change the app makes
/// to project data goes through one of these (via <see cref="UndoRedoManager.Do"/>) rather than
/// mutating the model directly, so it can be undone/redone uniformly.
/// </summary>
public interface IUndoableCommand
{
    /// <summary>Short, human-readable label for this edit (not currently surfaced in the UI, but useful for debugging/logging).</summary>
    string Description { get; }

    /// <summary>Applies the edit. Called once by <see cref="UndoRedoManager.Do"/>, and again by <see cref="UndoRedoManager.Redo"/>.</summary>
    void Execute();

    /// <summary>Reverses the edit applied by <see cref="Execute"/>, restoring the prior state exactly.</summary>
    void Undo();
}

using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>
/// Generic undoable edit for a single simple field on a task (name, priority, progress,
/// color, description, ...). Date/duration changes go through RescheduleTaskCommand instead,
/// since those can cascade to other tasks.
/// </summary>
public sealed class EditTaskFieldCommand<T> : IUndoableCommand
{
    private readonly GanttTask _task;
    private readonly Func<GanttTask, T> _getter;
    private readonly Action<GanttTask, T> _setter;
    private readonly T _newValue;
    private readonly string _fieldName;
    private T? _oldValue;

    public string Description => $"Change {_fieldName} of '{_task.Name}'";

    public EditTaskFieldCommand(GanttTask task, string fieldName, Func<GanttTask, T> getter, Action<GanttTask, T> setter, T newValue)
    {
        _task = task;
        _fieldName = fieldName;
        _getter = getter;
        _setter = setter;
        _newValue = newValue;
    }

    public void Execute()
    {
        _oldValue = _getter(_task);
        _setter(_task, _newValue);
    }

    public void Undo() => _setter(_task, _oldValue!);
}

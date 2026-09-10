using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>
/// Toggles a task's milestone flag. Unlike a plain field edit, this must also restore the
/// task's dates on undo, since turning a task into a milestone collapses its end date to its start.
/// </summary>
public sealed class SetMilestoneCommand : IUndoableCommand
{
    private readonly GanttTask _task;
    private readonly bool _newValue;
    private bool _oldValue;
    private DateOnly _oldStart;
    private DateOnly _oldEnd;

    public string Description => _newValue ? $"Mark '{_task.Name}' as milestone" : $"Unmark '{_task.Name}' as milestone";

    public SetMilestoneCommand(GanttTask task, bool newValue)
    {
        _task = task;
        _newValue = newValue;
    }

    public void Execute()
    {
        _oldValue = _task.IsMilestone;
        _oldStart = _task.StartDate;
        _oldEnd = _task.EndDate;
        _task.SetMilestone(_newValue);
    }

    public void Undo()
    {
        _task.SetMilestone(_oldValue);
        _task.SetDates(_oldStart, _oldEnd);
    }
}

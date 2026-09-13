using GanttSquared.Core.Model;
using GanttSquared.Core.Scheduling;

namespace GanttSquared.Core.Commands;

/// <summary>
/// Moves or resizes a task to new dates. By default cascades the change to dependent
/// successors (auto-reschedule), with all affected tasks captured so the whole cascade undoes
/// as one step; pass cascade: false to move only this task, leaving successors where they are
/// even if that now violates their dependency constraint (used for a plain, non-Shift drag).
/// </summary>
public sealed class RescheduleTaskCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly Guid _taskId;
    private readonly DateOnly _newStart;
    private readonly DateOnly _newEnd;
    private readonly bool _cascade;

    private List<(Guid TaskId, DateOnly OldStart, DateOnly OldEnd)> _previousDates = new();

    public string Description => "Reschedule task";

    public RescheduleTaskCommand(ProjectModel project, Guid taskId, DateOnly newStart, DateOnly newEnd, bool cascade = true)
    {
        _project = project;
        _taskId = taskId;
        _newStart = newStart;
        _newEnd = newEnd;
        _cascade = cascade;
    }

    public void Execute()
    {
        var cascade = _cascade
            ? SchedulingEngine.ComputeCascade(_project, _taskId, _newStart, _newEnd)
            : new[] { new TaskDateChange(_taskId, _newStart, _newEnd) };

        _previousDates = cascade
            .Select(c =>
            {
                var task = _project.FindTask(c.TaskId)!;
                return (c.TaskId, task.StartDate, task.EndDate);
            })
            .ToList();

        foreach (var change in cascade)
            _project.FindTask(change.TaskId)!.SetDates(change.NewStart, change.NewEnd);
    }

    public void Undo()
    {
        foreach (var (taskId, oldStart, oldEnd) in _previousDates)
            _project.FindTask(taskId)!.SetDates(oldStart, oldEnd);
    }
}

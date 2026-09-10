using GanttSquared.Core.Model;
using GanttSquared.Core.Scheduling;

namespace GanttSquared.Core.Commands;

/// <summary>
/// Moves or resizes a task to new dates and cascades the change to dependent successors
/// (auto-reschedule). All affected tasks are captured so the whole cascade undoes as one step.
/// </summary>
public sealed class RescheduleTaskCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly Guid _taskId;
    private readonly DateOnly _newStart;
    private readonly DateOnly _newEnd;

    private List<(Guid TaskId, DateOnly OldStart, DateOnly OldEnd)> _previousDates = new();

    public string Description => "Reschedule task";

    public RescheduleTaskCommand(ProjectModel project, Guid taskId, DateOnly newStart, DateOnly newEnd)
    {
        _project = project;
        _taskId = taskId;
        _newStart = newStart;
        _newEnd = newEnd;
    }

    public void Execute()
    {
        var cascade = SchedulingEngine.ComputeCascade(_project, _taskId, _newStart, _newEnd);

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

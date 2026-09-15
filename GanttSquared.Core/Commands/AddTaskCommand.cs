using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>Adds a new task to the project; undoing removes it (and cascades to anything added under it since, same as a manual delete).</summary>
public sealed class AddTaskCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly GanttTask _task;
    private readonly int? _insertAtIndex;

    public string Description => $"Add task '{_task.Name}'";

    public AddTaskCommand(ProjectModel project, GanttTask task, int? insertAtIndex = null)
    {
        _project = project;
        _task = task;
        _insertAtIndex = insertAtIndex;
    }

    public void Execute() => _project.AddTask(_task, _insertAtIndex);

    public void Undo() => _project.RemoveTaskCascading(_task.Id, out _, out _);
}

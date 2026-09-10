using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>Deletes a task along with all of its descendants and any dependency links touching them.</summary>
public sealed class DeleteTaskCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly Guid _taskId;

    private List<GanttTask> _removedTasks = new();
    private List<DependencyLink> _removedDependencies = new();
    private string _taskName = string.Empty;

    public string Description => $"Delete task '{_taskName}'";

    public DeleteTaskCommand(ProjectModel project, Guid taskId)
    {
        _project = project;
        _taskId = taskId;
    }

    public void Execute()
    {
        _taskName = _project.FindTask(_taskId)?.Name ?? "";
        _project.RemoveTaskCascading(_taskId, out _removedTasks, out _removedDependencies);
    }

    public void Undo()
    {
        _project.RestoreTasks(_removedTasks);
        _project.RestoreDependencies(_removedDependencies);
    }
}

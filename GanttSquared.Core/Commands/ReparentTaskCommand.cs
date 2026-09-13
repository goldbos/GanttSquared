using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>Moves a task (and its subtree) to become the last child of a different parent task, e.g. via drag-and-drop in the task list.</summary>
public sealed class ReparentTaskCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly Guid _taskId;
    private readonly Guid? _newParentId;
    private List<(Guid Id, Guid? ParentId, int OrderIndex)> _snapshot = new();

    public string Description => "Move task";

    public ReparentTaskCommand(ProjectModel project, Guid taskId, Guid? newParentId)
    {
        _project = project;
        _taskId = taskId;
        _newParentId = newParentId;
    }

    public void Execute()
    {
        var task = _project.FindTask(_taskId) ?? throw new InvalidOperationException("Task not found.");

        if (_newParentId == _taskId)
            throw new InvalidOperationException("A task cannot become its own parent.");

        if (_newParentId is { } newParentId && IsDescendant(newParentId, _taskId))
            throw new InvalidOperationException("Cannot move a task under one of its own descendants.");

        if (task.ParentId == _newParentId)
            return; // Already there; nothing to do.

        var oldSiblings = _project.GetChildren(task.ParentId).OrderBy(t => t.OrderIndex).ToList();
        var newSiblingsBefore = _project.GetChildren(_newParentId).ToList();

        var touched = oldSiblings
            .Concat(newSiblingsBefore)
            .DistinctBy(t => t.Id)
            .ToList();
        _snapshot = touched.Select(t => (t.Id, t.ParentId, t.OrderIndex)).ToList();

        var remainingOldSiblings = oldSiblings.Where(t => t.Id != _taskId).ToList();
        for (var i = 0; i < remainingOldSiblings.Count; i++)
            remainingOldSiblings[i].OrderIndex = i;

        task.ParentId = _newParentId;
        task.OrderIndex = newSiblingsBefore.Count;
    }

    public void Undo()
    {
        foreach (var (id, parentId, orderIndex) in _snapshot)
        {
            var t = _project.FindTask(id)!;
            t.ParentId = parentId;
            t.OrderIndex = orderIndex;
        }
    }

    private bool IsDescendant(Guid candidateId, Guid ofTaskId) =>
        _project.GetDescendants(ofTaskId).Any(t => t.Id == candidateId);
}

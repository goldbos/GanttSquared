using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>Makes a task a child of its immediately preceding sibling.</summary>
public sealed class IndentTaskCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly Guid _taskId;
    private List<(Guid Id, Guid? ParentId, int OrderIndex)> _snapshot = new();

    public string Description => "Indent task";

    public IndentTaskCommand(ProjectModel project, Guid taskId)
    {
        _project = project;
        _taskId = taskId;
    }

    public void Execute()
    {
        var task = _project.FindTask(_taskId) ?? throw new InvalidOperationException("Task not found.");

        var oldSiblings = _project.GetChildren(task.ParentId).OrderBy(t => t.OrderIndex).ToList();
        var idx = oldSiblings.FindIndex(t => t.Id == _taskId);
        if (idx <= 0)
            throw new InvalidOperationException("Cannot indent: no preceding sibling to become the parent.");

        var newParent = oldSiblings[idx - 1];
        var newParentChildrenBefore = _project.GetChildren(newParent.Id).ToList();

        var touched = oldSiblings
            .Concat(newParentChildrenBefore)
            .DistinctBy(t => t.Id)
            .ToList();
        _snapshot = touched.Select(t => (t.Id, t.ParentId, t.OrderIndex)).ToList();

        // Close the gap left in the old sibling list.
        var remainingOldSiblings = oldSiblings.Where(t => t.Id != _taskId).ToList();
        for (var i = 0; i < remainingOldSiblings.Count; i++)
            remainingOldSiblings[i].OrderIndex = i;

        task.ParentId = newParent.Id;
        task.OrderIndex = newParentChildrenBefore.Count;
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
}

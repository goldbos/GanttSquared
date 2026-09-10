using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>Moves a task out of its parent, placing it as a sibling immediately after its former parent.</summary>
public sealed class OutdentTaskCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly Guid _taskId;
    private List<(Guid Id, Guid? ParentId, int OrderIndex)> _snapshot = new();

    public string Description => "Outdent task";

    public OutdentTaskCommand(ProjectModel project, Guid taskId)
    {
        _project = project;
        _taskId = taskId;
    }

    public void Execute()
    {
        var task = _project.FindTask(_taskId) ?? throw new InvalidOperationException("Task not found.");
        if (task.ParentId is null)
            throw new InvalidOperationException("Cannot outdent a top-level task.");

        var parent = _project.FindTask(task.ParentId.Value) ?? throw new InvalidOperationException("Parent task not found.");
        var grandParentId = parent.ParentId;

        var oldSiblings = _project.GetChildren(task.ParentId).OrderBy(t => t.OrderIndex).ToList();
        var grandParentChildren = _project.GetChildren(grandParentId).OrderBy(t => t.OrderIndex).ToList();

        var touched = oldSiblings
            .Concat(grandParentChildren)
            .DistinctBy(t => t.Id)
            .ToList();
        _snapshot = touched.Select(t => (t.Id, t.ParentId, t.OrderIndex)).ToList();

        // Close the gap left in the old sibling list.
        var remainingOldSiblings = oldSiblings.Where(t => t.Id != _taskId).ToList();
        for (var i = 0; i < remainingOldSiblings.Count; i++)
            remainingOldSiblings[i].OrderIndex = i;

        // Reinsert the task among its new siblings, immediately after its former parent.
        var newOrder = new List<GanttTask>();
        foreach (var sibling in grandParentChildren)
        {
            newOrder.Add(sibling);
            if (sibling.Id == parent.Id)
                newOrder.Add(task);
        }

        task.ParentId = grandParentId;
        for (var i = 0; i < newOrder.Count; i++)
            newOrder[i].OrderIndex = i;
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

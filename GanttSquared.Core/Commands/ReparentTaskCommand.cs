using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>
/// Moves a task (and its subtree) to become a child of a different parent task (or the root,
/// for a null parent), e.g. via drag-and-drop in the task list. By default it's appended as
/// the new parent's last child; pass insertBeforeTaskId to place it at a specific position
/// among its new siblings instead - including reordering among the *same* siblings it already
/// has, unlike a plain reparent-with-no-position-given, which no-ops if the parent isn't
/// actually changing.
/// </summary>
public sealed class ReparentTaskCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly Guid _taskId;
    private readonly Guid? _newParentId;
    private readonly Guid? _insertBeforeTaskId;
    private List<(Guid Id, Guid? ParentId, int OrderIndex)> _snapshot = new();

    public string Description => "Move task";

    public ReparentTaskCommand(ProjectModel project, Guid taskId, Guid? newParentId, Guid? insertBeforeTaskId = null)
    {
        _project = project;
        _taskId = taskId;
        _newParentId = newParentId;
        _insertBeforeTaskId = insertBeforeTaskId;
    }

    public void Execute()
    {
        var task = _project.FindTask(_taskId) ?? throw new InvalidOperationException("Task not found.");

        if (_newParentId == _taskId)
            throw new InvalidOperationException("A task cannot become its own parent.");

        if (_newParentId is { } newParentId && IsDescendant(newParentId, _taskId))
            throw new InvalidOperationException("Cannot move a task under one of its own descendants.");

        var sameParent = task.ParentId == _newParentId;
        if (sameParent && _insertBeforeTaskId is null)
            return; // Already there, and no specific position requested; nothing to do.

        var oldSiblings = _project.GetChildren(task.ParentId).OrderBy(t => t.OrderIndex).ToList();

        // The set of siblings the task will actually be inserted among, in their current
        // relative order, with the task itself excluded either way (it was never in the new
        // parent's list for a cross-parent move, and is removed here for a same-parent reorder).
        var newParentChildren = sameParent
            ? oldSiblings
            : _project.GetChildren(_newParentId).OrderBy(t => t.OrderIndex).ToList();
        var newSiblingsBefore = newParentChildren.Where(t => t.Id != _taskId).ToList();

        var touched = oldSiblings
            .Concat(newSiblingsBefore)
            .DistinctBy(t => t.Id)
            .ToList();
        _snapshot = touched.Select(t => (t.Id, t.ParentId, t.OrderIndex)).ToList();

        if (!sameParent)
        {
            var remainingOldSiblings = oldSiblings.Where(t => t.Id != _taskId).ToList();
            for (var i = 0; i < remainingOldSiblings.Count; i++)
                remainingOldSiblings[i].OrderIndex = i;
        }

        task.ParentId = _newParentId;

        var insertIndex = newSiblingsBefore.Count;
        if (_insertBeforeTaskId is { } beforeId)
        {
            var idx = newSiblingsBefore.FindIndex(t => t.Id == beforeId);
            if (idx >= 0)
                insertIndex = idx;
        }

        for (var i = 0; i < newSiblingsBefore.Count; i++)
            newSiblingsBefore[i].OrderIndex = i >= insertIndex ? i + 1 : i;
        task.OrderIndex = insertIndex;
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

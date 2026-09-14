using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>Removes a resource and, since ProjectModel.RemoveResource also strips it from every
/// task's AssignedResourceIds, restores those assignments too on undo.</summary>
public sealed class RemoveResourceCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly Guid _resourceId;
    private ProjectResource? _removedResource;
    private List<Guid> _affectedTaskIds = new();

    public string Description => "Remove resource";

    public RemoveResourceCommand(ProjectModel project, Guid resourceId)
    {
        _project = project;
        _resourceId = resourceId;
    }

    public void Execute()
    {
        _removedResource = _project.Resources.FirstOrDefault(r => r.Id == _resourceId);
        _affectedTaskIds = _project.Tasks
            .Where(t => t.AssignedResourceIds.Contains(_resourceId))
            .Select(t => t.Id)
            .ToList();

        _project.RemoveResource(_resourceId);
    }

    public void Undo()
    {
        if (_removedResource is null)
            return;

        _project.AddResource(_removedResource);
        foreach (var taskId in _affectedTaskIds)
        {
            var task = _project.FindTask(taskId);
            if (task is not null && !task.AssignedResourceIds.Contains(_resourceId))
                task.AssignedResourceIds.Add(_resourceId);
        }
    }
}

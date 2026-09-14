using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

public sealed class AddResourceCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly ProjectResource _resource;

    public string Description => $"Add resource '{_resource.Name}'";

    public AddResourceCommand(ProjectModel project, ProjectResource resource)
    {
        _project = project;
        _resource = resource;
    }

    public void Execute() => _project.AddResource(_resource);

    public void Undo() => _project.RemoveResource(_resource.Id);
}

using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>Adds a resource to the project; undoing removes it again (and any assignments, since it never had any yet).</summary>
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

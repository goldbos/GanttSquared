using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

public sealed class RemoveDependencyCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly Guid _dependencyId;
    private DependencyLink? _removed;

    public string Description => "Remove dependency";

    public RemoveDependencyCommand(ProjectModel project, Guid dependencyId)
    {
        _project = project;
        _dependencyId = dependencyId;
    }

    public void Execute()
    {
        _removed = _project.Dependencies.FirstOrDefault(d => d.Id == _dependencyId);
        _project.RemoveDependency(_dependencyId);
    }

    public void Undo()
    {
        if (_removed is not null)
            _project.AddDependency(_removed);
    }
}

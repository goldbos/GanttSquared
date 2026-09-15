using GanttSquared.Core.Model;

namespace GanttSquared.Core.Commands;

/// <summary>Adds a dependency link between two tasks; undoing removes it. See <see cref="ProjectModel.AddDependency"/> for the validation this enforces (no cycles, no duplicates).</summary>
public sealed class AddDependencyCommand : IUndoableCommand
{
    private readonly ProjectModel _project;
    private readonly DependencyLink _link;

    public string Description => "Add dependency";

    public AddDependencyCommand(ProjectModel project, DependencyLink link)
    {
        _project = project;
        _link = link;
    }

    public void Execute() => _project.AddDependency(_link);

    public void Undo() => _project.RemoveDependency(_link.Id);
}

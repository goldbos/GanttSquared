namespace GanttSquared.Core.Commands;

/// <summary>Bundles several commands into a single undo/redo step, e.g. for a bulk action over multiple selected tasks.</summary>
public sealed class CompositeCommand : IUndoableCommand
{
    private readonly List<IUndoableCommand> _commands;

    public string Description { get; }

    public CompositeCommand(IEnumerable<IUndoableCommand> commands, string? description = null)
    {
        _commands = commands.ToList();
        Description = description ?? $"{_commands.Count} changes";
    }

    public void Execute()
    {
        foreach (var command in _commands)
            command.Execute();
    }

    public void Undo()
    {
        for (var i = _commands.Count - 1; i >= 0; i--)
            _commands[i].Undo();
    }
}

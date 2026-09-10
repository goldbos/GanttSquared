using GanttSquared.Core.Commands;
using GanttSquared.Core.Model;

namespace GanttSquared.Core.Tests;

public class SetMilestoneCommandTests
{
    [Fact]
    public void Execute_CollapsesEndToStart_AndUndoRestoresOriginalEnd()
    {
        var task = new GanttTask("T", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));
        var manager = new UndoRedoManager();

        manager.Do(new SetMilestoneCommand(task, true));

        Assert.True(task.IsMilestone);
        Assert.Equal(task.StartDate, task.EndDate);

        manager.Undo();

        Assert.False(task.IsMilestone);
        Assert.Equal(new DateOnly(2026, 1, 10), task.EndDate);
    }
}

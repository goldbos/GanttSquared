using GanttSquared.Core.Model;

namespace GanttSquared.Core.Tests;

public class GanttTaskTests
{
    [Fact]
    public void MoveTo_PreservesDuration()
    {
        var task = new GanttTask("T", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        task.MoveTo(new DateOnly(2026, 2, 1));

        Assert.Equal(new DateOnly(2026, 2, 1), task.StartDate);
        Assert.Equal(new DateOnly(2026, 2, 5), task.EndDate);
        Assert.Equal(4, task.DurationDays);
    }

    [Fact]
    public void ResizeTo_KeepsStartFixed()
    {
        var task = new GanttTask("T", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        task.ResizeTo(new DateOnly(2026, 1, 10));

        Assert.Equal(new DateOnly(2026, 1, 1), task.StartDate);
        Assert.Equal(new DateOnly(2026, 1, 10), task.EndDate);
    }

    [Fact]
    public void ResizeTo_BeforeStart_Throws()
    {
        var task = new GanttTask("T", new DateOnly(2026, 1, 5), new DateOnly(2026, 1, 10));

        Assert.Throws<ArgumentException>(() => task.ResizeTo(new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public void SetMilestone_CollapsesEndToStart()
    {
        var task = new GanttTask("T", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10));

        task.SetMilestone(true);

        Assert.Equal(task.StartDate, task.EndDate);
        Assert.Equal(0, task.DurationDays);
    }

    [Fact]
    public void SetDurationDays_MovesEndDate()
    {
        var task = new GanttTask("T", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));

        task.SetDurationDays(7);

        Assert.Equal(new DateOnly(2026, 1, 8), task.EndDate);
    }
}

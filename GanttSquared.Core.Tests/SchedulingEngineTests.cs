using GanttSquared.Core.Model;
using GanttSquared.Core.Scheduling;

namespace GanttSquared.Core.Tests;

public class SchedulingEngineTests
{
    private static GanttTask MakeTask(string name, DateOnly start, DateOnly end) => new(name, start, end);

    [Fact]
    public void FinishToStart_PushesSuccessorWhenPredecessorEndsLater()
    {
        var project = new ProjectModel();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 10));
        project.AddTask(a);
        project.AddTask(b);
        project.AddDependency(new DependencyLink(a.Id, b.Id, DependencyType.FinishToStart));

        var cascade = SchedulingEngine.ComputeCascade(project, a.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8));

        var bChange = cascade.Single(c => c.TaskId == b.Id);
        Assert.Equal(new DateOnly(2026, 1, 8), bChange.NewStart);
        Assert.Equal(new DateOnly(2026, 1, 12), bChange.NewEnd); // duration (4 days) preserved
    }

    [Fact]
    public void FinishToStart_PullsSuccessorEarlierWhenPredecessorMovesEarlier()
    {
        var project = new ProjectModel();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 25)); // 5-day duration
        project.AddTask(a);
        project.AddTask(b);
        project.AddDependency(new DependencyLink(a.Id, b.Id, DependencyType.FinishToStart));

        // Shrink A so it ends much earlier; with nothing else holding B back, it should follow.
        var cascade = SchedulingEngine.ComputeCascade(project, a.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));

        var bChange = cascade.Single(c => c.TaskId == b.Id);
        Assert.Equal(new DateOnly(2026, 1, 2), bChange.NewStart);
        Assert.Equal(new DateOnly(2026, 1, 7), bChange.NewEnd); // duration (5 days) preserved
    }

    [Fact]
    public void MultiplePredecessors_SuccessorNotPulledEarlierThanTheOtherStillRequires()
    {
        var project = new ProjectModel();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var a2 = MakeTask("A2", new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 15));
        var b = MakeTask("B", new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 25));
        project.AddTask(a);
        project.AddTask(a2);
        project.AddTask(b);
        project.AddDependency(new DependencyLink(a.Id, b.Id, DependencyType.FinishToStart));
        project.AddDependency(new DependencyLink(a2.Id, b.Id, DependencyType.FinishToStart));

        // A moves much earlier, but A2 (unchanged) still requires B to start no earlier than Jan 15.
        var cascade = SchedulingEngine.ComputeCascade(project, a.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2));

        var bChange = cascade.Single(c => c.TaskId == b.Id);
        Assert.Equal(new DateOnly(2026, 1, 15), bChange.NewStart); // held back by A2, not pulled to follow A
    }

    [Fact]
    public void Cascade_PropagatesThroughChain()
    {
        var project = new ProjectModel();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 10));
        var c = MakeTask("C", new DateOnly(2026, 1, 11), new DateOnly(2026, 1, 15));
        project.AddTask(a);
        project.AddTask(b);
        project.AddTask(c);
        project.AddDependency(new DependencyLink(a.Id, b.Id, DependencyType.FinishToStart));
        project.AddDependency(new DependencyLink(b.Id, c.Id, DependencyType.FinishToStart));

        var cascade = SchedulingEngine.ComputeCascade(project, a.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 20));

        Assert.Equal(3, cascade.Count);
        var cChange = cascade.Single(x => x.TaskId == c.Id);
        Assert.Equal(new DateOnly(2026, 1, 24), cChange.NewStart);
    }

    [Fact]
    public void LagDays_DelaysSuccessorFurther()
    {
        var project = new ProjectModel();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 10));
        project.AddTask(a);
        project.AddTask(b);
        project.AddDependency(new DependencyLink(a.Id, b.Id, DependencyType.FinishToStart, lagDays: 3));

        var cascade = SchedulingEngine.ComputeCascade(project, a.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        var bChange = cascade.Single(c => c.TaskId == b.Id);
        Assert.Equal(new DateOnly(2026, 1, 8), bChange.NewStart); // Jan 5 + 3 days lag
    }
}

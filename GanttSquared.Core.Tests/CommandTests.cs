using GanttSquared.Core.Commands;
using GanttSquared.Core.Model;

namespace GanttSquared.Core.Tests;

public class CommandTests
{
    private static GanttTask MakeTask(string name, DateOnly start, DateOnly end) => new(name, start, end);

    [Fact]
    public void UndoRedo_AddTask_RoundTrips()
    {
        var project = new ProjectModel();
        var manager = new UndoRedoManager();
        var task = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        manager.Do(new AddTaskCommand(project, task));
        Assert.Single(project.Tasks);

        manager.Undo();
        Assert.Empty(project.Tasks);

        manager.Redo();
        Assert.Single(project.Tasks);
    }

    [Fact]
    public void UndoRedo_EditField_RestoresOldValue()
    {
        var task = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var manager = new UndoRedoManager();

        var cmd = new EditTaskFieldCommand<string>(task, "Name", t => t.Name, (t, v) => t.Name = v, "Renamed");
        manager.Do(cmd);
        Assert.Equal("Renamed", task.Name);

        manager.Undo();
        Assert.Equal("A", task.Name);
    }

    [Fact]
    public void UndoRedo_RescheduleCascade_RestoresAllTasks()
    {
        var project = new ProjectModel();
        var manager = new UndoRedoManager();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 10));
        project.AddTask(a);
        project.AddTask(b);
        project.AddDependency(new DependencyLink(a.Id, b.Id, DependencyType.FinishToStart));

        manager.Do(new RescheduleTaskCommand(project, a.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8)));

        Assert.Equal(new DateOnly(2026, 1, 8), a.EndDate);
        Assert.Equal(new DateOnly(2026, 1, 8), b.StartDate);

        manager.Undo();

        Assert.Equal(new DateOnly(2026, 1, 5), a.EndDate);
        Assert.Equal(new DateOnly(2026, 1, 6), b.StartDate);
    }

    [Fact]
    public void Do_ClearsRedoStack()
    {
        var project = new ProjectModel();
        var manager = new UndoRedoManager();
        var t1 = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var t2 = MakeTask("B", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        manager.Do(new AddTaskCommand(project, t1));
        manager.Undo();
        Assert.True(manager.CanRedo);

        manager.Do(new AddTaskCommand(project, t2));
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void IndentTask_MakesItChildOfPrecedingSibling()
    {
        var project = new ProjectModel();
        var manager = new UndoRedoManager();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        project.AddTask(a);
        project.AddTask(b);

        manager.Do(new IndentTaskCommand(project, b.Id));

        Assert.Equal(a.Id, b.ParentId);
        Assert.Equal(0, b.OrderIndex);

        manager.Undo();

        Assert.Null(b.ParentId);
        Assert.Equal(1, b.OrderIndex);
    }

    [Fact]
    public void OutdentTask_MovesToGrandparentLevel()
    {
        var project = new ProjectModel();
        var manager = new UndoRedoManager();
        var root = MakeTask("Root", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var child = MakeTask("Child", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var grandchild = MakeTask("Grandchild", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        project.AddTask(root);
        child.ParentId = root.Id;
        project.AddTask(child);
        grandchild.ParentId = child.Id;
        project.AddTask(grandchild);

        manager.Do(new OutdentTaskCommand(project, grandchild.Id));

        Assert.Equal(root.Id, grandchild.ParentId);

        manager.Undo();

        Assert.Equal(child.Id, grandchild.ParentId);
    }

    [Fact]
    public void DeleteTask_ThenUndo_RestoresTaskAndDependencies()
    {
        var project = new ProjectModel();
        var manager = new UndoRedoManager();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 10));
        project.AddTask(a);
        project.AddTask(b);
        project.AddDependency(new DependencyLink(a.Id, b.Id));

        manager.Do(new DeleteTaskCommand(project, a.Id));
        Assert.Null(project.FindTask(a.Id));
        Assert.Empty(project.Dependencies);

        manager.Undo();
        Assert.NotNull(project.FindTask(a.Id));
        Assert.Single(project.Dependencies);
    }

    [Fact]
    public void RescheduleTaskCommand_NonCascading_LeavesSuccessorInPlace()
    {
        var project = new ProjectModel();
        var manager = new UndoRedoManager();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 6), new DateOnly(2026, 1, 10));
        project.AddTask(a);
        project.AddTask(b);
        project.AddDependency(new DependencyLink(a.Id, b.Id, DependencyType.FinishToStart));

        manager.Do(new RescheduleTaskCommand(project, a.Id, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 8), cascade: false));

        Assert.Equal(new DateOnly(2026, 1, 8), a.EndDate);
        Assert.Equal(new DateOnly(2026, 1, 6), b.StartDate); // unchanged, even though it now overlaps A

        manager.Undo();
        Assert.Equal(new DateOnly(2026, 1, 5), a.EndDate);
    }

    [Fact]
    public void CompositeCommand_UndoesAllStepsInReverseOrder()
    {
        var project = new ProjectModel();
        var manager = new UndoRedoManager();
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

        manager.Do(new CompositeCommand(new IUndoableCommand[]
        {
            new AddTaskCommand(project, a),
            new AddTaskCommand(project, b)
        }));

        Assert.Equal(2, project.Tasks.Count);

        manager.Undo();
        Assert.Empty(project.Tasks);
    }

    [Fact]
    public void ReparentTaskCommand_MovesTaskUnderNewParent_AndUndoRestores()
    {
        var project = new ProjectModel();
        var manager = new UndoRedoManager();
        var oldParent = MakeTask("OldParent", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var newParent = MakeTask("NewParent", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var child = MakeTask("Child", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        project.AddTask(oldParent);
        project.AddTask(newParent);
        child.ParentId = oldParent.Id;
        project.AddTask(child);

        manager.Do(new ReparentTaskCommand(project, child.Id, newParent.Id));

        Assert.Equal(newParent.Id, child.ParentId);

        manager.Undo();
        Assert.Equal(oldParent.Id, child.ParentId);
    }

    [Fact]
    public void ReparentTaskCommand_InsertBeforeTarget_PlacesAtThatPosition()
    {
        var project = new ProjectModel();
        var parent = MakeTask("Parent", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var moving = MakeTask("Moving", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        project.AddTask(parent);
        a.ParentId = parent.Id;
        project.AddTask(a);
        b.ParentId = parent.Id;
        project.AddTask(b);
        project.AddTask(moving);

        new ReparentTaskCommand(project, moving.Id, parent.Id, insertBeforeTaskId: b.Id).Execute();

        var siblings = project.GetChildren(parent.Id).OrderBy(t => t.OrderIndex).Select(t => t.Name).ToList();
        Assert.Equal(new[] { "A", "Moving", "B" }, siblings);
    }

    [Fact]
    public void ReparentTaskCommand_ReordersWithinSameParent()
    {
        var project = new ProjectModel();
        var parent = MakeTask("Parent", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var a = MakeTask("A", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var b = MakeTask("B", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var c = MakeTask("C", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        project.AddTask(parent);
        a.ParentId = parent.Id;
        project.AddTask(a);
        b.ParentId = parent.Id;
        project.AddTask(b);
        c.ParentId = parent.Id;
        project.AddTask(c);

        var manager = new UndoRedoManager();
        manager.Do(new ReparentTaskCommand(project, c.Id, parent.Id, insertBeforeTaskId: a.Id));

        Assert.Equal(new[] { "C", "A", "B" }, project.GetChildren(parent.Id).OrderBy(t => t.OrderIndex).Select(t => t.Name));

        manager.Undo();
        Assert.Equal(new[] { "A", "B", "C" }, project.GetChildren(parent.Id).OrderBy(t => t.OrderIndex).Select(t => t.Name));
    }

    [Fact]
    public void ReparentTaskCommand_UnderOwnDescendant_Throws()
    {
        var project = new ProjectModel();
        var parent = MakeTask("Parent", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        var child = MakeTask("Child", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        project.AddTask(parent);
        child.ParentId = parent.Id;
        project.AddTask(child);

        Assert.Throws<InvalidOperationException>(() => new ReparentTaskCommand(project, parent.Id, child.Id).Execute());
    }
}

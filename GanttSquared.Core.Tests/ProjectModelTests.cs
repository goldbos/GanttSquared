using GanttSquared.Core.Model;

namespace GanttSquared.Core.Tests;

public class ProjectModelTests
{
    private static GanttTask MakeTask(string name) =>
        new(name, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));

    [Fact]
    public void ReplaceContents_SwapsDataButKeepsInstanceIdentity()
    {
        var project = new ProjectModel { Name = "Original" };
        var original = MakeTask("Original Task");
        project.AddTask(original);

        var loaded = new ProjectModel { Name = "Loaded", UseWbsNumbering = true };
        var loadedTask = MakeTask("Loaded Task");
        loaded.AddTask(loadedTask);
        var resource = new ProjectResource { Name = "Bob" };
        loaded.AddResource(resource);

        project.ReplaceContents(loaded);

        Assert.Equal("Loaded", project.Name);
        Assert.True(project.UseWbsNumbering);
        Assert.Null(project.FindTask(original.Id));
        Assert.NotNull(project.FindTask(loadedTask.Id));
        Assert.Single(project.Resources);
        Assert.Equal("Bob", project.Resources[0].Name);
    }

    [Fact]
    public void AddTask_AssignsOrderIndex()
    {
        var project = new ProjectModel();
        var t1 = MakeTask("A");
        var t2 = MakeTask("B");

        project.AddTask(t1);
        project.AddTask(t2);

        Assert.Equal(0, t1.OrderIndex);
        Assert.Equal(1, t2.OrderIndex);
    }

    [Fact]
    public void RemoveTaskCascading_RemovesDescendantsAndDependencies()
    {
        var project = new ProjectModel();
        var parent = MakeTask("Parent");
        var child = MakeTask("Child");
        child.ParentId = parent.Id;
        var unrelated = MakeTask("Unrelated");

        project.AddTask(parent);
        project.AddTask(child);
        project.AddTask(unrelated);
        project.AddDependency(new DependencyLink(child.Id, unrelated.Id));

        project.RemoveTaskCascading(parent.Id, out var removedTasks, out var removedDeps);

        Assert.Equal(2, removedTasks.Count);
        Assert.Single(removedDeps);
        Assert.Null(project.FindTask(parent.Id));
        Assert.Null(project.FindTask(child.Id));
        Assert.NotNull(project.FindTask(unrelated.Id));
        Assert.Empty(project.Dependencies);
    }

    [Fact]
    public void AddDependency_DirectCycle_Throws()
    {
        var project = new ProjectModel();
        var a = MakeTask("A");
        var b = MakeTask("B");
        project.AddTask(a);
        project.AddTask(b);

        project.AddDependency(new DependencyLink(a.Id, b.Id));

        Assert.Throws<InvalidOperationException>(() => project.AddDependency(new DependencyLink(b.Id, a.Id)));
    }

    [Fact]
    public void AddDependency_TransitiveCycle_Throws()
    {
        var project = new ProjectModel();
        var a = MakeTask("A");
        var b = MakeTask("B");
        var c = MakeTask("C");
        project.AddTask(a);
        project.AddTask(b);
        project.AddTask(c);

        project.AddDependency(new DependencyLink(a.Id, b.Id));
        project.AddDependency(new DependencyLink(b.Id, c.Id));

        Assert.Throws<InvalidOperationException>(() => project.AddDependency(new DependencyLink(c.Id, a.Id)));
    }

    [Fact]
    public void AddDependency_SelfLink_Throws()
    {
        var project = new ProjectModel();
        var a = MakeTask("A");
        project.AddTask(a);

        Assert.Throws<InvalidOperationException>(() => project.AddDependency(new DependencyLink(a.Id, a.Id)));
    }

    [Fact]
    public void GetWbsCode_ReflectsHierarchyPosition()
    {
        var project = new ProjectModel();
        var section1 = MakeTask("Section 1");
        var section2 = MakeTask("Section 2");
        project.AddTask(section1);
        project.AddTask(section2);

        var child1 = MakeTask("Child 1");
        child1.ParentId = section2.Id;
        var child2 = MakeTask("Child 2");
        child2.ParentId = section2.Id;
        project.AddTask(child1);
        project.AddTask(child2);

        Assert.Equal("1", project.GetWbsCode(section1.Id));
        Assert.Equal("2", project.GetWbsCode(section2.Id));
        Assert.Equal("2.1", project.GetWbsCode(child1.Id));
        Assert.Equal("2.2", project.GetWbsCode(child2.Id));
    }

    [Fact]
    public void RemoveResource_UnassignsFromAllTasks()
    {
        var project = new ProjectModel();
        var task = MakeTask("A");
        var resource = new ProjectResource { Name = "Bob" };
        task.AssignedResourceIds.Add(resource.Id);
        project.AddTask(task);
        project.AddResource(resource);

        project.RemoveResource(resource.Id);

        Assert.Empty(task.AssignedResourceIds);
        Assert.Empty(project.Resources);
    }
}

using GanttSquared.Core.Model;
using GanttSquared.Core.Persistence;

namespace GanttSquared.Core.Tests;

public class ProjectFileSerializerTests
{
    private static GanttTask MakeTask(string name, DateOnly start, DateOnly end) => new(name, start, end);

    [Fact]
    public void RoundTrip_PreservesHierarchyDatesAndDependencies()
    {
        var project = new ProjectModel { Name = "Website Redesign", UseWbsNumbering = true };

        var parent = MakeTask("Website Redesign", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 30));
        project.AddTask(parent);

        var research = MakeTask("Research", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 5));
        research.ParentId = parent.Id;
        research.Priority = PriorityLevel.High;
        research.ProgressPercent = 40;
        research.Color = "#3478F6";
        research.Description = "Competitor analysis";
        project.AddTask(research);

        var launch = MakeTask("Launch", new DateOnly(2026, 1, 30), new DateOnly(2026, 1, 30));
        launch.ParentId = parent.Id;
        launch.SetMilestone(true);
        project.AddTask(launch);

        project.AddDependency(new DependencyLink(research.Id, launch.Id, DependencyType.FinishToStart, lagDays: 2));

        var resource = new ProjectResource { Name = "Alice", Email = "alice@example.com", Color = "#FF0000" };
        project.AddResource(resource);
        research.AssignedResourceIds.Add(resource.Id);

        var path = Path.Combine(Path.GetTempPath(), $"gantt_test_{Guid.NewGuid()}.json");
        try
        {
            ProjectFileSerializer.Save(project, path);
            var loaded = ProjectFileSerializer.Load(path);

            Assert.Equal(project.Name, loaded.Name);
            Assert.Equal(project.UseWbsNumbering, loaded.UseWbsNumbering);
            Assert.Equal(3, loaded.Tasks.Count);

            var loadedResearch = loaded.FindTask(research.Id);
            Assert.NotNull(loadedResearch);
            Assert.Equal("Research", loadedResearch!.Name);
            Assert.Equal(parent.Id, loadedResearch.ParentId);
            Assert.Equal(PriorityLevel.High, loadedResearch.Priority);
            Assert.Equal(40, loadedResearch.ProgressPercent);
            Assert.Equal("#3478F6", loadedResearch.Color);
            Assert.Equal("Competitor analysis", loadedResearch.Description);
            Assert.Equal(new DateOnly(2026, 1, 1), loadedResearch.StartDate);
            Assert.Equal(new DateOnly(2026, 1, 5), loadedResearch.EndDate);
            Assert.Equal(0, loadedResearch.OrderIndex);
            Assert.Contains(resource.Id, loadedResearch.AssignedResourceIds);

            var loadedLaunch = loaded.FindTask(launch.Id);
            Assert.NotNull(loadedLaunch);
            Assert.True(loadedLaunch!.IsMilestone);
            Assert.Equal(loadedLaunch.StartDate, loadedLaunch.EndDate);
            Assert.Equal(1, loadedLaunch.OrderIndex);

            Assert.Single(loaded.Dependencies);
            var loadedDep = loaded.Dependencies[0];
            Assert.Equal(research.Id, loadedDep.PredecessorTaskId);
            Assert.Equal(launch.Id, loadedDep.SuccessorTaskId);
            Assert.Equal(DependencyType.FinishToStart, loadedDep.Type);
            Assert.Equal(2, loadedDep.LagDays);

            Assert.Single(loaded.Resources);
            Assert.Equal("Alice", loaded.Resources[0].Name);
            Assert.Equal(resource.Id, loaded.Resources[0].Id);

            // The hierarchy itself must still be walkable after reload, not just flat fields.
            var loadedRoots = loaded.GetRootTasks().ToList();
            Assert.Single(loadedRoots);
            Assert.Equal(parent.Id, loadedRoots[0].Id);
            Assert.Equal(2, loaded.GetChildren(parent.Id).Count());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RoundTrip_EmptyProject_Works()
    {
        var project = new ProjectModel { Name = "Empty" };
        var path = Path.Combine(Path.GetTempPath(), $"gantt_test_{Guid.NewGuid()}.json");
        try
        {
            ProjectFileSerializer.Save(project, path);
            var loaded = ProjectFileSerializer.Load(path);

            Assert.Equal("Empty", loaded.Name);
            Assert.Empty(loaded.Tasks);
            Assert.Empty(loaded.Dependencies);
            Assert.Empty(loaded.Resources);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gantt_does_not_exist_{Guid.NewGuid()}.json");
        Assert.Throws<FileNotFoundException>(() => ProjectFileSerializer.Load(path));
    }
}

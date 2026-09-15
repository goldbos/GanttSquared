using System.Text.Json;
using System.Text.Json.Serialization;
using GanttSquared.Core.Model;

namespace GanttSquared.Core.Persistence;

/// <summary>
/// Reads and writes a ProjectModel as a single plain-JSON file - the project's on-disk
/// permanence. The in-memory ProjectModel a running app edits is otherwise lost when the
/// process exits; this is what lets it survive a restart.
/// </summary>
public static class ProjectFileSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Serializes a project to indented JSON and writes it to <paramref name="filePath"/>, overwriting any existing file.</summary>
    public static void Save(ProjectModel project, string filePath)
    {
        var dto = ToDto(project);
        var json = JsonSerializer.Serialize(dto, Options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>Reads and deserializes a project from <paramref name="filePath"/>. Throws <see cref="InvalidDataException"/> if the file isn't valid GanttSquared JSON.</summary>
    public static ProjectModel Load(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var dto = JsonSerializer.Deserialize<ProjectFileDto>(json, Options)
            ?? throw new InvalidDataException($"'{filePath}' did not contain a valid GanttSquared project.");

        return FromDto(dto);
    }

    /// <summary>Converts a live project into its plain-DTO on-disk representation, without touching the filesystem.</summary>
    public static ProjectFileDto ToDto(ProjectModel project)
    {
        return new ProjectFileDto
        {
            Name = project.Name,
            UseWbsNumbering = project.UseWbsNumbering,
            Tasks = project.Tasks.Select(t => new TaskDto
            {
                Id = t.Id,
                Name = t.Name,
                StartDate = t.StartDate,
                EndDate = t.EndDate,
                IsMilestone = t.IsMilestone,
                ProgressPercent = t.ProgressPercent,
                Priority = t.Priority,
                Color = t.Color,
                Description = t.Description,
                Notes = t.Notes,
                ParentId = t.ParentId,
                OrderIndex = t.OrderIndex,
                IsExpanded = t.IsExpanded,
                AssignedResourceIds = t.AssignedResourceIds.ToList()
            }).ToList(),
            Dependencies = project.Dependencies.Select(d => new DependencyDto
            {
                Id = d.Id,
                PredecessorTaskId = d.PredecessorTaskId,
                SuccessorTaskId = d.SuccessorTaskId,
                Type = d.Type,
                LagDays = d.LagDays
            }).ToList(),
            Resources = project.Resources.Select(r => new ResourceDto
            {
                Id = r.Id,
                Name = r.Name,
                Email = r.Email,
                Color = r.Color
            }).ToList()
        };
    }

    /// <summary>Rebuilds a live project from its DTO representation, restoring tasks (dates/milestone via <see cref="GanttTask.SetDates"/>/<see cref="GanttTask.SetMilestone"/> so their invariants hold) before dependencies, since a dependency needs both endpoint tasks to already exist.</summary>
    public static ProjectModel FromDto(ProjectFileDto dto)
    {
        var project = new ProjectModel
        {
            Name = dto.Name,
            UseWbsNumbering = dto.UseWbsNumbering
        };

        foreach (var resourceDto in dto.Resources)
        {
            project.AddResource(new ProjectResource
            {
                Id = resourceDto.Id,
                Name = resourceDto.Name,
                Email = resourceDto.Email,
                Color = resourceDto.Color
            });
        }

        foreach (var taskDto in dto.Tasks)
        {
            var task = new GanttTask
            {
                Id = taskDto.Id,
                Name = taskDto.Name,
                ProgressPercent = taskDto.ProgressPercent,
                Priority = taskDto.Priority,
                Color = taskDto.Color,
                Description = taskDto.Description,
                Notes = taskDto.Notes,
                ParentId = taskDto.ParentId,
                IsExpanded = taskDto.IsExpanded,
                AssignedResourceIds = new List<Guid>(taskDto.AssignedResourceIds)
            };

            // SetDates before SetMilestone: milestone tasks are saved with Start == End
            // already, and SetMilestone(true) would collapse End to Start regardless, so
            // this order just avoids relying on that collapse to satisfy SetDates' own
            // end->=start check.
            task.SetDates(taskDto.StartDate, taskDto.EndDate);
            if (taskDto.IsMilestone)
                task.SetMilestone(true);

            // AddTask assigns OrderIndex from the current sibling count, which won't match
            // the saved value unless tasks happen to be restored in that exact order - so
            // restore the real value explicitly afterward.
            project.AddTask(task);
            task.OrderIndex = taskDto.OrderIndex;
        }

        foreach (var depDto in dto.Dependencies)
        {
            project.AddDependency(new DependencyLink(depDto.PredecessorTaskId, depDto.SuccessorTaskId, depDto.Type, depDto.LagDays)
            {
                Id = depDto.Id
            });
        }

        return project;
    }
}

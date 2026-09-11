using GanttSquared.Core.Model;

namespace GanttSquared.Core.Persistence;

/// <summary>
/// Plain, serializable mirror of a ProjectModel. GanttTask/DependencyLink/ProjectResource
/// keep their invariants behind method calls (SetDates, SetMilestone, AddDependency's cycle
/// check, ...), so the domain types themselves aren't serialized directly - the DTOs are the
/// on-disk contract, decoupled from how the domain model chooses to enforce its rules.
/// </summary>
public sealed class ProjectFileDto
{
    public int FormatVersion { get; set; } = 1;

    public string Name { get; set; } = "Untitled Project";

    public bool UseWbsNumbering { get; set; }

    public List<TaskDto> Tasks { get; set; } = new();

    public List<DependencyDto> Dependencies { get; set; } = new();

    public List<ResourceDto> Resources { get; set; } = new();
}

public sealed class TaskDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    public bool IsMilestone { get; set; }

    public int ProgressPercent { get; set; }

    public PriorityLevel Priority { get; set; }

    public string? Color { get; set; }

    public string Description { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public int OrderIndex { get; set; }

    public bool IsExpanded { get; set; } = true;

    public List<Guid> AssignedResourceIds { get; set; } = new();
}

public sealed class DependencyDto
{
    public Guid Id { get; set; }

    public Guid PredecessorTaskId { get; set; }

    public Guid SuccessorTaskId { get; set; }

    public DependencyType Type { get; set; }

    public int LagDays { get; set; }
}

public sealed class ResourceDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Color { get; set; }
}

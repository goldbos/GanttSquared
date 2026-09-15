namespace GanttSquared.Core.Model;

/// <summary>A person (or role) that tasks can be assigned to, via <see cref="GanttTask.AssignedResourceIds"/>.</summary>
public sealed class ProjectResource
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "New Resource";

    /// <summary>Optional contact email, shown in the Resources view.</summary>
    public string? Email { get; set; }

    /// <summary>Hex color used for the resource's avatar/chip.</summary>
    public string? Color { get; set; }
}

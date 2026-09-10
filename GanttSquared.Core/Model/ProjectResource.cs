namespace GanttSquared.Core.Model;

public sealed class ProjectResource
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "New Resource";

    public string? Email { get; set; }

    /// <summary>Hex color used for the resource's avatar/chip.</summary>
    public string? Color { get; set; }
}

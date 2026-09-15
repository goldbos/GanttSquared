namespace GanttSquared.Core.Model;

/// <summary>A task's priority. Drives its default bar color (see TaskNodeViewModel.DefaultColorFor) when no explicit <see cref="GanttTask.Color"/> is set.</summary>
public enum PriorityLevel
{
    Low,
    Medium,
    High,
    Critical
}

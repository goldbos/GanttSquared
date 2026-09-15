namespace GanttSquared.ViewModels;

/// <summary>
/// One task's bar on the Resources tab's allocation timeline. Positioned absolutely (X/Y) on a
/// single shared canvas, the same layered-ItemsControl approach the main Gantt canvas uses,
/// rather than nested per-row canvases - X comes from the same GanttTimelineViewModel instance
/// the Gantt chart uses, so the two charts always line up date-wise.
/// </summary>
public sealed record ResourceAllocationBarViewModel(double X, double Y, double Width, double Height, string ColorHex, string TaskName, bool IsConflict);

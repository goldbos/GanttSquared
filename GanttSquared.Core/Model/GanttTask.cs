namespace GanttSquared.Core.Model;

/// <summary>
/// A single task (or milestone, or group/section) in the project.
/// Named GanttTask to avoid colliding with System.Threading.Tasks.Task.
/// </summary>
public sealed class GanttTask
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; set; } = "New Task";

    public DateOnly StartDate { get; private set; } = DateOnly.FromDateTime(DateTime.Today);

    public DateOnly EndDate { get; private set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>Duration in calendar days, derived from Start/End (inclusive span).</summary>
    public int DurationDays => EndDate.DayNumber - StartDate.DayNumber;

    public bool IsMilestone { get; private set; }

    /// <summary>0-100.</summary>
    public int ProgressPercent { get; set; }

    public PriorityLevel Priority { get; set; } = PriorityLevel.Medium;

    /// <summary>Hex color e.g. "#3478F6". Null means "use default/derived color".</summary>
    public string? Color { get; set; }

    public string Description { get; set; } = string.Empty;

    /// <summary>Parent task id, for hierarchical grouping (a "Section"). Null = top-level.</summary>
    public Guid? ParentId { get; set; }

    /// <summary>Position among siblings; lower sorts first.</summary>
    public int OrderIndex { get; set; }

    /// <summary>Whether child tasks are shown (collapsed groups hide their subtasks).</summary>
    public bool IsExpanded { get; set; } = true;

    public List<Guid> AssignedResourceIds { get; init; } = new();

    public GanttTask()
    {
    }

    public GanttTask(string name, DateOnly start, DateOnly end)
    {
        Name = name;
        SetDates(start, end);
    }

    /// <summary>Sets both dates directly. End must not be before Start.</summary>
    public void SetDates(DateOnly start, DateOnly end)
    {
        if (end < start)
            throw new ArgumentException("End date cannot be before start date.");

        StartDate = start;
        EndDate = IsMilestone ? start : end;
    }

    /// <summary>Shifts the task (both dates move together), preserving duration. Used for drag-move.</summary>
    public void MoveTo(DateOnly newStart)
    {
        var length = DurationDays;
        StartDate = newStart;
        EndDate = newStart.AddDays(length);
    }

    /// <summary>Changes the end date only, keeping start fixed. Used for drag-resize.</summary>
    public void ResizeTo(DateOnly newEnd)
    {
        if (newEnd < StartDate)
            throw new ArgumentException("End date cannot be before start date.");

        EndDate = IsMilestone ? StartDate : newEnd;
    }

    /// <summary>Sets duration directly by moving the end date, keeping start fixed.</summary>
    public void SetDurationDays(int days)
    {
        if (days < 0)
            throw new ArgumentException("Duration cannot be negative.");

        EndDate = StartDate.AddDays(days);
    }

    public void SetMilestone(bool isMilestone)
    {
        IsMilestone = isMilestone;
        if (isMilestone)
            EndDate = StartDate;
    }
}

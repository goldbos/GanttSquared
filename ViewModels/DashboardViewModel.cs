namespace GanttSquared.ViewModels;

/// <summary>A task name plus the one date relevant to why it's listed (its due date if overdue, its start date if an upcoming milestone).</summary>
public sealed record DashboardTaskSummary(string Name, DateOnly Date);

/// <summary>
/// A point-in-time snapshot of the whole project for the Dashboard tab. Recomputed wholesale by
/// MainViewModel.RebuildDashboard() on every rebuild (same trigger points as RebuildTree/
/// RebuildResources) rather than tracked incrementally - cheap enough given typical project
/// sizes, and much simpler than keeping running totals in sync with every possible edit.
/// </summary>
public sealed class DashboardViewModel
{
    /// <summary>Leaf tasks only (groups are organizational, not independent work items).</summary>
    public int TotalTasks { get; init; }

    /// <summary>Average ProgressPercent across leaf tasks, 0-100.</summary>
    public double CompletionPercent { get; init; }

    public int NotStartedCount { get; init; }
    public int InProgressCount { get; init; }
    public int CompletedCount { get; init; }

    /// <summary>Leaf tasks past their end date and not yet 100% complete.</summary>
    public int OverdueCount { get; init; }

    public int LowPriorityCount { get; init; }
    public int MediumPriorityCount { get; init; }
    public int HighPriorityCount { get; init; }
    public int CriticalPriorityCount { get; init; }

    /// <summary>Overdue tasks, soonest-overdue first, capped for display.</summary>
    public IReadOnlyList<DashboardTaskSummary> OverdueTasks { get; init; } = Array.Empty<DashboardTaskSummary>();

    /// <summary>Milestones not yet reached, soonest first, capped for display.</summary>
    public IReadOnlyList<DashboardTaskSummary> UpcomingMilestones { get; init; } = Array.Empty<DashboardTaskSummary>();

    /// <summary>Names of resources currently assigned to two or more overlapping tasks.</summary>
    public IReadOnlyList<string> ConflictedResourceNames { get; init; } = Array.Empty<string>();
}

namespace GanttSquared.Core.Model;

/// <summary>A directed link from one task (the predecessor) to another (the successor) constraining when the successor can be scheduled.</summary>
public sealed class DependencyLink
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The task the link runs from - the one that must (per <see cref="Type"/>) happen before the successor.</summary>
    public Guid PredecessorTaskId { get; init; }

    /// <summary>The task the link runs to - the one constrained by the predecessor.</summary>
    public Guid SuccessorTaskId { get; init; }

    /// <summary>Currently only <see cref="DependencyType.FinishToStart"/> is exposed in the UI; the other values exist for forward-compatibility with the persisted format.</summary>
    public DependencyType Type { get; set; } = DependencyType.FinishToStart;

    /// <summary>Extra gap (positive) or overlap (negative) in days between the linked dates.</summary>
    public int LagDays { get; set; }

    public DependencyLink()
    {
    }

    public DependencyLink(Guid predecessorTaskId, Guid successorTaskId, DependencyType type = DependencyType.FinishToStart, int lagDays = 0)
    {
        PredecessorTaskId = predecessorTaskId;
        SuccessorTaskId = successorTaskId;
        Type = type;
        LagDays = lagDays;
    }
}

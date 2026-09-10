namespace GanttSquared.Core.Model;

public sealed class DependencyLink
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public Guid PredecessorTaskId { get; init; }

    public Guid SuccessorTaskId { get; init; }

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

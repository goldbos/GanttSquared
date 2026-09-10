using GanttSquared.Core.Model;

namespace GanttSquared.Core.Scheduling;

public readonly record struct TaskDateChange(Guid TaskId, DateOnly NewStart, DateOnly NewEnd);

/// <summary>
/// Computes forward-scheduling cascades: when a task's dates change, its successors
/// (and their successors, transitively) are pushed later if their dependency constraint
/// is violated. Tasks are never pulled earlier automatically.
/// </summary>
public static class SchedulingEngine
{
    /// <summary>
    /// Given that <paramref name="changedTaskId"/> has just moved/resized to the given dates,
    /// returns every task (including the changed one) with its resulting dates, in an order
    /// safe to apply. Does not mutate the project; callers apply the changes themselves.
    /// </summary>
    public static IReadOnlyList<TaskDateChange> ComputeCascade(
        ProjectModel project, Guid changedTaskId, DateOnly newStart, DateOnly newEnd)
    {
        var results = new Dictionary<Guid, TaskDateChange>
        {
            [changedTaskId] = new TaskDateChange(changedTaskId, newStart, newEnd)
        };

        // BFS over successors. A task may be visited more than once if it has multiple
        // predecessors; we keep pushing it later as constraints demand, never earlier.
        var queue = new Queue<Guid>();
        queue.Enqueue(changedTaskId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var current = results[currentId];

            foreach (var dep in project.GetOutgoing(currentId))
            {
                var successor = project.FindTask(dep.SuccessorTaskId);
                if (successor is null)
                    continue;

                (DateOnly Start, DateOnly End) existing = results.TryGetValue(successor.Id, out var e)
                    ? (e.NewStart, e.NewEnd)
                    : (successor.StartDate, successor.EndDate);

                var duration = existing.End.DayNumber - existing.Start.DayNumber;

                var (requiredStart, requiredEnd) = ComputeConstraint(
                    dep.Type, current.NewStart, current.NewEnd, dep.LagDays, existing.Start, existing.End, duration);

                if (requiredStart == existing.Start && requiredEnd == existing.End)
                    continue; // no change needed for this predecessor

                results[successor.Id] = new TaskDateChange(successor.Id, requiredStart, requiredEnd);
                queue.Enqueue(successor.Id);
            }
        }

        return results.Values.ToList();
    }

    private static (DateOnly Start, DateOnly End) ComputeConstraint(
        DependencyType type,
        DateOnly predStart, DateOnly predEnd, int lagDays,
        DateOnly succStart, DateOnly succEnd, int succDuration)
    {
        DateOnly earliestStart = type switch
        {
            DependencyType.FinishToStart => predEnd.AddDays(lagDays),
            DependencyType.StartToStart => predStart.AddDays(lagDays),
            DependencyType.FinishToFinish => predEnd.AddDays(lagDays - succDuration),
            DependencyType.StartToFinish => predStart.AddDays(lagDays - succDuration),
            _ => succStart
        };

        if (earliestStart <= succStart)
            return (succStart, succEnd);

        return (earliestStart, earliestStart.AddDays(succDuration));
    }
}

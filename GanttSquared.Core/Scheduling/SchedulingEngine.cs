using GanttSquared.Core.Model;

namespace GanttSquared.Core.Scheduling;

public readonly record struct TaskDateChange(Guid TaskId, DateOnly NewStart, DateOnly NewEnd);

/// <summary>
/// Computes forward-scheduling cascades: when a task's dates change, every dependent
/// successor (transitively) is snapped to its earliest possible start given ALL of its
/// incoming dependencies - standard CPM early-start scheduling, the same model tools like
/// MS Project use for auto-scheduled tasks. This is symmetric: a successor is pulled earlier
/// right along with a predecessor that moves earlier, not just pushed later, unless another
/// one of its predecessors still holds it back.
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

        var queue = new Queue<Guid>();
        var queued = new HashSet<Guid> { changedTaskId };
        queue.Enqueue(changedTaskId);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            queued.Remove(currentId);

            foreach (var dep in project.GetOutgoing(currentId))
            {
                var successor = project.FindTask(dep.SuccessorTaskId);
                if (successor is null)
                    continue;

                var (existingStart, existingEnd) = results.TryGetValue(successor.Id, out var e)
                    ? (e.NewStart, e.NewEnd)
                    : (successor.StartDate, successor.EndDate);
                var duration = existingEnd.DayNumber - existingStart.DayNumber;

                // Recompute the successor's earliest allowed start across ALL of its incoming
                // dependencies (not just the edge that triggered this visit), using each
                // predecessor's current - possibly already-cascaded - dates. This is what
                // makes pulling a task earlier safe: the successor never overshoots past
                // whatever a different predecessor still requires.
                DateOnly? requiredStart = null;
                foreach (var incoming in project.GetIncoming(successor.Id))
                {
                    var predecessor = project.FindTask(incoming.PredecessorTaskId);
                    if (predecessor is null)
                        continue;

                    var (predStart, predEnd) = results.TryGetValue(predecessor.Id, out var pe)
                        ? (pe.NewStart, pe.NewEnd)
                        : (predecessor.StartDate, predecessor.EndDate);

                    var candidate = ComputeConstraintStart(incoming.Type, predStart, predEnd, incoming.LagDays, duration);
                    if (requiredStart is null || candidate > requiredStart)
                        requiredStart = candidate;
                }

                if (requiredStart is not { } newSuccStart)
                    continue;

                var newSuccEnd = newSuccStart.AddDays(duration);
                if (newSuccStart == existingStart && newSuccEnd == existingEnd)
                    continue; // already exactly where it needs to be

                results[successor.Id] = new TaskDateChange(successor.Id, newSuccStart, newSuccEnd);
                if (queued.Add(successor.Id))
                    queue.Enqueue(successor.Id);
            }
        }

        return results.Values.ToList();
    }

    /// <summary>The earliest the successor could start to satisfy this one dependency, given the predecessor's dates.</summary>
    private static DateOnly ComputeConstraintStart(
        DependencyType type, DateOnly predStart, DateOnly predEnd, int lagDays, int succDuration) => type switch
    {
        DependencyType.FinishToStart => predEnd.AddDays(lagDays),
        DependencyType.StartToStart => predStart.AddDays(lagDays),
        DependencyType.FinishToFinish => predEnd.AddDays(lagDays - succDuration),
        DependencyType.StartToFinish => predStart.AddDays(lagDays - succDuration),
        _ => predStart
    };
}

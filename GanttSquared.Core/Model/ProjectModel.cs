namespace GanttSquared.Core.Model;

/// <summary>
/// The root aggregate for a single project document: its tasks, their dependencies,
/// and the resources that can be assigned to tasks.
/// </summary>
public sealed class ProjectModel
{
    private readonly List<GanttTask> _tasks = new();
    private readonly Dictionary<Guid, GanttTask> _tasksById = new();
    private readonly List<DependencyLink> _dependencies = new();
    private readonly List<ProjectResource> _resources = new();

    public string Name { get; set; } = "Untitled Project";

    /// <summary>Whether task rows should display WBS-style numbering (1, 1.1, 1.2, 2, ...). Set at project creation.</summary>
    public bool UseWbsNumbering { get; set; }

    public IReadOnlyList<GanttTask> Tasks => _tasks;

    public IReadOnlyList<DependencyLink> Dependencies => _dependencies;

    public IReadOnlyList<ProjectResource> Resources => _resources;

    public GanttTask? FindTask(Guid id) => _tasksById.GetValueOrDefault(id);

    public IEnumerable<GanttTask> GetChildren(Guid? parentId) =>
        _tasks.Where(t => t.ParentId == parentId).OrderBy(t => t.OrderIndex);

    public IEnumerable<GanttTask> GetRootTasks() => GetChildren(null);

    /// <summary>All descendants of a task (children, grandchildren, ...), not including the task itself.</summary>
    public IEnumerable<GanttTask> GetDescendants(Guid taskId)
    {
        foreach (var child in GetChildren(taskId))
        {
            yield return child;
            foreach (var grandchild in GetDescendants(child.Id))
                yield return grandchild;
        }
    }

    /// <summary>
    /// Computes the WBS code (e.g. "2.1.3") for a task from its position among siblings at
    /// each level of the hierarchy. Meaningful only when <see cref="UseWbsNumbering"/> is set.
    /// </summary>
    public string GetWbsCode(Guid taskId)
    {
        var segments = new List<int>();
        var current = FindTask(taskId) ?? throw new InvalidOperationException("Task not found.");

        while (true)
        {
            var siblings = GetChildren(current.ParentId).OrderBy(t => t.OrderIndex).ToList();
            segments.Insert(0, siblings.FindIndex(t => t.Id == current.Id) + 1);

            if (current.ParentId is null)
                break;

            current = FindTask(current.ParentId.Value)!;
        }

        return string.Join('.', segments);
    }

    /// <summary>
    /// Adds a task to the project. If OrderIndex is 0 and siblings already exist, it is placed last.
    /// </summary>
    public void AddTask(GanttTask task, int? insertAtIndex = null)
    {
        if (_tasksById.ContainsKey(task.Id))
            throw new InvalidOperationException("A task with this id already exists.");

        var siblingCount = GetChildren(task.ParentId).Count();
        task.OrderIndex = insertAtIndex ?? siblingCount;

        _tasks.Add(task);
        _tasksById[task.Id] = task;
    }

    /// <summary>Removes a task and, cascading, all of its descendants and any dependency links touching them.</summary>
    public void RemoveTaskCascading(Guid taskId, out List<GanttTask> removedTasks, out List<DependencyLink> removedDependencies)
    {
        var task = FindTask(taskId) ?? throw new InvalidOperationException("Task not found.");

        var toRemove = new List<GanttTask> { task };
        toRemove.AddRange(GetDescendants(taskId));

        removedTasks = toRemove;
        var idsToRemove = toRemove.Select(t => t.Id).ToHashSet();

        removedDependencies = _dependencies
            .Where(d => idsToRemove.Contains(d.PredecessorTaskId) || idsToRemove.Contains(d.SuccessorTaskId))
            .ToList();

        foreach (var dep in removedDependencies)
            _dependencies.Remove(dep);

        foreach (var t in toRemove)
        {
            _tasks.Remove(t);
            _tasksById.Remove(t.Id);
        }
    }

    internal void RestoreTasks(IEnumerable<GanttTask> tasks)
    {
        foreach (var t in tasks)
        {
            _tasks.Add(t);
            _tasksById[t.Id] = t;
        }
    }

    internal void RestoreDependencies(IEnumerable<DependencyLink> deps) => _dependencies.AddRange(deps);

    /// <summary>True if adding predecessor -> successor would create a cycle in the dependency graph.</summary>
    public bool WouldCreateCycle(Guid predecessorTaskId, Guid successorTaskId)
    {
        if (predecessorTaskId == successorTaskId)
            return true;

        // Would there already be a path from successor back to predecessor?
        var visited = new HashSet<Guid>();
        var stack = new Stack<Guid>();
        stack.Push(successorTaskId);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!visited.Add(current))
                continue;

            if (current == predecessorTaskId)
                return true;

            foreach (var dep in _dependencies.Where(d => d.PredecessorTaskId == current))
                stack.Push(dep.SuccessorTaskId);
        }

        return false;
    }

    public void AddDependency(DependencyLink link)
    {
        if (FindTask(link.PredecessorTaskId) is null || FindTask(link.SuccessorTaskId) is null)
            throw new InvalidOperationException("Both tasks must exist in the project.");

        if (WouldCreateCycle(link.PredecessorTaskId, link.SuccessorTaskId))
            throw new InvalidOperationException("This dependency would create a cycle.");

        if (_dependencies.Any(d => d.PredecessorTaskId == link.PredecessorTaskId && d.SuccessorTaskId == link.SuccessorTaskId))
            throw new InvalidOperationException("This dependency already exists.");

        _dependencies.Add(link);
    }

    public void RemoveDependency(Guid dependencyId)
    {
        var dep = _dependencies.FirstOrDefault(d => d.Id == dependencyId);
        if (dep is not null)
            _dependencies.Remove(dep);
    }

    public IEnumerable<DependencyLink> GetOutgoing(Guid taskId) =>
        _dependencies.Where(d => d.PredecessorTaskId == taskId);

    public IEnumerable<DependencyLink> GetIncoming(Guid taskId) =>
        _dependencies.Where(d => d.SuccessorTaskId == taskId);

    public void AddResource(ProjectResource resource)
    {
        if (_resources.Any(r => r.Id == resource.Id))
            throw new InvalidOperationException("A resource with this id already exists.");

        _resources.Add(resource);
    }

    public void RemoveResource(Guid resourceId)
    {
        _resources.RemoveAll(r => r.Id == resourceId);
        foreach (var task in _tasks)
            task.AssignedResourceIds.Remove(resourceId);
    }
}

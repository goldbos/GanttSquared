using System.Globalization;
using System.Xml.Linq;
using GanttSquared.Core.Model;

namespace GanttSquared.Core.Persistence;

/// <summary>
/// One-way importer for GanttProject's native .gan file format (plain XML, no namespace/schema).
/// Reads tasks (with hierarchy, dates, milestones, priority, progress), dependencies, resources,
/// and resource assignments into a new ProjectModel. Deliberately scoped to a minimal, correct
/// core rather than the full format: calendars/holidays, vacations, baselines, custom properties,
/// notes, and view state are all ignored - see AddWorkingDays for the one place that matters.
/// </summary>
public static class GanttProjectImporter
{
    /// <summary>Reads and imports a .gan file from disk.</summary>
    public static ProjectModel Import(string filePath) => Import(XDocument.Load(filePath));

    /// <summary>Imports an already-loaded .gan document. Throws <see cref="InvalidDataException"/> if the root element isn't &lt;project&gt;.</summary>
    public static ProjectModel Import(XDocument document)
    {
        var root = document.Root;
        if (root is null || root.Name.LocalName != "project")
            throw new InvalidDataException("Not a valid GanttProject file: expected a <project> root element.");

        var projectName = (string?)root.Attribute("name");
        var project = new ProjectModel
        {
            Name = string.IsNullOrWhiteSpace(projectName) ? "Imported Project" : projectName
        };

        var taskIdMap = new Dictionary<int, Guid>();

        // <depend> references a successor by file-local id, which may not be mapped yet at the
        // point its owning task is visited (forward references are legal) - so dependencies are
        // collected here and resolved in a second pass, once every task has been imported.
        var pendingDependencies = new List<(Guid PredecessorId, XElement DependElement)>();

        var tasksRoot = root.Element("tasks");
        if (tasksRoot is not null)
        {
            foreach (var taskElement in tasksRoot.Elements("task"))
                ImportTask(taskElement, parentId: null, project, taskIdMap, pendingDependencies);
        }

        foreach (var (predecessorId, dependElement) in pendingDependencies)
        {
            var successorFileId = (int?)dependElement.Attribute("id");
            if (successorFileId is null || !taskIdMap.TryGetValue(successorFileId.Value, out var successorId))
                continue;

            var type = MapDependencyType((int?)dependElement.Attribute("type"));
            var lag = (int?)dependElement.Attribute("difference") ?? 0;

            try
            {
                project.AddDependency(new DependencyLink(predecessorId, successorId, type, lag));
            }
            catch (InvalidOperationException)
            {
                // Cyclic or duplicate reference in the source file; skip it rather than aborting
                // the whole import over one bad edge.
            }
        }

        var resourceIdMap = new Dictionary<int, Guid>();
        var resourcesRoot = root.Element("resources");
        if (resourcesRoot is not null)
        {
            foreach (var resourceElement in resourcesRoot.Elements("resource"))
            {
                var fileId = (int?)resourceElement.Attribute("id");
                if (fileId is null)
                    continue;

                var name = (string?)resourceElement.Attribute("name");
                var resource = new ProjectResource
                {
                    Name = string.IsNullOrWhiteSpace(name) ? "Imported Resource" : name,
                    // GanttProject's attribute is named "contacts" but is used as the resource's email.
                    Email = (string?)resourceElement.Attribute("contacts") is { Length: > 0 } contacts ? contacts : null
                };
                project.AddResource(resource);
                resourceIdMap[fileId.Value] = resource.Id;
            }
        }

        var allocationsRoot = root.Element("allocations");
        if (allocationsRoot is not null)
        {
            foreach (var allocationElement in allocationsRoot.Elements("allocation"))
            {
                var taskFileId = (int?)allocationElement.Attribute("task-id");
                var resourceFileId = (int?)allocationElement.Attribute("resource-id");
                if (taskFileId is null || resourceFileId is null)
                    continue;
                if (!taskIdMap.TryGetValue(taskFileId.Value, out var taskId) || !resourceIdMap.TryGetValue(resourceFileId.Value, out var resourceId))
                    continue;

                var task = project.FindTask(taskId);
                if (task is not null && !task.AssignedResourceIds.Contains(resourceId))
                    task.AssignedResourceIds.Add(resourceId);
            }
        }

        return project;
    }

    private static void ImportTask(
        XElement taskElement, Guid? parentId, ProjectModel project,
        Dictionary<int, Guid> taskIdMap, List<(Guid PredecessorId, XElement DependElement)> pendingDependencies)
    {
        var fileId = (int?)taskElement.Attribute("id");
        if (fileId is null)
            return; // malformed entry; nothing to key it by, so it can't be a dependency/allocation target anyway

        var name = (string?)taskElement.Attribute("name");
        var isMilestone = (bool?)taskElement.Attribute("meeting") ?? false;

        var startText = (string?)taskElement.Attribute("start");
        var start = startText is not null
            && DateOnly.TryParseExact(startText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedStart)
                ? parsedStart
                : DateOnly.FromDateTime(DateTime.Today);

        // GanttProject stores duration as a count of WORKING days with no end-date attribute at
        // all - naively treating it as calendar days would compute end dates too early for any
        // task spanning a weekend. The conversion to an absolute end date happens once, here;
        // from then on this app's own calendar-day model takes over.
        var durationWorkingDays = Math.Max(0, (int?)taskElement.Attribute("duration") ?? 0);
        var end = isMilestone ? start : AddWorkingDays(start, durationWorkingDays);

        var task = new GanttTask(string.IsNullOrWhiteSpace(name) ? "Imported Task" : name, start, end)
        {
            ParentId = parentId,
            Priority = MapPriority((string?)taskElement.Attribute("priority")),
            ProgressPercent = Math.Clamp((int?)taskElement.Attribute("complete") ?? 0, 0, 100)
        };
        if (isMilestone)
            task.SetMilestone(true);

        project.AddTask(task);
        taskIdMap[fileId.Value] = task.Id;

        foreach (var dependElement in taskElement.Elements("depend"))
            pendingDependencies.Add((task.Id, dependElement));

        foreach (var childTaskElement in taskElement.Elements("task"))
            ImportTask(childTaskElement, task.Id, project, taskIdMap, pendingDependencies);
    }

    /// <summary>GanttProject's persistent type codes: 1=SS, 2=FS, 3=FF, 4=SF; blank/unknown defaults to FS.</summary>
    private static DependencyType MapDependencyType(int? typeCode) => typeCode switch
    {
        1 => DependencyType.StartToStart,
        3 => DependencyType.FinishToFinish,
        4 => DependencyType.StartToFinish,
        _ => DependencyType.FinishToStart
    };

    /// <summary>GanttProject's 5-level priority (persistent values 0/1/2/3/4 = Low/Normal/High/Lowest/Highest)
    /// collapsed onto this app's 4-level PriorityLevel; a missing attribute means Normal.</summary>
    private static PriorityLevel MapPriority(string? persistentValue) => persistentValue switch
    {
        "3" => PriorityLevel.Low, // Lowest
        "0" => PriorityLevel.Low, // Low
        "2" => PriorityLevel.High,
        "4" => PriorityLevel.Critical, // Highest
        _ => PriorityLevel.Medium // "1" (Normal) or missing
    };

    /// <summary>
    /// Walks forward the given number of working days (Mon-Fri) from start. Weekends are
    /// hardcoded as Saturday/Sunday - the file's own &lt;calendars&gt; weekday/holiday
    /// definitions aren't read, a deliberate scope cut for a first-pass importer.
    /// </summary>
    private static DateOnly AddWorkingDays(DateOnly start, int workingDays)
    {
        var date = start;
        var remaining = workingDays;
        while (remaining > 0)
        {
            date = date.AddDays(1);
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                remaining--;
        }

        return date;
    }
}

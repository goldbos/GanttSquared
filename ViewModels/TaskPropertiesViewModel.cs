using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GanttSquared.Core.Commands;
using GanttSquared.Core.Model;

namespace GanttSquared.ViewModels;

/// <summary>
/// Editable draft of the selected task's fields, shown in the right-hand Properties panel.
/// Nothing is applied to the project until Save is pressed, at which point each changed
/// field becomes its own undoable command.
/// </summary>
public sealed partial class TaskPropertiesViewModel : ObservableObject
{
    private readonly ProjectModel _project;
    private readonly UndoRedoManager _undoRedo;
    private GanttTask? _task;

    // CommunityToolkit.Mvvm only auto-requeries a [RelayCommand]'s CanExecute when the
    // property it reads carries [NotifyCanExecuteChangedFor] — without it, SaveCommand's
    // enabled state is frozen at its first evaluation (false, before anything is selected)
    // and never updates, no matter how many tasks get selected afterward.
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private bool _isMilestone;

    [ObservableProperty]
    private DateTime _startDate;

    [ObservableProperty]
    private DateTime _endDate;

    [ObservableProperty]
    private int _durationDays;

    [ObservableProperty]
    private string? _color;

    [ObservableProperty]
    private PriorityLevel _priority;

    [ObservableProperty]
    private int _progressPercent;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private string _sectionDisplay = string.Empty;

    // --- Bulk edit: several tasks selected at once. Priority/Color/Progress are the only
    // fields exposed here (the ones worth setting identically across a batch); each has its
    // own "apply" checkbox so Save only touches fields the user actually opted into, rather
    // than guessing from whether the shown value happens to differ from the first task's.
    [ObservableProperty]
    private bool _isBulkMode;

    [ObservableProperty]
    private int _bulkTaskCount;

    [ObservableProperty]
    private bool _bulkApplyPriority;

    [ObservableProperty]
    private bool _bulkApplyColor;

    [ObservableProperty]
    private bool _bulkApplyProgress;

    private List<GanttTask> _bulkTasks = new();

    /// <summary>Every project resource as a checkbox option for the selected task, rebuilt on each LoadFrom.</summary>
    public ObservableCollection<ResourceOptionViewModel> ResourceOptions { get; } = new();

    public IReadOnlyList<PriorityLevel> PriorityLevels { get; } = Enum.GetValues<PriorityLevel>();

    public event EventHandler? Applied;

    public TaskPropertiesViewModel(ProjectModel project, UndoRedoManager undoRedo)
    {
        _project = project;
        _undoRedo = undoRedo;
    }

    public void LoadFrom(GanttTask? task)
    {
        _task = task;
        IsBulkMode = false;
        HasSelection = task is not null;

        if (task is null)
            return;

        Name = task.Name;
        IsMilestone = task.IsMilestone;
        StartDate = task.StartDate.ToDateTime(TimeOnly.MinValue);
        EndDate = task.EndDate.ToDateTime(TimeOnly.MinValue);
        DurationDays = task.DurationDays;
        Color = task.Color;
        Priority = task.Priority;
        ProgressPercent = task.ProgressPercent;
        Description = task.Description;
        SectionDisplay = task.ParentId is { } parentId ? _project.FindTask(parentId)?.Name ?? string.Empty : string.Empty;

        ResourceOptions.Clear();
        foreach (var resource in _project.Resources)
            ResourceOptions.Add(new ResourceOptionViewModel(resource.Id, resource.Name, task.AssignedResourceIds.Contains(resource.Id)));
    }

    public void LoadForBulk(IReadOnlyList<GanttTask> tasks)
    {
        _task = null;
        _bulkTasks = tasks.ToList();
        IsBulkMode = true;
        HasSelection = _bulkTasks.Count > 0;
        BulkTaskCount = _bulkTasks.Count;
        BulkApplyPriority = false;
        BulkApplyColor = false;
        BulkApplyProgress = false;

        var first = _bulkTasks.Count > 0 ? _bulkTasks[0] : null;
        Priority = first?.Priority ?? PriorityLevel.Medium;
        Color = first?.Color;
        ProgressPercent = first?.ProgressPercent ?? 0;

        ResourceOptions.Clear(); // resource assignment isn't part of bulk edit in this first pass
    }

    partial void OnStartDateChanged(DateTime value) =>
        DurationDays = Math.Max(0, (EndDate.Date - value.Date).Days);

    partial void OnDurationDaysChanged(int value) =>
        EndDate = StartDate.Date.AddDays(Math.Max(0, value));

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Save()
    {
        if (IsBulkMode)
        {
            SaveBulk();
            return;
        }

        if (_task is null)
            return;

        // Snapshot every draft value up front. Each _undoRedo.Do() below synchronously
        // triggers UndoRedo.StateChanged -> MainViewModel.RebuildTree() -> a fresh
        // SelectedNode -> Properties.LoadFrom(_task), which reassigns Name/Priority/etc.
        // back from the task's (still only partially-updated) state. Reading from these
        // local copies instead of the live bound properties keeps that reentrant reload
        // from wiping out edits further down this method before their own check runs.
        var task = _task;
        var newName = Name;
        var newIsMilestone = IsMilestone;
        var newColor = Color;
        var newPriority = Priority;
        var newProgress = ProgressPercent;
        var newDescription = Description;
        var newStart = DateOnly.FromDateTime(StartDate);
        var newEnd = newIsMilestone ? newStart : DateOnly.FromDateTime(EndDate);
        // Same reentrancy hazard as the fields above: each _undoRedo.Do() below triggers a
        // reentrant LoadFrom(task) that rebuilds ResourceOptions from the task's still-
        // unchanged AssignedResourceIds, discarding any checkbox toggles if read live later.
        var newAssignedResourceIds = ResourceOptions.Where(r => r.IsAssigned).Select(r => r.ResourceId).ToHashSet();

        if (task.Name != newName)
            _undoRedo.Do(new EditTaskFieldCommand<string>(task, "name", t => t.Name, (t, v) => t.Name = v, newName));

        if (task.Priority != newPriority)
            _undoRedo.Do(new EditTaskFieldCommand<PriorityLevel>(task, "priority", t => t.Priority, (t, v) => t.Priority = v, newPriority));

        if (task.ProgressPercent != newProgress)
            _undoRedo.Do(new EditTaskFieldCommand<int>(task, "progress", t => t.ProgressPercent, (t, v) => t.ProgressPercent = v, newProgress));

        if (task.Color != newColor)
        {
            // A parent's color cascades to its whole subtree, so indented children visually
            // follow their section rather than keeping whatever color they had (or the default).
            // Bundled into one CompositeCommand so the entire cascade undoes as a single step.
            var descendants = _project.GetDescendants(task.Id).ToList();
            IUndoableCommand colorChange = descendants.Count == 0
                ? new EditTaskFieldCommand<string?>(task, "color", t => t.Color, (t, v) => t.Color = v, newColor)
                : new CompositeCommand(
                    new IUndoableCommand[] { new EditTaskFieldCommand<string?>(task, "color", t => t.Color, (t, v) => t.Color = v, newColor) }
                        .Concat(descendants.Select(d => (IUndoableCommand)new EditTaskFieldCommand<string?>(d, "color", t => t.Color, (t, v) => t.Color = v, newColor))),
                    $"Change color of '{task.Name}' and its subtasks");

            _undoRedo.Do(colorChange);
        }

        if (task.Description != newDescription)
            _undoRedo.Do(new EditTaskFieldCommand<string>(task, "description", t => t.Description, (t, v) => t.Description = v, newDescription));

        if (task.IsMilestone != newIsMilestone)
            _undoRedo.Do(new SetMilestoneCommand(task, newIsMilestone));

        if (task.StartDate != newStart || task.EndDate != newEnd)
            _undoRedo.Do(new RescheduleTaskCommand(_project, task.Id, newStart, newEnd));

        // Not routed through undo/redo, consistent with resource field edits elsewhere -
        // see ResourceRowViewModel.
        if (!task.AssignedResourceIds.ToHashSet().SetEquals(newAssignedResourceIds))
        {
            task.AssignedResourceIds.Clear();
            task.AssignedResourceIds.AddRange(newAssignedResourceIds);
        }

        LoadFrom(task);
        Applied?.Invoke(this, EventArgs.Empty);
    }

    private void SaveBulk()
    {
        var commands = new List<IUndoableCommand>();

        foreach (var task in _bulkTasks)
        {
            if (BulkApplyPriority && task.Priority != Priority)
                commands.Add(new EditTaskFieldCommand<PriorityLevel>(task, "priority", t => t.Priority, (t, v) => t.Priority = v, Priority));

            if (BulkApplyColor && task.Color != Color)
                commands.Add(new EditTaskFieldCommand<string?>(task, "color", t => t.Color, (t, v) => t.Color = v, Color));

            if (BulkApplyProgress && task.ProgressPercent != ProgressPercent)
                commands.Add(new EditTaskFieldCommand<int>(task, "progress", t => t.ProgressPercent, (t, v) => t.ProgressPercent = v, ProgressPercent));
        }

        if (commands.Count > 0)
            _undoRedo.Do(new CompositeCommand(commands, $"Edit {_bulkTasks.Count} tasks"));

        Applied?.Invoke(this, EventArgs.Empty);
    }
}

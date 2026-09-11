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
    }

    partial void OnStartDateChanged(DateTime value) =>
        DurationDays = Math.Max(0, (EndDate.Date - value.Date).Days);

    partial void OnDurationDaysChanged(int value) =>
        EndDate = StartDate.Date.AddDays(Math.Max(0, value));

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void Save()
    {
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

        if (task.Name != newName)
            _undoRedo.Do(new EditTaskFieldCommand<string>(task, "name", t => t.Name, (t, v) => t.Name = v, newName));

        if (task.Priority != newPriority)
            _undoRedo.Do(new EditTaskFieldCommand<PriorityLevel>(task, "priority", t => t.Priority, (t, v) => t.Priority = v, newPriority));

        if (task.ProgressPercent != newProgress)
            _undoRedo.Do(new EditTaskFieldCommand<int>(task, "progress", t => t.ProgressPercent, (t, v) => t.ProgressPercent = v, newProgress));

        if (task.Color != newColor)
            _undoRedo.Do(new EditTaskFieldCommand<string?>(task, "color", t => t.Color, (t, v) => t.Color = v, newColor));

        if (task.Description != newDescription)
            _undoRedo.Do(new EditTaskFieldCommand<string>(task, "description", t => t.Description, (t, v) => t.Description = v, newDescription));

        if (task.IsMilestone != newIsMilestone)
            _undoRedo.Do(new SetMilestoneCommand(task, newIsMilestone));

        if (task.StartDate != newStart || task.EndDate != newEnd)
            _undoRedo.Do(new RescheduleTaskCommand(_project, task.Id, newStart, newEnd));

        LoadFrom(task);
        Applied?.Invoke(this, EventArgs.Empty);
    }
}

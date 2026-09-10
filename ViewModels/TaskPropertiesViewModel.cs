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

        if (_task.Name != Name)
            _undoRedo.Do(new EditTaskFieldCommand<string>(_task, "name", t => t.Name, (t, v) => t.Name = v, Name));

        if (_task.Priority != Priority)
            _undoRedo.Do(new EditTaskFieldCommand<PriorityLevel>(_task, "priority", t => t.Priority, (t, v) => t.Priority = v, Priority));

        if (_task.ProgressPercent != ProgressPercent)
            _undoRedo.Do(new EditTaskFieldCommand<int>(_task, "progress", t => t.ProgressPercent, (t, v) => t.ProgressPercent = v, ProgressPercent));

        if (_task.Color != Color)
            _undoRedo.Do(new EditTaskFieldCommand<string?>(_task, "color", t => t.Color, (t, v) => t.Color = v, Color));

        if (_task.Description != Description)
            _undoRedo.Do(new EditTaskFieldCommand<string>(_task, "description", t => t.Description, (t, v) => t.Description = v, Description));

        if (_task.IsMilestone != IsMilestone)
            _undoRedo.Do(new SetMilestoneCommand(_task, IsMilestone));

        var newStart = DateOnly.FromDateTime(StartDate);
        var newEnd = IsMilestone ? newStart : DateOnly.FromDateTime(EndDate);
        if (_task.StartDate != newStart || _task.EndDate != newEnd)
            _undoRedo.Do(new RescheduleTaskCommand(_project, _task.Id, newStart, newEnd));

        LoadFrom(_task);
        Applied?.Invoke(this, EventArgs.Empty);
    }
}

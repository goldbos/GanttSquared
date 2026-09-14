using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GanttSquared.Core.Model;

namespace GanttSquared.ViewModels;

/// <summary>Wraps a GanttTask for display in the task list and on the Gantt canvas.</summary>
public sealed partial class TaskNodeViewModel : ObservableObject
{
    public GanttTask Task { get; }

    public ObservableCollection<TaskNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Hierarchy depth (0 = root), set by MainViewModel when flattening the visible rows.</summary>
    [ObservableProperty]
    private int _depth;

    /// <summary>
    /// The task's own dates for a leaf; for a group, the rolled-up span (earliest descendant
    /// start to latest descendant end), computed by MainViewModel over the full tree. Both the
    /// canvas bar and the list's date text use these rather than Task.StartDate/EndDate directly.
    /// </summary>
    [NotifyPropertyChangedFor(nameof(DateRangeText))]
    [ObservableProperty]
    private DateOnly _effectiveStartDate;

    [NotifyPropertyChangedFor(nameof(DateRangeText))]
    [ObservableProperty]
    private DateOnly _effectiveEndDate;

    /// <summary>Pixel position/size on the Gantt canvas, recomputed by MainViewModel on layout/zoom changes (and live during a drag).</summary>
    [ObservableProperty]
    private double _barX;

    [ObservableProperty]
    private double _barWidth;

    [ObservableProperty]
    private double _rowTop;

    [ObservableProperty]
    private double _progressWidth;

    [ObservableProperty]
    private bool _isAlternateRow;

    [ObservableProperty]
    private bool _isEditingName;

    /// <summary>WBS code (e.g. "2.1.3"), set by MainViewModel while building the tree; empty when the project hasn't opted into WBS numbering.</summary>
    [ObservableProperty]
    private string _wbsCode = string.Empty;

    [ObservableProperty]
    private string _editingNameDraft = string.Empty;

    public TaskNodeViewModel(GanttTask task)
    {
        Task = task;
        _effectiveStartDate = task.StartDate;
        _effectiveEndDate = task.EndDate;
    }

    public string Name => Task.Name;

    public bool IsMilestone => Task.IsMilestone;

    public bool IsGroup => Children.Count > 0;

    public string DateRangeText => IsMilestone
        ? EffectiveStartDate.ToString("MMM d")
        : $"{EffectiveStartDate:MMM d} - {EffectiveEndDate:MMM d}";

    public string BarColorHex => Task.Color ?? DefaultColorFor(Task.Priority);

    public bool IsExpanded
    {
        get => Task.IsExpanded;
        set
        {
            if (Task.IsExpanded == value)
                return;
            Task.IsExpanded = value;
            OnPropertyChanged();
        }
    }

    partial void OnBarWidthChanged(double value) =>
        ProgressWidth = value * Task.ProgressPercent / 100.0;

    public void BeginRename()
    {
        EditingNameDraft = Name;
        IsEditingName = true;
    }

    public void RaiseDisplayChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsMilestone));
        OnPropertyChanged(nameof(DateRangeText));
        OnPropertyChanged(nameof(IsGroup));
        OnPropertyChanged(nameof(BarColorHex));
    }

    private static string DefaultColorFor(PriorityLevel priority) => priority switch
    {
        PriorityLevel.Low => "#FF6B7280",
        PriorityLevel.Medium => "#FF3B82F6",
        PriorityLevel.High => "#FFF4B740",
        PriorityLevel.Critical => "#FFEF4444",
        _ => "#FF3B82F6"
    };
}

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
    private bool _isAlternateRow;

    /// <summary>True when the effective end date is in the past and the task isn't 100% complete; set by MainViewModel.RecomputeLayout.</summary>
    [ObservableProperty]
    private bool _isOverdue;

    /// <summary>True when this task shares an assigned resource with another visible task whose date range overlaps it; set by MainViewModel.RecomputeLayout.</summary>
    [ObservableProperty]
    private bool _isResourceConflict;

    [ObservableProperty]
    private bool _isEditingName;

    /// <summary>True while hovering this task's row in the list or its bar on the canvas; drives a highlight on the other side plus the list row's jump-to-chart button.</summary>
    [ObservableProperty]
    private bool _isHovered;

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

    /// <summary>
    /// Set by MainWindow whenever the theme toggles; every existing node then has
    /// RaiseDisplayChanged() called on it so BarColorHex (and its default-priority-color
    /// branch below) picks up the new theme immediately without waiting for a data reload.
    /// </summary>
    public static bool IsDarkTheme { get; set; } = true;

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

    // Dark-theme variants are a notch lighter/more saturated than the light-theme ones since
    // they sit on a near-black canvas, where the light-theme colors read as dull/muddy.
    // Internal (not private) so MainViewModel can reuse it for the Resources tab's allocation
    // bars, which need the same priority-color fallback a task's own Gantt bar uses.
    internal static string DefaultColorFor(PriorityLevel priority) => IsDarkTheme
        ? priority switch
        {
            PriorityLevel.Low => "#FF8B95A5",
            PriorityLevel.Medium => "#FF60A5FA",
            PriorityLevel.High => "#FFFBBF24",
            PriorityLevel.Critical => "#FFF87171",
            _ => "#FF60A5FA"
        }
        : priority switch
        {
            PriorityLevel.Low => "#FF6B7280",
            PriorityLevel.Medium => "#FF3B82F6",
            PriorityLevel.High => "#FFF4B740",
            PriorityLevel.Critical => "#FFEF4444",
            _ => "#FF3B82F6"
        };
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GanttSquared.Core.Model;

namespace GanttSquared.ViewModels;

/// <summary>Wraps a GanttTask for display in the hierarchical task list.</summary>
public sealed partial class TaskNodeViewModel : ObservableObject
{
    public GanttTask Task { get; }

    public ObservableCollection<TaskNodeViewModel> Children { get; } = new();

    [ObservableProperty]
    private bool _isSelected;

    public TaskNodeViewModel(GanttTask task)
    {
        Task = task;
    }

    public string Name => Task.Name;

    public bool IsMilestone => Task.IsMilestone;

    public bool IsGroup => Children.Count > 0;

    public string DateRangeText => IsMilestone
        ? Task.StartDate.ToString("MMM d")
        : $"{Task.StartDate:MMM d} - {Task.EndDate:MMM d}";

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

    public void RaiseDisplayChanged()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(IsMilestone));
        OnPropertyChanged(nameof(DateRangeText));
        OnPropertyChanged(nameof(IsGroup));
    }
}

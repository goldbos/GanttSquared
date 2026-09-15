using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GanttSquared.Core.Model;

namespace GanttSquared.ViewModels;

/// <summary>Wraps a ProjectResource for the Resources view's compact row list and allocation timeline.</summary>
public sealed partial class ResourceRowViewModel : ObservableObject
{
    public ProjectResource Resource { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string? _email;

    [ObservableProperty]
    private string? _color;

    [ObservableProperty]
    private string _assignedTaskNames = string.Empty;

    /// <summary>True when two or more of this resource's assigned tasks have overlapping date ranges; set by MainViewModel.RebuildResources.</summary>
    [ObservableProperty]
    private bool _isOverallocated;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>True while the compact row list's email/color details popup is open for this resource.</summary>
    [ObservableProperty]
    private bool _isDetailsExpanded;

    [RelayCommand]
    private void ToggleDetails() => IsDetailsExpanded = !IsDetailsExpanded;

    /// <summary>Pixel Y position of this row on the allocation timeline canvas (index * RowHeight), mirroring TaskNodeViewModel.RowTop on the Gantt canvas. Set by MainViewModel.RebuildResources.</summary>
    [ObservableProperty]
    private double _rowTop;

    /// <summary>Drives the timeline's alternating row-stripe background, mirroring TaskNodeViewModel.IsAlternateRow.</summary>
    [ObservableProperty]
    private bool _isAlternateRow;

    public ResourceRowViewModel(ProjectResource resource)
    {
        Resource = resource;
        _name = resource.Name;
        _email = resource.Email;
        _color = resource.Color;
    }

    // Name/Email/Color are edited directly (not routed through undo/redo) - a deliberate
    // simplification for this first pass, matching how lightweight a resource record is
    // compared to a task; Add/Remove are the operations worth undoing.
    partial void OnNameChanged(string value) => Resource.Name = value;

    partial void OnEmailChanged(string? value) => Resource.Email = value;

    partial void OnColorChanged(string? value) => Resource.Color = value;
}

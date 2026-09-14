using CommunityToolkit.Mvvm.ComponentModel;
using GanttSquared.Core.Model;

namespace GanttSquared.ViewModels;

/// <summary>Wraps a ProjectResource for the Resources view's allocation table.</summary>
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

    [ObservableProperty]
    private bool _isSelected;

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

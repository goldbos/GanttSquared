using CommunityToolkit.Mvvm.ComponentModel;

namespace GanttSquared.ViewModels;

/// <summary>One row of the Properties panel's Resources checklist for the selected task.</summary>
public sealed partial class ResourceOptionViewModel : ObservableObject
{
    public Guid ResourceId { get; }

    public string Name { get; }

    [ObservableProperty]
    private bool _isAssigned;

    public ResourceOptionViewModel(Guid resourceId, string name, bool isAssigned)
    {
        ResourceId = resourceId;
        Name = name;
        _isAssigned = isAssigned;
    }
}

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GanttSquared.ViewModels;

/// <summary>
/// An ObservableCollection with a ReplaceAll that swaps its entire contents as a single Reset
/// notification, instead of the Clear-then-N-Adds pattern used everywhere a view model rebuilds
/// a list wholesale (RefreshVisibleRows, RebuildResources, ...). That pattern fires N+1 separate
/// CollectionChanged events, and every ItemsControl bound to the collection (the task list, and
/// several layered canvas ItemsControls on top of it) reacts to each one individually - visibly
/// laggy once a rebuild touches dozens of rows, e.g. on every expand/collapse. A single Reset
/// lets each bound control do one full refresh instead of N incremental ones.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

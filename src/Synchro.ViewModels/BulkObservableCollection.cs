using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Synchro.ViewModels;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can also swap its whole content with one Reset notification, instead
/// of one notification per item: re-sorting thousands of rows stays cheap for the list control.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

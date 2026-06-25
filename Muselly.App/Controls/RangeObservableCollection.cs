using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Muselly.App.Controls;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can be repopulated in one shot. Replacing the contents
/// raises a single <see cref="NotifyCollectionChangedAction.Reset"/> instead of one event per item, which
/// avoids the notification/allocation storm (and the GC pauses that can starve audio) when filtering a
/// large library.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Replaces all items, raising a single Reset notification.</summary>
    public void Reset(IReadOnlyList<T> items)
    {
        Items.Clear();
        for (var i = 0; i < items.Count; i++)
            Items.Add(items[i]);

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>Appends items, raising a single ranged Add notification (used for incremental "load more").</summary>
    public void AddRange(IReadOnlyList<T> items)
    {
        if (items.Count == 0) return;

        var start = Items.Count;
        var added = new List<T>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            Items.Add(items[i]);
            added.Add(items[i]);
        }

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, added, start));
    }
}

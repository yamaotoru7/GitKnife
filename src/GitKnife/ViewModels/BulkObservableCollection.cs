using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace GitKnife.ViewModels;

/// <summary>
/// 中身をまとめて入れ替えられる ObservableCollection。
/// 1 件ずつ Add すると件数ぶん CollectionChanged が出て、数万行の一覧では UI が止まる。入れ替えは Reset を 1 回だけ出す。
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var i in items) Items.Add(i);
        RaiseReset();
    }

    public void AddRange(IEnumerable<T> items)
    {
        CheckReentrancy();
        var before = Items.Count;
        foreach (var i in items) Items.Add(i);
        if (Items.Count != before) RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

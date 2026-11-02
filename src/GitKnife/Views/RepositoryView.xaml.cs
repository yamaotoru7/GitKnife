using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GitKnife.ViewModels;

namespace GitKnife.Views;

public partial class RepositoryView : UserControl
{
    public RepositoryView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is INotifyPropertyChanged o) o.PropertyChanged -= OnVmChanged;
            if (e.NewValue is INotifyPropertyChanged n) n.PropertyChanged += OnVmChanged;
            UpdateView();
            UpdateLayoutOrientation();
        };
        SidebarColumn.Width = new GridLength(App.Settings.SidebarColumnWidth);
        Unloaded += (_, _) => App.Settings.SidebarColumnWidth = (int)SidebarColumn.ActualWidth;
    }

    private void OnVmChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryViewModel.View)) UpdateView();
        if (e.PropertyName == nameof(RepositoryViewModel.DetailsCollapsed)) UpdateDetails();
        if (e.PropertyName == nameof(RepositoryViewModel.IsVerticalLayout)) UpdateLayoutOrientation();
    }

    private void UpdateView()
    {
        var local = (DataContext as RepositoryViewModel)?.View == RepositoryView_LocalChanges;
        AllCommitsPanel.Visibility = local ? Visibility.Collapsed : Visibility.Visible;
        LocalChangesPanel.Visibility = local ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsVertical => (DataContext as RepositoryViewModel)?.IsVerticalLayout == true;

    /// <summary>
    /// 一覧と詳細の並び。Horizontal：一覧が上、詳細が下。Vertical：一覧が左（幅 VerticalLayoutRevisionListViewWidth）、詳細が右で高さいっぱい。
    /// </summary>
    private void UpdateLayoutOrientation()
    {
        var vertical = IsVertical;
        Grid.SetRow(ListView, 0); Grid.SetColumn(ListView, 0);
        Grid.SetRow(ListSplitter, vertical ? 0 : 1); Grid.SetColumn(ListSplitter, vertical ? 1 : 0);
        Grid.SetRow(DetailsView, vertical ? 0 : 2); Grid.SetColumn(DetailsView, vertical ? 2 : 0);
        Grid.SetRowSpan(ListView, vertical ? 3 : 1);
        Grid.SetRowSpan(ListSplitter, vertical ? 3 : 1);
        Grid.SetRowSpan(DetailsView, vertical ? 3 : 1);
        ListSplitter.Width = vertical ? 1 : double.NaN;
        ListSplitter.Height = vertical ? double.NaN : 1;
        ListSplitter.ResizeDirection = vertical ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        ListColumn.Width = vertical ? new GridLength(App.Settings.VerticalLayoutRevisionListViewWidth) : new GridLength(1, GridUnitType.Star);
        ListColumn.MinWidth = vertical ? 200 : 0;
        ListSplitterColumn.Width = new GridLength(vertical ? 1 : 0);
        DetailsColumn.Width = vertical ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        UpdateDetails();
    }

    private void ListSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (IsVertical && ListColumn.ActualWidth > 0) App.Settings.VerticalLayoutRevisionListViewWidth = (int)ListColumn.ActualWidth;
    }

    /// <summary>コミット詳細を畳む・戻す（Hide Commit Details）。畳んでもタブの帯（32px）は下に残る。縦レイアウトでは畳まない（未確認）。</summary>
    private void UpdateDetails()
    {
        if (IsVertical)
        {
            // 縦レイアウトでは行を 1 つだけ使う
            DetailsRow.MinHeight = 0;
            DetailsRow.Height = new GridLength(0);
            DetailsSplitterRow.Height = new GridLength(0);
            return;
        }
        var collapsed = (DataContext as RepositoryViewModel)?.DetailsCollapsed == true;
        DetailsRow.MinHeight = collapsed ? 32 : 80;
        DetailsRow.Height = collapsed ? new GridLength(32) : new GridLength(1, GridUnitType.Star);
        DetailsSplitterRow.Height = new GridLength(collapsed ? 0 : 1);
    }

    private const ViewModels.RepositoryView RepositoryView_LocalChanges = ViewModels.RepositoryView.LocalChanges;
}

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GitKnife.ViewModels;

namespace GitKnife.Views;

/// <summary>
/// 縦タブ：窓の左端に、今のワークスペース名の下にタブを並べる。
/// ツールバー左端の ToggleTabListSidebarToolbarButton で開閉（TabListSidebarCollapsed）、幅は TabListSidebarWidth。上のタブ帯はそのまま残る。
/// </summary>
public partial class MainWindow
{
    private readonly ObservableCollection<object> _tabNodes = [];
    private bool _syncingTabList;

    private void InitTabList()
    {
        WorkspaceTreeView.ItemsSource = _tabNodes;
        Vm.Tabs.CollectionChanged += (_, _) => RebuildTabList();
        Vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.SelectedTab)) SyncTabListSelection();
            else if (e.PropertyName == nameof(MainViewModel.Title)) RebuildTabList();
        };
        RebuildTabList();
        ApplyTabListState();
    }

    private void RebuildTabList()
    {
        _syncingTabList = true;
        _tabNodes.Clear();
        _tabNodes.Add(new TabListWorkspaceNode(Vm.Settings.Workspaces.ActiveWorkspace));
        foreach (var t in Vm.Tabs) _tabNodes.Add(new TabListTabNode(t));
        _syncingTabList = false;
        SyncTabListSelection();
    }

    private void SyncTabListSelection()
    {
        _syncingTabList = true;
        WorkspaceTreeView.SelectedItem = _tabNodes.OfType<TabListTabNode>().FirstOrDefault(n => n.Tab == Vm.SelectedTab);
        _syncingTabList = false;
    }

    private void WorkspaceTreeView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingTabList) return;
        if (WorkspaceTreeView.SelectedItem is TabListTabNode node) Vm.SelectedTab = node.Tab;
        else SyncTabListSelection();   // 見出しの行は選べない
    }

    private void WorkspaceTreeView_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>() is not { DataContext: TabListTabNode node }) { e.Handled = true; return; }
        WorkspaceTreeView.ContextMenu = BuildTabMenu(node.Tab);
    }

    private void TabListClose_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TabListTabNode node }) Vm.CloseTabCommand.Execute(node.Tab);
    }

    private void ToggleTabList_Click(object sender, RoutedEventArgs e)
    {
        App.Settings.TabListSidebarCollapsed = !App.Settings.TabListSidebarCollapsed;
        ApplyTabListState();
    }

    private void ApplyTabListState()
    {
        var open = !App.Settings.TabListSidebarCollapsed;
        TabListSidebar.Visibility = TabListSplitter.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        TabListColumn.Width = open ? new GridLength(Math.Max(80, App.Settings.TabListSidebarWidth)) : new GridLength(0);
        TabListColumn.MinWidth = open ? 80 : 0;
        TabListSplitterColumn.Width = open ? new GridLength(1) : new GridLength(0);
    }

    private void TabListSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        if (TabListColumn.ActualWidth > 0) App.Settings.TabListSidebarWidth = (int)TabListColumn.ActualWidth;
    }
}

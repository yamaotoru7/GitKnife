using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GitKnife.ViewModels;

namespace GitKnife.Views;

public partial class SidebarView : UserControl
{
    private RepositoryViewModel? Vm => DataContext as RepositoryViewModel;

    public SidebarView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is RepositoryViewModel old) old.Sidebar.SelectRequested -= SelectRows;
            if (e.NewValue is RepositoryViewModel vm) vm.Sidebar.SelectRequested += SelectRows;
        };
    }

    private void SelectRows(IReadOnlyList<SidebarItem> items)
    {
        foreach (var item in items) if (!Tree.SelectedItems.Contains(item)) Tree.SelectedItems.Add(item);
        Tree.ScrollIntoView(items[0]);
        Tree.Focus();
    }

    private void Expander_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SidebarItem item, IsEnabled: true }) Vm?.Sidebar.Toggle(item);
        e.Handled = true;   // ToggleButton が自分で IsChecked を変えないように
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && (sender as FrameworkElement)?.DataContext is SidebarItem item) _ = Vm.TogglePinAsync(item);
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && (sender as FrameworkElement)?.DataContext is SidebarItem item) _ = Vm.ToggleReferenceFilterAsync(item, hide: false);
    }

    private void HideButton_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && (sender as FrameworkElement)?.DataContext is SidebarItem item) _ = Vm.ToggleReferenceFilterAsync(item, hide: true);
    }

    /// <summary>2 つのブランチ・タグ・stash を Ctrl+クリックで選ぶと、そのコミットどうしを比べる（詳細が 2 コミットの比較になる）。</summary>
    private void Tree_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Tree.SelectedItems.Count != 2 || DataContext is not ViewModels.RepositoryViewModel vm) return;
        var items = Tree.SelectedItems.Cast<SidebarItem>().ToList();
        // 1 つ目を選んだときの一覧の移動（単独の詳細の読み込み）の後で比べる
        Dispatcher.BeginInvoke(() => vm.CompareSidebarItems(items[0], items[1]), System.Windows.Threading.DispatcherPriority.Background);
    }

    /// <summary>"Show all tags (N)" / "Show less" の行を押したら、すべて出すか省略に戻す。</summary>
    private void Tree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as SidebarItem;
        var reclicked = item is not null && ReferenceEquals(item, _pressedSelected);
        _pressedSelected = null;
        if (item is { Kind: SidebarItemKind.ShowMore }) Vm?.Sidebar.ToggleAllTags();
        // 選ばれたままの行をもう一度押したとき（Local Changes や別のコミットを見ている間）も、一覧をそのコミットへ動かす
        else if (reclicked && Keyboard.Modifiers == ModifierKeys.None && Tree.SelectedItems.Count == 1) Vm?.Sidebar.Renavigate(item!);
    }

    /// <summary>押した時点で、その行が 1 つだけ選ばれていた（押しても選択が変わらない）。</summary>
    private SidebarItem? _pressedSelected;

    private void Tree_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = (e.OriginalSource as FrameworkElement)?.DataContext as SidebarItem;
        _pressedSelected = item is not null && e.ClickCount == 1 && Tree.SelectedItems.Count == 1 && ReferenceEquals(Tree.SelectedItem, item) ? item : null;
    }

    private void Tree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Tree.SelectedItem is not SidebarItem item) return;
        if (item.CanExpand) { Vm?.Sidebar.Toggle(item); return; }
        // ローカルブランチのダブルクリックはダイアログなしでチェックアウト（D07）。リモートブランチは Checkout ダイアログ
        if (Vm is null || item.Reference is not { } r) return;
        // worktree はそのフォルダをタブで開く
        if (item.Kind == SidebarItemKind.Worktree && r.WorktreePath is { } path && Window.GetWindow(this)?.DataContext is MainViewModel main) { _ = main.OpenAsync(System.IO.Path.GetFullPath(path)); return; }
        if (item.Kind == SidebarItemKind.LocalBranch) RepositoryActions.CheckoutDirect(Vm, r);
        else if (item.Kind == SidebarItemKind.RemoteBranch) RepositoryActions.Checkout(Vm, r);
    }

    /// <summary>F2 でローカルブランチの名前を変える、Delete で削除（ブランチ・タグ・stash）。</summary>
    private void Tree_KeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is null || Tree.SelectedItem is not SidebarItem item) return;
        switch (e.Key)
        {
            case Key.F2 when item is { Kind: SidebarItemKind.LocalBranch, Reference: { } b }:
                RepositoryActions.RenameBranch(Vm, b);
                e.Handled = true;
                break;
            case Key.Delete when item is { Kind: SidebarItemKind.LocalBranch, Reference: { IsHead: false } b }:
                RepositoryActions.DeleteBranch(Vm, b);
                e.Handled = true;
                break;
            case Key.Delete when item is { Kind: SidebarItemKind.Tag, Reference: { } t }:
                RepositoryActions.DeleteTag(Vm, t.ShortName);
                e.Handled = true;
                break;
            case Key.Delete when item is { Kind: SidebarItemKind.Stash, Stash: { } s }:
                RepositoryActions.DeleteStash(Vm, s);
                e.Handled = true;
                break;
        }
    }

    /// <summary>右クリックした行のメニューを作る（行が無い・メニューが無い項目なら出さない）。</summary>
    private void Tree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var item = (e.OriginalSource as DependencyObject)?.FindAncestor<ListBoxItem>()?.DataContext as SidebarItem;
        if (Vm is null || item is null) { e.Handled = true; return; }
        // 選んだ行の中を右クリックしたら複数選択のメニュー（"Delete 2 Branches..." など）
        if (Tree.SelectedItems.Count > 1 && Tree.SelectedItems.Contains(item))
        {
            if (SidebarMenus.ForMany(Vm, Tree.SelectedItems.Cast<SidebarItem>().ToList()) is { } many) { Tree.ContextMenu = many; return; }
            e.Handled = true;
            return;
        }
        if (SidebarMenus.For(Vm, item) is not { } menu) { e.Handled = true; return; }
        Tree.SelectedItem = item;
        Tree.ContextMenu = menu;
    }

    // ---------------------------------------------------------- Search タブ

    /// <summary>Search タブを開いたら検索欄にフォーカス（開いてすぐ打てる）。</summary>
    private void SearchTab_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Dispatcher.BeginInvoke(() => { SearchBox.Focus(); SearchBox.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Vm is not null) { _ = Vm.Search.RunAsync(); e.Handled = true; }
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) Vm.Search.SearchText = "";
        SearchBox.Focus();
    }

    private void SearchExpander_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && (sender as FrameworkElement)?.DataContext is SearchRow row) Vm.Search.Toggle(row);
    }

    /// <summary>SearchDropdownButton：Commit Message / Author / Path / Diff Content、---、Search（見出し）、Repository / Current Branch。</summary>
    private void SearchMode_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button b) return;
        var s = Vm.Search;
        var m = new ContextMenu { PlacementTarget = b, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var t in new[] { Core.Git.RevisionSearchType.Message, Core.Git.RevisionSearchType.Author, Core.Git.RevisionSearchType.DiffPath, Core.Git.RevisionSearchType.DiffContent })
        {
            var item = new MenuItem { Header = SearchViewModel.TypeName(t), IsChecked = s.Type == t };
            item.Click += (_, _) => s.Type = t;
            m.Items.Add(item);
        }
        m.Items.Add(new Separator());
        m.Items.Add(new MenuItem { Header = "Search", IsEnabled = false });
        foreach (var (name, scope) in new[] { ("Repository", Core.Git.RevisionSearchScope.Repository), ("Current Branch", Core.Git.RevisionSearchScope.CurrentBranch) })
        {
            var item = new MenuItem { Header = name, IsChecked = s.Scope == scope };
            item.Click += (_, _) => s.Scope = scope;
            m.Items.Add(item);
        }
        m.IsOpen = true;
    }

    /// <summary>PART_DropDownButton：Recent Queries（見出し）、"Message	 "greet"" …、---、Clear Recents。</summary>
    private void RecentSearches_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not Button b) return;
        var s = Vm.Search;
        var m = new ContextMenu { PlacementTarget = b, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        m.Items.Add(new MenuItem { Header = "Recent Queries", IsEnabled = false });
        foreach (var q in s.Recent)
        {
            var item = new MenuItem { Header = $"{SearchViewModel.RecentName(q.Type)}\t \"{q.SearchString}\"" };
            item.Click += (_, _) => s.ApplyRecent(q);
            m.Items.Add(item);
        }
        m.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear Recents" };
        clear.Click += (_, _) => s.ClearRecents();
        m.Items.Add(clear);
        m.IsOpen = true;
    }

    private void RepositorySettings_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { ContextMenu: { } menu } b) { menu.PlacementTarget = b; menu.IsOpen = true; }
    }

    private void OpenRepositorySettings_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) RepositoryActions.RepositorySettings(Vm);
    }

    /// <summary>Rename Repository：タブの名前をその場で書き換える（タブの右クリックの Rename と同じ）。</summary>
    private void RenameRepository_Click(object sender, RoutedEventArgs e) => Vm?.BeginRename();

    private void CopyRepositoryPath_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) Clipboard.SetText(Vm.Path);
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitKnife.ViewModels;

namespace GitKnife.Views;

public partial class RevisionListView : UserControl
{
    public RevisionListView()
    {
        InitializeComponent();
        // ラベルの色はコンバーターがテーマを見て決めるので、テーマを替えたら行を作り直す（Window > Switch Theme）
        void Rethemed() => List.Items.Refresh();
        Loaded += (_, _) => ThemeManager.Changed += Rethemed;
        Unloaded += (_, _) => ThemeManager.Changed -= Rethemed;
        // 裏のタブ（見えていない）で選んだ行は、初めて見えたときに見える位置へ（2026-10-27 pytube：起動時に裏のタブで HEAD を選び、表示しても一覧の先頭のままだった）
        List.IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is not true || _pendingScroll is not { } pending) return;
            _pendingScroll = null;
            Dispatcher.BeginInvoke(() => { if (List.SelectedItem == pending) List.ScrollIntoView(pending); }, System.Windows.Threading.DispatcherPriority.Loaded);
        };
        // Ctrl+C で SHA、Ctrl+Shift+C でコミット情報をコピー
        List.InputBindings.Add(new KeyBinding(new RelayAction(CopySha), Key.C, ModifierKeys.Control));
        List.InputBindings.Add(new KeyBinding(new RelayAction(CopyInfo), Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        // Ctrl+F で一覧内検索、Space でコミット詳細のポップアップ
        List.InputBindings.Add(new KeyBinding(new RelayAction(OpenFind), Key.F, ModifierKeys.Control));
        List.InputBindings.Add(new KeyBinding(new RelayAction(TogglePopup), Key.Space, ModifierKeys.None));
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is RepositoryViewModel old) old.Find.MatchesChanged -= UpdateMarkers;
            if (e.OldValue is RepositoryViewModel o2) o2.PropertyChanged -= OnRepoChanged;
            if (e.NewValue is RepositoryViewModel repo) { repo.Find.MatchesChanged += UpdateMarkers; repo.PropertyChanged += OnRepoChanged; }
            ApplyRowTemplate();
            UpdateMarkers();
        };
        // マージの折りたたみ：← でたたむ、→ で開く、ノードのクリックで切り替え
        List.PreviewKeyDown += List_PreviewKeyDown;
        List.PreviewMouseLeftButtonDown += List_PreviewMouseLeftButtonDown;
        List.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => UpdateMarkers()));
        List.SizeChanged += (_, _) => UpdateMarkers();
    }

    private RepositoryViewModel? Repo => DataContext as RepositoryViewModel;

    private void OnRepoChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RepositoryViewModel.IsVerticalLayout)) ApplyRowTemplate();
    }

    /// <summary>横（1 行 23px）と縦（1 行 42px）で行のテンプレートを替える。</summary>
    private void ApplyRowTemplate()
    {
        var vertical = Repo?.IsVerticalLayout == true;
        List.ItemTemplate = (DataTemplate)FindResource(vertical ? "VerticalRowTemplate" : "HorizontalRowTemplate");
        Markers.RowHeight = vertical ? 42 : 23;
        UpdateMarkers();
    }

    // ---------------------------------------------------------- マージの折りたたみ（E03）

    private void List_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.None || Repo is not { } repo || Selected is not { } row) return;
        if (e.Key is not (Key.Left or Key.Right)) return;
        _ = repo.SetMergeCollapsedAsync(row, collapsed: e.Key == Key.Left);
        e.Handled = true;   // 一覧の横スクロールはしない
    }

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not GitKnife.Controls.GraphCell { Row: { IsMerge: true } graph } cell) return;
        if (cell.DataContext is not CommitRow row || Repo is not { } repo) return;
        var p = e.GetPosition(cell);
        var center = new Point(GitKnife.Controls.GraphCell.X(graph.NodeLane), cell.ActualHeight / 2);
        if ((p - center).Length > GitKnife.Controls.GraphCell.MergeNodeRadius + 2) return;
        repo.SelectedCommit = row;
        _ = repo.SetMergeCollapsedAsync(row, collapsed: !graph.IsCollapsedMerge);
        e.Handled = true;
    }

    // ---------------------------------------------------------- Ctrl+F（RevisionSearchPanelUserControl）

    private void OpenFind()
    {
        if (Repo is not { } repo) return;
        repo.Find.Open();
        FindBox.Focus();
        FindBox.SelectAll();
    }

    private void CloseFind()
    {
        Repo?.Find.Close();
        List.Focus();
        if (List.ItemContainerGenerator.ContainerFromItem(List.SelectedItem) is ListBoxItem item) item.Focus();
    }

    private void FindBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                Repo?.Find.Next(forward: (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
                e.Handled = true;
                break;
            case Key.Escape:
                CloseFind();
                e.Handled = true;
                break;
        }
    }

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => Repo?.Find.Next(forward: false);
    private void FindNext_Click(object sender, RoutedEventArgs e) => Repo?.Find.Next(forward: true);
    private void FindClose_Click(object sender, RoutedEventArgs e) => CloseFind();

    private ScrollViewer? _scroll;

    /// <summary>右端の一致の印。仮想化した一覧のスクロール位置は行の単位。</summary>
    private void UpdateMarkers()
    {
        _scroll ??= FindDescendant<ScrollViewer>(List);
        var matches = Repo?.Find.Matches ?? [];
        Markers.Update(matches, List.Items.Count, _scroll?.VerticalOffset ?? 0, _scroll?.ViewportHeight ?? List.Items.Count);
    }

    private static T? FindDescendant<T>(DependencyObject d) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
        {
            var c = VisualTreeHelper.GetChild(d, i);
            if (c is T t) return t;
            if (FindDescendant<T>(c) is { } found) return found;
        }
        return null;
    }

    // ---------------------------------------------------------- Space（コミット詳細のポップアップ）

    private RevisionPopupWindow? _popup;

    /// <summary>
    /// Space：選んだコミットの詳細を別の窓で出す。開いているときはもう一度 Space で閉じる。一覧で別の行を選んでも中身は追いかけない。
    /// </summary>
    private void TogglePopup()
    {
        if (_popup is { IsLoaded: true })
        {
            _popup.Close();
            return;
        }
        if (Repo is not { } repo || Selected is not { } row) return;
        _popup = new RevisionPopupWindow(repo, row) { Owner = Window.GetWindow(this) };
        _popup.Closed += (_, _) => _popup = null;
        _popup.Show();
    }

    private CommitRow? Selected => List.SelectedItem as CommitRow;
    private object? _pendingScroll;

    // 選んだコミット（Show HEAD、PARENTS のリンクなど）を見える位置に
    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (List.SelectedItems.Count == 1 && List.SelectedItem is { } item)
        {
            if (!List.IsVisible) _pendingScroll = item;
            List.ScrollIntoView(item);
            // 読み込み直後は行がまだ作られていないので、組み立ての後でもう一度（2026-10-27 pytube：HEAD が 210 行下で、選んだ行が画面の外のままだった）
            Dispatcher.BeginInvoke(() => { if (List.SelectedItem == item) List.ScrollIntoView(item); }, System.Windows.Threading.DispatcherPriority.Loaded);
        }
        if (DataContext is not RepositoryViewModel repo) return;
        // 2 つ選ぶと比較（古い方 = 一覧で下の方を先に）。1 つに戻ったら普通の表示に
        if (List.SelectedItems.Count == 2)
        {
            var rows = List.SelectedItems.Cast<CommitRow>().OrderByDescending(r => List.Items.IndexOf(r)).ToList();
            _ = repo.Details.LoadCompareAsync(rows[0], rows[1]);
        }
        else if (List.SelectedItems.Count == 1 && repo.Details.IsComparing && List.SelectedItem is CommitRow row)
            _ = repo.Details.LoadAsync(row);
    }

    /// <summary>右クリックした行のメニュー。</summary>
    private void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var row = (e.OriginalSource as System.Windows.DependencyObject)?.FindAncestor<ListBoxItem>()?.DataContext as CommitRow;
        if (row is null || DataContext is not RepositoryViewModel repo) { e.Handled = true; return; }
        // 2 つ選んでいてその片方を右クリック：2 つ用のメニュー
        if (List.SelectedItems.Count == 2 && List.SelectedItems.Contains(row))
        {
            List.ContextMenu = RevisionMenus.ForTwo(repo, List.SelectedItems.Cast<CommitRow>().OrderBy(r => List.Items.IndexOf(r)).ToList());
            return;
        }
        List.SelectedItem = row;
        List.ContextMenu = RevisionMenus.For(repo, row);
    }

    private void CopySha()
    {
        if (Selected is { } r) System.Windows.Clipboard.SetText(r.Commit.Sha);
    }

    private void CopyInfo()
    {
        if (Selected is not { } r) return;
        var c = r.Commit;
        System.Windows.Clipboard.SetText($"{c.ShortSha} {c.Subject}\nAuthor: {c.AuthorName} <{c.AuthorEmail}>\nDate: {c.AuthorTime:yyyy-MM-dd HH:mm:ss zzz}");
    }
}

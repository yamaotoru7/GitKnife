using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GitKnife.Controls;
using GitKnife.ViewModels;

namespace GitKnife.Views;

public partial class LocalChangesView : UserControl
{
    private LocalChangesViewModel? _vm;

    private void ApplySpellChecking()
    {
        var mode = App.Settings.CommitSpellCheckingMode;
        var lang = mode == 2 ? "en-US" : System.Globalization.CultureInfo.CurrentUICulture.Name;
        foreach (var box in new[] { SubjectBox, DescriptionBox })
        {
            box.Language = System.Windows.Markup.XmlLanguage.GetLanguage(lang);
            SpellCheck.SetIsEnabled(box, mode is 1 or 2);
        }
    }
    private bool _restoring;

    public LocalChangesView()
    {
        InitializeComponent();
        // 件名の欄にフォーカスが入ったら、空ならコミットのテンプレートを入れる
        SubjectBox.GotKeyboardFocus += (_, _) => _ = _vm?.ApplyCommitTemplateAsync();
        // スペルチェック（Preferences > Commit の "Spell checking:"。Disable / System (OS の言語) / English）
        ApplySpellChecking();
        App.SettingsChanged += (_, _) => ApplySpellChecking();
        // Space で大きな diff
        DiffPopupWindow.Attach(UnstagedList);
        DiffPopupWindow.Attach(StagedList);
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) { _vm.SelectionRestored -= OnSelectionRestored; _vm.FocusSubjectRequested -= OnFocusSubject; }
            _vm = e.NewValue as LocalChangesViewModel;
            if (_vm is not null) { _vm.SelectionRestored += OnSelectionRestored; _vm.FocusSubjectRequested += OnFocusSubject; }
            // 先に読み込んだ選択（先頭のファイルを選んだものなど）もビューに写す。後から作られたビューは通知を受け取っていない
            OnSelectionRestored(this, EventArgs.Empty);
        };
        // Ctrl+Enter でコミット、Ctrl+Alt+Enter で Commit and Push
        InputBindings.Add(new KeyBinding(new RelayAction(() => _vm?.CommitCommand.Execute(null)), Key.Enter, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayAction(() => _vm?.CommitAndPushCommand.Execute(null)), Key.Enter, ModifierKeys.Control | ModifierKeys.Alt));
        // Ctrl+F でファイル一覧のフィルタ
        InputBindings.Add(new KeyBinding(new RelayAction(OpenFilter), Key.F, ModifierKeys.Control));
        Loaded += (_, _) =>
        {
            if (Content is Grid g) g.ColumnDefinitions[0].Width = new GridLength(App.Settings.CommitViewColumnWidth);
            OnSelectionRestored(this, EventArgs.Empty);
        };
        Unloaded += (_, _) =>
        {
            if (Content is Grid g && g.ColumnDefinitions[0].ActualWidth > 0) App.Settings.CommitViewColumnWidth = (int)g.ColumnDefinitions[0].ActualWidth;
        };
    }

    // 表示が切り替わった後でフォーカスする
    private void OnFocusSubject(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(() => { SubjectBox.Focus(); SubjectBox.CaretIndex = SubjectBox.Text.Length; }, System.Windows.Threading.DispatcherPriority.Input);

    // ------------------------------------------------------------------ ファイル一覧

    private void OnSelectionRestored(object? sender, EventArgs e)
    {
        if (_vm is null) return;
        _restoring = true;
        try
        {
            Sync(UnstagedList, _vm.SelectedUnstaged);
            Sync(StagedList, _vm.SelectedStaged);
        }
        finally { _restoring = false; }

        static void Sync(ListView list, IReadOnlyList<FileListItem> selected)
        {
            list.SelectedItems.Clear();
            foreach (var s in selected) list.SelectedItems.Add(s);
        }
    }

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_restoring || _vm is null) return;
        var list = (ListView)sender;
        var staged = list == StagedList;
        var items = list.SelectedItems.Cast<FileListItem>().ToList();
        if (items.Count > 0)
        {
            // 反対側の選択は外す（diff は 1 つ）
            _restoring = true;
            (staged ? UnstagedList : StagedList).SelectedItems.Clear();
            _restoring = false;
        }
        _vm.SetSelection(staged, items);
    }

    /// <summary>ダブルクリックでステージ／アンステージ。フォルダは開閉。</summary>
    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null || (e.OriginalSource as DependencyObject)?.FindAncestor<ListViewItem>() is not { DataContext: FileListItem item }) return;
        if (item.IsFolder) { _vm.ToggleExpanded(item); return; }
        if (sender == StagedList) _ = _vm.Unstage(item.Entries); else _ = _vm.Stage(item.Entries);
    }

    /// <summary>Enter で選んだファイルをステージ／アンステージ（Unstaged で Enter → `add --force --verbose --pathspec-from-file=-`）。</summary>
    private void List_KeyDown(object sender, KeyEventArgs e)
    {
        if (_vm is null || e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        if (sender == StagedList) { if (_vm.CanUnstage) _ = _vm.UnstageSelected(); }
        else _ = _vm.StageSelected();
    }

    // ---- ドラッグ＆ドロップでステージ／アンステージ（公開リリースノート "ドラッグ＆ドロップで stage / unstage"。動きの細部は推定）----

    private const string DragFormat = "GitKnife.LocalChangesFiles";
    private Point? _dragStart;

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStart = (e.OriginalSource as DependencyObject)?.FindAncestor<ListViewItem>() is null ? null : e.GetPosition(null);

    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || sender is not ListView list || list.SelectedItems.Count == 0) return;
        var d = e.GetPosition(null) - start;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        DragDrop.DoDragDrop(list, new DataObject(DragFormat, list == StagedList ? "staged" : "unstaged"), DragDropEffects.Move);
    }

    private void List_DragOver(object sender, DragEventArgs e)
    {
        var from = e.Data.GetData(DragFormat) as string;
        var to = sender == StagedList ? "staged" : "unstaged";
        e.Effects = from is not null && from != to ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void List_Drop(object sender, DragEventArgs e)
    {
        if (_vm is null || e.Data.GetData(DragFormat) is not string from) return;
        e.Handled = true;
        if (sender == StagedList && from == "unstaged") _ = _vm.StageSelected();
        else if (sender == UnstagedList && from == "staged" && _vm.CanUnstage) _ = _vm.UnstageSelected();
    }

    private void List_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ListView { View: GridView gv } list && gv.Columns.Count > 0)
            gv.Columns[0].Width = Math.Max(50, list.ActualWidth - 10);
    }

    private void SearchButton_Click(object sender, RoutedEventArgs e)
    {
        if (FilterPanel.Visibility == Visibility.Visible) CloseFilter();
        else OpenFilter();
    }

    /// <summary>ファイル一覧のフィルタを出す（虫眼鏡 / Ctrl+F）。</summary>
    private void OpenFilter()
    {
        FilterPanel.Visibility = Visibility.Visible;
        FilterTextBox.Focus();
        FilterTextBox.SelectAll();
    }

    /// <summary>閉じる（Esc / ×）。絞り込みも消す。</summary>
    private void CloseFilter()
    {
        FilterPanel.Visibility = Visibility.Collapsed;
        if (_vm is not null) _vm.FileFilter = "";
    }

    private void FilterClose_Click(object sender, RoutedEventArgs e) => CloseFilter();

    private void FilterTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseFilter();
        e.Handled = true;
    }

    /// <summary>ファイル一覧の設定。</summary>
    private void FileListSettings_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var (text, mode) in new[] { ("View as Tree", FileListMode.Tree), ("View as List", FileListMode.List), ("View as Combined List", FileListMode.Combined) })
        {
            var item = new MenuItem { Header = text, FontWeight = App.Settings.FileListMode == (int)mode ? FontWeights.SemiBold : FontWeights.Normal };
            item.Click += (_, _) => { App.Settings.FileListMode = (int)mode; _vm?.SettingsChanged(); };
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        // 押すたびに切り替える。今の状態のときの文言（"Show Untracked Files" / "Hide Ignored Files"）は推定
        if (_vm is { } vm)
        {
            var repo = vm.Repository;
            var untracked = new MenuItem { Header = repo.HideUntracked ? "Show Untracked Files" : "Hide Untracked Files" };
            untracked.Click += (_, _) => { repo.HideUntracked = !repo.HideUntracked; vm.SettingsChanged(); };
            menu.Items.Add(untracked);
            var ignored = new MenuItem { Header = repo.ShowIgnored ? "Hide Ignored Files" : "Show Ignored Files" };
            ignored.Click += async (_, _) => { repo.ShowIgnored = !repo.ShowIgnored; await repo.RefreshStatusAsync(); };
            menu.Items.Add(ignored);
        }
        OpenBelow(menu, (FrameworkElement)sender);
    }

    /// <summary>右クリックした一覧のメニューを作る（右クリックした行が選択に入っていなければ、その行を選び直す）。</summary>
    private void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var list = (ListView)sender;
        if (_vm is null) { e.Handled = true; return; }
        if ((e.OriginalSource as DependencyObject)?.FindAncestor<ListViewItem>() is { DataContext: FileListItem row } && !list.SelectedItems.Contains(row))
        {
            list.SelectedItems.Clear();
            list.SelectedItems.Add(row);
        }
        var menu = LocalChangesMenus.For(_vm, list == StagedList, list.SelectedItems.Cast<FileListItem>().ToList());
        if (menu is null) { e.Handled = true; return; }
        list.ContextMenu = menu;
    }

    // ------------------------------------------------------------------ diff

    private void DiffView_StageRequested(object? sender, DiffLinesEventArgs e) => _vm?.ApplyLines(LinesAction.Stage, e.Lines);
    private void DiffView_UnstageRequested(object? sender, DiffLinesEventArgs e) => _vm?.ApplyLines(LinesAction.Unstage, e.Lines);
    private void DiffView_DiscardRequested(object? sender, DiffLinesEventArgs e) => _vm?.ApplyLines(LinesAction.Discard, e.Lines);

    private void DiffPanel_OptionsChanged(object? sender, EventArgs e) => _vm?.SettingsChanged();

    /// <summary>
    /// 競合の案内の ResolveButton：両方にチェックなら Merge Conflict Resolver を開く。片方だけならその側を使う（Resolve Using）。
    /// </summary>
    private void StageResolved_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.DiffEntry is { } entry) _ = _vm.Stage([entry]);
    }

    private void ExternalMergeButton_Loaded(object sender, RoutedEventArgs e)
    {
        var tool = ExternalTools.MergeTools.FirstOrDefault();
        ExternalMergeButton.Visibility = tool is null ? Visibility.Collapsed : Visibility.Visible;
        if (tool is not null) ExternalMergeButton.Content = $"Merge in {tool.Name}";
    }

    private void ExternalMerge_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Conflict is { } info && ExternalTools.MergeTools.FirstOrDefault() is { } tool) _ = _vm.ExternalMergeAsync(tool, info.Path);
    }

    private async void Resolve_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Conflict is not { } info || _vm.DiffEntry is not { } entry) return;
        if (info.UseTheirs != info.UseOurs) { await _vm.ResolveUsing([entry], ours: info.UseOurs); return; }
        if (Window.GetWindow(this)?.DataContext is not MainViewModel { SelectedTab: { } repo }) return;
        var text = await _vm.ReadConflictFile(info.Path);
        RepositoryActions.Run(repo, new ViewModels.Dialogs.MergeResolverViewModel(_vm, info, text));
    }

    // ------------------------------------------------------------------ コミット欄

    /// <summary>最近のコミットメッセージ。</summary>
    private void RecentMessages_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var menu = new ContextMenu();
        var ai = new MenuItem { Header = "Generate commit message with Claude" };
        ai.Click += (_, _) => Alerts.NotImplemented("Generate commit message with Claude");
        menu.Items.Add(ai);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Recent Commit Messages", IsEnabled = false });
        menu.Items.Add(new Separator());
        foreach (var c in _vm.RecentMessages)
        {
            var header = c.HasDescription ? c.Subject + "..." : c.Subject;
            var item = new MenuItem { Header = new AccessText { Text = header.Replace("_", "__") } };
            System.Windows.Automation.AutomationProperties.SetName(item, header);
            var commit = c;
            item.Click += (_, _) => { _vm.UseRecentMessage(commit); SubjectBox.Focus(); SubjectBox.CaretIndex = SubjectBox.Text.Length; };
            menu.Items.Add(item);
        }
        OpenBelow(menu, (FrameworkElement)sender);
    }

    /// <summary>コミットボタンの横のドロップダウン。</summary>
    private void CommitDropdown_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = "Commit Options", IsEnabled = false });
        menu.Items.Add(new Separator());
        var push = new MenuItem { Header = "Commit and Push", InputGestureText = "Ctrl+Alt+Enter" };
        push.Click += (_, _) => _vm.CommitAndPushCommand.Execute(null);
        menu.Items.Add(push);
        OpenBelow(menu, (FrameworkElement)sender);
    }

    private static void OpenBelow(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}

internal static class VisualTreeExtensions
{
    /// <summary>
    /// 親をたどって T を探す。Run などの文字の要素（ContentElement）はビジュアルツリーに無いので、論理ツリーで TextBlock まで上がってから続ける
    /// （コミット一覧の件名は Highlight で Run に分けているので、文字の上で右クリックすると Run から始まる）。
    /// </summary>
    public static T? FindAncestor<T>(this DependencyObject d) where T : DependencyObject
    {
        for (var cur = d; cur is not null; cur = Parent(cur))
            if (cur is T t) return t;
        return null;

        static DependencyObject? Parent(DependencyObject o) => o is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D
            ? System.Windows.Media.VisualTreeHelper.GetParent(o)
            : LogicalTreeHelper.GetParent(o);
    }
}

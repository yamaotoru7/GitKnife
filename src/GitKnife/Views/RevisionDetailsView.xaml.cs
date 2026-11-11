using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using GitKnife.ViewModels;

namespace GitKnife.Views;

public partial class RevisionDetailsView : UserControl
{
    private RevisionDetailsViewModel? _details;

    public RevisionDetailsView()
    {
        InitializeComponent();
        DiffPopupWindow.Attach(ChangesList);   // Space で大きな diff
        // ← / → でフォルダの開閉と親・子へ
        TreeKeys.AttachFiles(ChangesList, f => _details?.ToggleChangeFolder(f));
        TreeKeys.AttachFiles(FilesTree, f => _details?.ToggleTreeFolder(f));
        DataContextChanged += (_, e) =>
        {
            if (_details is not null) _details.PropertyChanged -= OnDetailsChanged;
            _details = (e.NewValue as IRevisionDetailsHost)?.Details;
            if (_details is not null) _details.PropertyChanged += OnDetailsChanged;
            Preview.Text = _details?.PreviewText ?? "";
        };
        Loaded += (_, _) =>
        {
            ChangesColumn.Width = new GridLength(App.Settings.RevisionDetailsChangesColumnWidth);
            FileTreeColumn.Width = new GridLength(App.Settings.RevisionDetailsFileTreeColumnWidth);
        };
        Unloaded += (_, _) =>
        {
            if (ChangesColumn.ActualWidth > 0) App.Settings.RevisionDetailsChangesColumnWidth = (int)ChangesColumn.ActualWidth;
            if (FileTreeColumn.ActualWidth > 0) App.Settings.RevisionDetailsFileTreeColumnWidth = (int)FileTreeColumn.ActualWidth;
        };
    }

    /// <summary>
    /// Space のポップアップと別ウィンドウの形：見出し（作者・SHA・日付・件名）をタブより上に、
    /// どのモードでも出す。折りたたむボタンは無い。diff の表示形式は RevisionWindowDiffLayoutMode。
    /// </summary>
    public bool IsStandalone
    {
        get => _isStandalone;
        set
        {
            _isStandalone = value;
            if (!value) return;
            System.Windows.Data.BindingOperations.ClearBinding(HeaderPane, VisibilityProperty);
            HeaderPane.Visibility = Visibility.Visible;
            Root.RowDefinitions[0].Height = GridLength.Auto;
            Root.RowDefinitions[1].Height = new GridLength(32);
            Grid.SetRow(HeaderPane, 0);
            Grid.SetRow(TabBar, 1);
            Grid.SetRow(CommitPane, 2);
            Grid.SetRowSpan(CommitPane, 1);
            CollapseButton.Visibility = Visibility.Collapsed;
            SeparateWindowButton.Visibility = Visibility.Hidden;   // UIA には出るが画面には見えない
            ChangesDiff.Place = DiffPlace.RevisionWindow;
        }
    }
    private bool _isStandalone;

    /// <summary>
    /// Interactive Rebase の下半分の形：メインの詳細ペインと同じ並びで、たたむボタンが無く、File Tree は押せない。
    /// </summary>
    public bool IsEmbedded
    {
        get => _isEmbedded;
        set
        {
            _isEmbedded = value;
            if (!value) return;
            CollapseButton.Visibility = Visibility.Collapsed;
            System.Windows.Data.BindingOperations.ClearBinding(FileTreeRadio, IsEnabledProperty);
            FileTreeRadio.IsEnabled = false;
        }
    }
    private bool _isEmbedded;

    private void Collapse_Click(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.ToggleDetails();

    /// <summary>右上のボタン：選んでいるコミットを別ウィンドウで開く（今のモードと選んでいるファイルを引き継ぐ）。</summary>
    private void SeparateWindow_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is IRevisionDetailsHost { SelectedCommit: { } row } host)
            RevisionWindow.Open(host.Repository, row, host.DetailsMode, host.Details.SelectedChange?.Path);
    }

    /// <summary>File Tree のプレビュー（AvalonEdit の Text は束縛できないのでここで入れる）。</summary>
    private void OnDetailsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RevisionDetailsViewModel.PreviewText))
        {
            Preview.Text = _details?.PreviewText ?? "";
            Preview.SyntaxHighlighting = GitKnife.Controls.SyntaxColors.For(_details?.SelectedTreeFile?.Path);
            Preview.ScrollToHome();
        }
    }

    /// <summary>PARENTS のリンク：そのコミットを一覧で選ぶ。</summary>
    private void Parent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string sha } && DataContext is IRevisionDetailsHost host) host.NavigateToCommit(sha);
    }

    private void List_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is ListView { View: GridView gv } list && gv.Columns.Count > 0)
            gv.Columns[0].Width = Math.Max(50, list.ActualWidth - 10);
    }

    private void ChangesDiff_OptionsChanged(object? sender, EventArgs e) => _ = _details?.LoadDiffAsync();

    // ファイルの右クリック（RevisionFileMenus）。右クリックした行を選んでから開く
    private void Changes_ContextMenuOpening(object sender, ContextMenuEventArgs e) => OpenFileMenu((ListView)sender, e, fileTree: false);
    private void FileTree_ContextMenuOpening(object sender, ContextMenuEventArgs e) => OpenFileMenu((ListView)sender, e, fileTree: true);

    /// <summary>
    /// フォルダの行は右クリックで選ばない（選んでいるファイルとプレビューはそのまま）。
    /// </summary>
    private void FileTree_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindAncestor<ListViewItem>() is { DataContext: FileListItem { IsFolder: true } }) e.Handled = true;
    }

    private void OpenFileMenu(ListView list, ContextMenuEventArgs e, bool fileTree)
    {
        if (_details is null || (e.OriginalSource as DependencyObject)?.FindAncestor<ListViewItem>() is not { DataContext: FileListItem row }) { e.Handled = true; return; }
        if (list.SelectedItem != row && !(fileTree && row.IsFolder)) list.SelectedItem = row;
        if (RevisionFileMenus.For(_details, row, fileTree) is not { } menu) { e.Handled = true; return; }
        // ContextMenu を差し替えると最初の 1 回は開かない。今の ContextMenu の中身を入れ替える
        var target = list.ContextMenu ??= new ContextMenu();
        target.Items.Clear();
        var items = menu.Items.Cast<object>().ToList();
        menu.Items.Clear();
        foreach (var item in items) target.Items.Add(item);
    }

    // ---- Changes のファイル一覧の絞り込み（Local Changes の FilterPanel と同じ動き）----

    private void ChangesSearch_Click(object sender, RoutedEventArgs e)
    {
        if (ChangesFilterPanel.Visibility == Visibility.Visible) { CloseChangesFilter(); return; }
        ChangesFilterPanel.Visibility = Visibility.Visible;
        ChangesFilterTextBox.Focus();
        ChangesFilterTextBox.SelectAll();
    }

    private void CloseChangesFilter()
    {
        ChangesFilterPanel.Visibility = Visibility.Collapsed;
        if (DataContext is ViewModels.RevisionDetailsViewModel vm) vm.ChangeFilter = "";
    }

    private void ChangesFilterClose_Click(object sender, RoutedEventArgs e) => CloseChangesFilter();

    private void ChangesFilter_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        CloseChangesFilter();
        e.Handled = true;
    }
}

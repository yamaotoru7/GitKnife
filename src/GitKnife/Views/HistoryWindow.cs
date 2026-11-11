using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Markup;
using GitKnife.Core.Diff;
using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;
using GitKnife.ViewModels;

namespace GitKnife.Views;

/// <summary>File History の 1 行の表示用（作者、日付、SHA、件名、変化のアイコン、パス）。</summary>
public sealed class HistoryItem(FileHistoryEntry entry, bool isSubItem = false)
{
    public FileHistoryEntry Entry { get; } = entry;
    /// <summary>フォルダの History のコミットの下の行。</summary>
    public bool IsSubItem { get; } = isSubItem;
    public string Author => Entry.Author;
    public string Email => Entry.Email;
    public string Date => Entry.Date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    public string ShortSha => Entry.ShortSha;
    public string Subject => Entry.Subject;
    public string Path => Entry.OldPath is { } old ? $"{old} → {Entry.Path}" : Entry.Path;
    public string ChangeLetter => Entry.Change switch
    {
        FileChange.Added or FileChange.Copied => "+",
        FileChange.Deleted => "−",
        FileChange.Renamed => "R",
        _ => "M",
    };
}

/// <summary>
/// フォルダの History のコミットの行（開閉、アバター、件名、日付、別窓で開く）。下にファイルの行（HistoryItem）が並ぶ。
/// </summary>
public sealed class HistoryCommitRow(FolderHistoryCommit commit)
{
    public FolderHistoryCommit Commit { get; } = commit;
    public string Author => Commit.Author;
    public string Email => Commit.Email;
    public string Subject => Commit.Subject;
    public string Date => Commit.Date.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture);
    public bool IsExpanded { get; set; } = true;
    public IReadOnlyList<HistoryItem> Files { get; } = commit.Files.Select(f => new HistoryItem(f, isSubItem: true)).ToList();
}

/// <summary>
/// File History。別窓 "app.cs - History"。
/// 左に TreeView（ファイルを変えたコミット：作者・AuthorDateTextBlock・ShaTextBlock、RevisionSubjectTextBlock、変化のアイコンと PathTextBlock）、
/// 右に選んだコミットでのファイルの diff（親と。`diff &lt;sha&gt;~1 &lt;sha&gt; --unified=19 -- path`）。上の時間軸は未実装。
/// </summary>
internal sealed class HistoryWindow : ToolWindow
{
    private readonly FileHistoryReader _reader;
    private readonly string _path;
    private readonly ListView _list;
    private readonly FileDiffPanel _diff;
    private readonly RepositoryViewModel _repo;

    /// <summary>フォルダの History（コミットの行の下にファイルの行。File Tree のフォルダの History...）。</summary>
    private readonly bool _folder;
    private IReadOnlyList<HistoryCommitRow> _commits = [];

    public HistoryWindow(RepositoryViewModel repo, string path, bool folder = false)
        : base("History", path, App.Settings.HistoryWindowLocationState, 1100, 720)
    {
        _reader = new FileHistoryReader(repo.Git, repo.Path);
        _path = path;
        _repo = repo;
        _folder = folder;
        // 見出しの右に今のブランチ
        if (repo.References?.HeadBranch is { } head)
        {
            var branch = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            branch.Children.Add(new GitKnife.Controls.IconImage { Geometry = (System.Windows.Media.Geometry)FindResource("Icon.Branch"), Width = 14, Height = 14, Margin = new Thickness(0, 0, 4, 0) });
            branch.Children.Add(new TextBlock { Text = head, VerticalAlignment = VerticalAlignment.Center });
            HeaderRight.Children.Add(branch);
        }

        var grid = new Grid { Margin = new Thickness(20, 4, 20, 20) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        _list = new ListView { BorderThickness = new Thickness(1), SelectionMode = SelectionMode.Extended };
        if (folder) _list.ItemTemplateSelector = new FolderRowSelector((DataTemplate)XamlReader.Parse(CommitRowTemplate), (DataTemplate)XamlReader.Parse(SubRowTemplate));
        else _list.ItemTemplate = (DataTemplate)XamlReader.Parse(RowTemplate);
        AutomationProperties.SetAutomationId(_list, "TreeView");
        _list.SetResourceReference(BackgroundProperty, "Gk.ToolWindow.List");   // #212121
        _list.SetResourceReference(BorderBrushProperty, "Gk.Border");
        _list.SetResourceReference(ForegroundProperty, "Gk.Text");
        _list.SetResourceReference(ItemsControl.ItemContainerStyleProperty, "Gk.HistoryItem");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        // ← / → でコミットの行の開閉と親・子へ（フォルダの History）
        TreeKeys.Attach(_list, row => row switch
            {
                HistoryCommitRow c => new(c.Files.Count > 0, c.IsExpanded, 0),
                HistoryItem { IsSubItem: true } => new(false, false, 1),
                _ => null,
            },
            row => { var c = (HistoryCommitRow)row; c.IsExpanded = !c.IsExpanded; Refill(c); return c; });
        _list.SelectionChanged += async (_, _) => { Timeline.Selected = CommitIndexOf(_list.SelectedItem); await LoadDiffAsync(); };
        Timeline.CommitClicked += (_, i) => _list.SelectedItem = RowOfCommit(i);
        _list.ContextMenu = new ContextMenu();
        _list.ContextMenuOpening += List_ContextMenuOpening;
        // 行の右上の ⇗：そのコミットを別ウィンドウで（Open in Separate Window... と同じ）
        _list.AddHandler(Button.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.OriginalSource is Button { DataContext: HistoryItem item }) RevisionWindow.OpenCommit(_repo, item.Entry.Sha, item.Entry.Author, item.Entry.Email, item.Entry.Date, item.Entry.Subject);
            else if (e.OriginalSource is Button { Tag: "expander", DataContext: HistoryCommitRow row }) { row.IsExpanded = !row.IsExpanded; Refill(row); }
            else if (e.OriginalSource is Button { DataContext: HistoryCommitRow c }) RevisionWindow.OpenCommit(_repo, c.Commit.Sha, c.Author, c.Email, c.Commit.Date, c.Subject);
        }));
        grid.Children.Add(_list);
        var line = new Border();
        line.SetResourceReference(Border.BackgroundProperty, "Gk.Border");
        Grid.SetColumn(line, 1);
        grid.Children.Add(line);
        _diff = new FileDiffPanel { Place = DiffPlace.Revision, ShowActions = false, Repository = repo };
        _diff.OptionsChanged += async (_, _) => await LoadDiffAsync();
        Grid.SetColumn(_diff, 2);
        grid.Children.Add(_diff);
        Body = grid;

        Loaded += async (_, _) => await LoadAsync();
    }

    /// <summary>
    /// 行の右クリック：Open in Separate Window...、Reveal in GitKnife、Open、
    /// Show in File Explorer、---、Reset File to State at Commit...、---、Save as...、（---、ファイルとコミットのカスタムコマンド）、---、Copy Commit SHA、Copy Commit Info。
    /// </summary>
    private void List_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindAncestor<ListViewItem>() is not { DataContext: HistoryItem item }) { e.Handled = true; return; }
        // 2 つ選んでいてその片方を右クリック：External Diff だけ
        if (_list.SelectedItems.Count == 2 && _list.SelectedItems.Contains(item))
        {
            _list.ContextMenu = TwoSelectedMenu();
            return;
        }
        _list.SelectedItem = item;
        var h = item.Entry;
        var full = System.IO.Path.Combine(_repo.Path, h.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var m = new ContextMenu();
        MenuItem Add(string header, Action action)
        {
            var mi = new MenuItem { Header = new AccessText { Text = header.Replace("_", "__") } };
            AutomationProperties.SetName(mi, header);
            mi.Click += (_, _) => action();
            m.Items.Add(mi);
            return mi;
        }
        Add("Open in Separate Window...", () => RevisionWindow.OpenCommit(_repo, h.Sha, h.Author, h.Email, h.Date, h.Subject));
        Add("Reveal in GitKnife", () =>
        {
            _repo.View = ViewModels.RepositoryView.AllCommits;
            if (_repo.Commits.FirstOrDefault(c => c.Commit.Sha == h.Sha) is { } row) _repo.SelectedCommit = row;
            Application.Current.MainWindow?.Activate();
        });
        Add("Open", () => _ = RevisionFileMenus.OpenRevisionFile(_repo, h.Sha, h.Path)).IsEnabled = h.Change != FileChange.Deleted;
        Add("Show in File Explorer", () => RepositoryActions.Explorer(Core.Git.HostFile.Exists(full) ? $"/select,\"{full}\"" : $"\"{_repo.Path}\""));
        m.Items.Add(new Separator());
        Add("Reset File to State at Commit...", () => RevisionFileMenus.ResetFile(_repo, h.Sha, h.Path, "at"));
        m.Items.Add(new Separator());
        Add("Save as...", () => _ = RevisionFileMenus.SaveAs(_repo, h.Sha, h.Path)).IsEnabled = h.Change != FileChange.Deleted;
        // カスタムコマンド：ファイル、続けてコミット（それぞれ区切りを挟む）
        var ctx = new Core.Settings.CustomCommandContext(_repo.Path) { Sha = h.Sha, ShortSha = h.ShortSha, File = h.Path };
        CustomCommandMenus.AddTo(m, _repo, "file", () => ctx);
        CustomCommandMenus.AddTo(m, _repo, "revision", () => ctx);
        m.Items.Add(new Separator());
        Add("Copy Commit SHA", () => RepositoryActions.Copy(h.Sha));
        Add("Copy Commit Info", () => RepositoryActions.Copy($"{h.ShortSha} {h.Subject}{Environment.NewLine}Author: {h.Author} <{h.Email}>{Environment.NewLine}Date: {h.Date:yyyy-MM-dd HH:mm:ss zzz}"));
        _list.ContextMenu = m;
    }

    /// <summary>古い方と新しい方（一覧で下にある方が古い）。2 つ選んでいないときは null。</summary>
    private (HistoryItem Older, HistoryItem Newer)? SelectedPair()
    {
        if (_list.SelectedItems.Count != 2) return null;
        var items = _list.SelectedItems.OfType<HistoryItem>().OrderBy(i => _list.Items.IndexOf(i)).ToList();
        if (items.Count != 2) return null;
        return (items[1], items[0]);
    }

    private ContextMenu TwoSelectedMenu()
    {
        var m = new ContextMenu();
        var diff = new MenuItem { Header = "External Diff" };
        foreach (var t in ExternalTools.DiffTools)
        {
            var tool = t;
            var mi = new MenuItem { Header = t.Name };
            mi.Click += async (_, _) =>
            {
                if (SelectedPair() is not { } pair) return;
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GitKnife", "diff-" + Guid.NewGuid().ToString("N")[..8]);
                Core.Git.HostDirectory.CreateDirectory(dir);
                async Task<string> Write(HistoryItem i)
                {
                    var file = System.IO.Path.Combine(dir, i.Entry.ShortSha + "_" + System.IO.Path.GetFileName(i.Entry.Path));
                    var r = await _repo.Git.RunAsync(new GitRequest(_repo.Path, ["show", $"{i.Entry.Sha}:{i.Entry.Path}"]));
                    await Core.Git.HostFile.WriteAllTextAsync(file, r.Succeeded ? r.StandardOutput : "");
                    return file;
                }
                tool.Open(await Write(pair.Older), await Write(pair.Newer));
            };
            diff.Items.Add(mi);
        }
        diff.IsEnabled = diff.Items.Count > 0;
        m.Items.Add(diff);
        return m;
    }

    private async Task LoadAsync()
    {
        try
        {
            if (_folder)
            {
                _commits = (await _reader.ReadFolderHistoryAsync(_path)).Select(c => new HistoryCommitRow(c)).ToList();
                Refill(null);
                Timeline.SetCommits(_commits.Select(c => c.Commit.Date).ToList(), 0);
                // 先頭のコミットの最初のファイルを選ぶ（diff を出す）
                _list.SelectedItem = _commits.FirstOrDefault()?.Files.FirstOrDefault() ?? (object?)_commits.FirstOrDefault();
                return;
            }
            var history = await _reader.ReadHistoryAsync(_path);
            _list.ItemsSource = history.Select(h => new HistoryItem(h)).ToList();
            Timeline.SetCommits(history.Select(h => h.Date).ToList(), 0);
            _list.SelectedIndex = history.Count > 0 ? 0 : -1;
        }
        catch (GitException ex) { Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
    }

    /// <summary>フォルダの History の行を作り直す（開いているコミットの下にファイルの行）。keep の行を選んだままにする。</summary>
    private void Refill(HistoryCommitRow? keep)
    {
        var rows = new List<object>();
        foreach (var c in _commits)
        {
            rows.Add(c);
            if (c.IsExpanded) rows.AddRange(c.Files);
        }
        var selected = _list.SelectedItem;
        _list.ItemsSource = rows;
        _list.SelectedItem = selected is not null && rows.Contains(selected) ? selected : keep;
    }

    /// <summary>行が属するコミットの番号（時間軸の印）。</summary>
    private int CommitIndexOf(object? row)
    {
        if (!_folder) return _list.SelectedIndex;
        for (var i = 0; i < _commits.Count; i++)
            if (_commits[i] == row || _commits[i].Files.Contains(row)) return i;
        return -1;
    }

    private object? RowOfCommit(int i)
    {
        if (!_folder) return i >= 0 && i < _list.Items.Count ? _list.Items[i] : null;
        if (i < 0 || i >= _commits.Count) return null;
        var c = _commits[i];
        return c.IsExpanded && c.Files.Count > 0 ? c.Files[0] : c;
    }

    private async Task LoadDiffAsync()
    {
        if (SelectedPair() is { } pair)
        {
            _diff.Title = pair.Newer.Entry.Path;
            try
            {
                var compare = await _reader.ReadCompareDiffAsync(pair.Older.Entry, pair.Newer.Entry, App.Settings.DiffShowEntireFile);
                if (SelectedPair() == pair) _diff.Diff = compare;
            }
            catch (GitException ex) { Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
            return;
        }
        var item = _list.SelectedItem as HistoryItem ?? (_list.SelectedItem as HistoryCommitRow)?.Files.FirstOrDefault();
        if (item is null) { _diff.Diff = null; return; }
        _diff.Title = item.Entry.Path;
        try
        {
            FileDiff? diff;
            try { diff = await _reader.ReadDiffAsync(item.Entry, hasParent: true, fullFile: App.Settings.DiffShowEntireFile); }
            catch (GitException) { diff = await _reader.ReadDiffAsync(item.Entry, hasParent: false, fullFile: App.Settings.DiffShowEntireFile); }   // ルートコミット
            if (_list.SelectedItem == item || (_list.SelectedItem as HistoryCommitRow)?.Files.FirstOrDefault() == item) _diff.Diff = diff;
        }
        catch (GitException ex) { Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
    }

    /// <summary>フォルダの History の行の種類で見た目を選ぶ。</summary>
    private sealed class FolderRowSelector(DataTemplate commit, DataTemplate file) : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object item, DependencyObject container) => item is HistoryCommitRow ? commit : file;
    }

    // コミットの行：23px。開閉（ExpandCollapseToggleButton）、アバター 14px（AuthorAvatarImage）、件名（RevisionSubjectTextBlock）、
    // 日付（AuthorDateTextBlock）、別窓で開くボタン
    private const string CommitRowTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:c="clr-namespace:GitKnife.Controls;assembly=GitKnife">
            <Grid Height="23">
                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <Button Tag="expander" AutomationProperties.AutomationId="ExpandCollapseToggleButton" Width="16" Height="16" Padding="0" Margin="0,0,4,0" Style="{DynamicResource Gk.FlatButton}" Focusable="False" VerticalAlignment="Center">
                    <Path Data="{DynamicResource Icon.ChevronDown}" Width="10" Height="10" Stretch="Uniform" RenderTransformOrigin="0.5,0.5">
                        <Path.Style>
                            <Style TargetType="Path" BasedOn="{StaticResource Gk.IconPath}">
                                <Style.Triggers>
                                    <!-- 閉じているときは右向き（›）。開閉のたびに行を作り直すので、ここで向きが決まる -->
                                    <DataTrigger Binding="{Binding IsExpanded}" Value="False">
                                        <Setter Property="RenderTransform"><Setter.Value><RotateTransform Angle="-90"/></Setter.Value></Setter>
                                    </DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Path.Style>
                    </Path>
                </Button>
                <c:Avatar Grid.Column="1" Width="14" Height="14" NameText="{Binding Author}" Email="{Binding Email}" Margin="0,0,8,0" VerticalAlignment="Center" AutomationProperties.AutomationId="AuthorAvatarImage"/>
                <TextBlock Grid.Column="2" Text="{Binding Subject}" AutomationProperties.AutomationId="RevisionSubjectTextBlock" FontSize="13" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="3" Text="{Binding Date}" AutomationProperties.AutomationId="AuthorDateTextBlock" Margin="6,0,4,0" VerticalAlignment="Center"/>
                <Button Grid.Column="4" Width="14" Height="14" Padding="0" Style="{DynamicResource Gk.FlatButton}" ToolTip="Open in Separate Window" Focusable="False" VerticalAlignment="Center">
                    <Path Data="{DynamicResource Icon.ExternalWindow}" Style="{DynamicResource Gk.IconPath}" Width="12" Height="12"/>
                </Button>
            </Grid>
        </DataTemplate>
        """;

    // ファイルの行：23px。変化の印 12px、ファイルのアイコン 12px、PathTextBlock
    private const string SubRowTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:c="clr-namespace:GitKnife.Controls;assembly=GitKnife">
            <StackPanel Orientation="Horizontal" Height="23" Margin="20,0,0,0">
                <Border Style="{DynamicResource Gk.ChangeBadge}" Width="12" Height="12" Margin="0,0,5,0" VerticalAlignment="Center">
                    <TextBlock Text="{Binding ChangeLetter}" FontSize="9" FontWeight="Bold" Foreground="#FFFFFF" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,-1,0,0"/>
                </Border>
                <Image Source="{Binding Path, Converter={x:Static c:ShellIconConverter.Instance}}" Width="12" Height="12" Margin="0,0,3,0" VerticalAlignment="Center"
                       xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"/>
                <TextBlock Text="{Binding Path}" AutomationProperties.AutomationId="PathTextBlock" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
            </StackPanel>
        </DataTemplate>
        """;

    private const string RowTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:c="clr-namespace:GitKnife.Controls;assembly=GitKnife">
            <Grid Margin="2,3,2,4">
                <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
                <Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
                <c:Avatar Width="14" Height="14" NameText="{Binding Author}" Email="{Binding Email}" Margin="0,0,8,0" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="1" Text="{Binding Author}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="2" Text="{Binding Date}" AutomationProperties.AutomationId="AuthorDateTextBlock" Margin="8,0,6,0" VerticalAlignment="Center"/>
                <TextBlock Grid.Column="3" Text="{Binding ShortSha}" AutomationProperties.AutomationId="ShaTextBlock" VerticalAlignment="Center"/>
                <Button Grid.Column="4" Width="14" Height="14" Margin="4,0,0,0" Padding="0" Style="{DynamicResource Gk.FlatButton}" ToolTip="Open in Separate Window" Focusable="False">
                    <Path Data="{DynamicResource Icon.ExternalWindow}" Style="{DynamicResource Gk.IconPath}" Width="12" Height="12"/>
                </Button>
                <TextBlock Grid.Row="1" Grid.ColumnSpan="5" Text="{Binding Subject}" AutomationProperties.AutomationId="RevisionSubjectTextBlock" FontSize="13.5" TextTrimming="CharacterEllipsis" Margin="0,2,0,2"/>
                <StackPanel Grid.Row="2" Grid.ColumnSpan="5" Orientation="Horizontal">
                    <Border Style="{DynamicResource Gk.ChangeBadge}" Margin="0,0,8,0">
                        <TextBlock Text="{Binding ChangeLetter}" FontSize="10" FontWeight="Bold" Foreground="#FFFFFF" HorizontalAlignment="Center" VerticalAlignment="Center" Margin="0,-1,0,0"/>
                    </Border>
                    <TextBlock Text="{Binding Path}" AutomationProperties.AutomationId="PathTextBlock" TextTrimming="CharacterEllipsis"/>
                </StackPanel>
            </Grid>
        </DataTemplate>
        """;
}

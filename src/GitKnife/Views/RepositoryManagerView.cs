using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitKnife.Controls;
using GitKnife.Core.Git;
using GitKnife.Core.Settings;
using GitKnife.ViewModels;
using GitKnife.Views.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// Repository Manager のタブ。
/// 左：見出し "Repository Manager" と RepositoriesTreeView（"Recent"：最近開いた 5 件、"Repositories"：フォルダとリポジトリ。名前順、フォルダが先）。
/// 一覧は repositories.toml（RepositoryList）。フォルダはソースフォルダの下の途中のフォルダ（RepositoryTree。T110）。
/// 右：RepositoryDetailsUserControl（RepositoryName、RepositoryPath、Open、Uncommitted Files / Commits / Initial Commit / Last Commit / Remotes、
/// Summary タブの Branches / Tags / Commits / README）。ダブルクリックか Open で開く。Statistics は未実装。
/// </summary>
internal sealed class RepositoryManagerView : UserControl
{
    private readonly ListView _tree;
    private readonly ContentControl _details = new();
    /// <summary>節（Recent / Repositories）は閉じたものを、フォルダは開いたものを覚える（フォルダは閉じた状態で出す）。</summary>
    private readonly HashSet<string> _collapsed = [];
    private readonly HashSet<string> _expandedFolders = [];
    private bool IsCollapsed(string key) => key.StartsWith("folder:", StringComparison.Ordinal) ? !_expandedFolders.Contains(key) : _collapsed.Contains(key);
    private void Toggle(string key)
    {
        var set = key.StartsWith("folder:", StringComparison.Ordinal) ? _expandedFolders : _collapsed;
        if (!set.Remove(key)) set.Add(key);
        SaveExpanded();
    }

    private bool _expandedLoaded;

    /// <summary>
    /// 開閉の状態（設定 RepositoryManagerTreeViewExpandedItems。形：開いている節とフォルダを入れ子にしたリスト
    /// `[{"title":"Recent","children":[]},{"title":"Repositories","children":[{"title":"Apps","children":[]}]}]`）。null なら既定（節は開く、フォルダは閉じる）。
    /// </summary>
    private void LoadExpanded()
    {
        if (_expandedLoaded) return;
        _expandedLoaded = true;
        if (Settings.RepositoryManagerTreeViewExpandedItems is not { ValueKind: System.Text.Json.JsonValueKind.Array } items) return;
        static string? Title(System.Text.Json.JsonElement e) => e.TryGetProperty("title", out var t) ? t.GetString() : null;
        var sections = items.EnumerateArray().ToList();
        if (!sections.Any(e => Title(e) == "Recent")) _collapsed.Add("section:recent");
        if (!sections.Any(e => Title(e) == "Repositories")) _collapsed.Add("section:repos");
        void Walk(System.Text.Json.JsonElement children, string parentKey)
        {
            foreach (var c in children.EnumerateArray())
            {
                if (Title(c) is not { Length: > 0 } name) continue;
                var key = parentKey.Length == 0 ? name : parentKey + "\\" + name;
                _expandedFolders.Add("folder:" + key.ToUpperInvariant());
                if (c.TryGetProperty("children", out var cc) && cc.ValueKind == System.Text.Json.JsonValueKind.Array) Walk(cc, key);
            }
        }
        foreach (var e in sections)
            if (Title(e) == "Repositories" && e.TryGetProperty("children", out var ch) && ch.ValueKind == System.Text.Json.JsonValueKind.Array) Walk(ch, "");
    }

    private void SaveExpanded()
    {
        List<object> Folders(RepositoryFolder parent) => parent.Folders
            .Where(f => _expandedFolders.Contains(FolderKey(f)))
            .Select(f => (object)new { title = f.Name, children = Folders(f) })
            .ToList();
        var list = new List<object>();
        if (!_collapsed.Contains("section:recent")) list.Add(new { title = "Recent", children = new List<object>() });
        if (!_collapsed.Contains("section:repos")) list.Add(new { title = "Repositories", children = _root is null ? [] : Folders(_root) });
        Settings.RepositoryManagerTreeViewExpandedItems = System.Text.Json.JsonSerializer.SerializeToElement(list);
    }

    private static string FolderKey(RepositoryFolder f) => "folder:" + f.Key.ToUpperInvariant();

    /// <summary>いま出している木（Fill で作り直す）。</summary>
    private RepositoryFolder? _root;
    private MainViewModel? Main => Application.Current.MainWindow?.DataContext as MainViewModel;
    private AppSettings Settings => App.Settings;

    private sealed record Row(string Key, string Name, int Level, bool IsSection, RepositoryFolder? Folder, RepositoryListEntry? Repository, bool Expandable)
    {
        public bool IsFolder => Folder is not null;
    }

    public RepositoryManagerView()
    {
        AutomationProperties.SetAutomationId(this, "RepositoryManagerUserControl");
        // 文字は 13（ほかの画面の 12 より大きい。2026-10-26 ユーザーの指摘「文字の大きさ、見やすさが違う」）
        FontSize = 13;
        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(350) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new DockPanel();
        left.SetResourceReference(BackgroundProperty, "Gk.Manager.Tree");
        var title = new TextBlock { Text = "Repository Manager", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(20, 10, 0, 6) };
        DockPanel.SetDock(title, Dock.Top);
        left.Children.Add(title);
        _tree = new ListView { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch };
        AutomationProperties.SetAutomationId(_tree, "RepositoriesTreeView");
        _tree.SetResourceReference(ForegroundProperty, "Gk.Text");
        _tree.ItemContainerStyle = RowStyle();
        ScrollViewer.SetHorizontalScrollBarVisibility(_tree, ScrollBarVisibility.Disabled);
        // ← / → で節とフォルダの開閉と親・子へ（行は Fill で作り直すので、同じキーの新しい行を選ぶ）
        TreeKeys.Attach(_tree, item => (item as ListViewItem)?.Tag is Row r ? new(r.Expandable, !IsCollapsed(r.Key), r.Level) : null,
            item =>
            {
                var key = ((Row)((ListViewItem)item).Tag).Key;
                Toggle(key);
                Fill();
                return _tree.Items.OfType<ListViewItem>().FirstOrDefault(i => (i.Tag as Row)?.Key == key);
            });
        _tree.SelectionChanged += async (_, _) => await ShowDetailsAsync();
        _tree.MouseDoubleClick += (_, e) => { if ((_tree.SelectedItem as ListViewItem)?.Tag is Row { Repository: { } r }) Open(r); };
        _tree.ContextMenu = new ContextMenu();
        _tree.ContextMenuOpening += Tree_ContextMenuOpening;
        // フォルダを落とすと一覧に足す（空のときの文言 "Drop repository here to add"。開きはしない。リポジトリでないフォルダは無視する。どちらも推定）
        _tree.AllowDrop = true;
        _tree.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        _tree.Drop += Tree_Drop;
        left.Children.Add(_tree);
        root.Children.Add(left);

        var line = new Border();
        line.SetResourceReference(Border.BackgroundProperty, "Gk.Border");
        Grid.SetColumn(line, 1);
        root.Children.Add(line);
        Grid.SetColumn(_details, 2);
        root.Children.Add(_details);
        Content = root;
        SetResourceReference(BackgroundProperty, "Gk.Manager.Background");

        Loaded += (_, _) => Fill();
        IsVisibleChanged += (_, _) => { if (IsVisible) Fill(); };
    }

    // ------------------------------------------------------------------ 左の木

    private async void Tree_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;   // メインウィンドウの Drop（タブで開く）に渡さない
        if (Main is not { } main || e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        string? added = null;
        foreach (var path in paths.Where(Core.Git.HostDirectory.Exists))
        {
            var git = main.GitFor(path);
            var top = await git.RunAsync(new GitRequest(path, ["rev-parse", "--show-toplevel"]));
            if (!top.Succeeded) continue;
            added = Path.GetFullPath(git.Host.ToLocalPath(top.StandardOutput.Trim()));
            MainViewModel.AddToRepositoryList(added);
        }
        if (added is null) return;
        Fill();
        _tree.SelectedItem = _tree.Items.OfType<ListViewItem>().FirstOrDefault(i => i.Tag is Row { Repository: { } r } && !((Row)i.Tag).Key.StartsWith("recent:", StringComparison.Ordinal)
            && RepositoryList.Normalize(r.Path) == RepositoryList.Normalize(added));
    }

    /// <summary>一覧（repositories.toml。色と名前もここ）。Fill のたびに読み直す。</summary>
    private RepositoryList? _list;

    private void Fill()
    {
        try { _list = RepositoryList.Load(); } catch (IOException) { _list = null; }
        var repos = _list?.Repositories ?? [];
        _root = RepositoryTree.Build(repos, _list?.SourceDirectories ?? []);
        LoadExpanded();
        var selectedKey = ((_tree.SelectedItem as ListViewItem)?.Tag as Row)?.Key;
        _tree.Items.Clear();
        Add(new Row("section:recent", "Recent", 0, true, null, null, true));
        if (!IsCollapsed("section:recent") && _list is not null)
            foreach (var r in _list.Recent(5).Select(p => _list.Get(p)!))
                Add(new Row("recent:" + r.Path, r.Name, 1, false, null, r, false));
        Add(new Row("section:repos", "Repositories", 0, true, null, null, true));
        if (!IsCollapsed("section:repos")) AddChildren(_root, 1);
        var select = _tree.Items.Cast<ListViewItem>().FirstOrDefault(i => (i.Tag as Row)?.Key == selectedKey)
                     ?? _tree.Items.Cast<ListViewItem>().FirstOrDefault(i => (i.Tag as Row)?.Repository is not null);
        _tree.SelectedItem = select;
    }

    /// <summary>フォルダの中身：フォルダを先に、名前順（大文字小文字を区別しない。RepositoryTree が並べてある）。</summary>
    private void AddChildren(RepositoryFolder folder, int level)
    {
        foreach (var f in folder.Folders)
        {
            var key = FolderKey(f);
            Add(new Row(key, f.Name, level, false, f, null, true));
            if (!IsCollapsed(key)) AddChildren(f, level + 1);
        }
        foreach (var r in folder.Repositories)
            Add(new Row("repo:" + r.Path, r.Name, level, false, null, r, false));
    }

    private void Add(Row row)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4 + row.Level * 12, 2, 0, 2) };
        var toggle = new Button { Width = 16, Height = 16, Padding = new Thickness(0), Focusable = false, Visibility = row.Expandable ? Visibility.Visible : Visibility.Hidden };
        toggle.SetResourceReference(StyleProperty, "Gk.FlatButton");
        AutomationProperties.SetAutomationId(toggle, "ExpandCollapseToggleButton");
        toggle.Content = new IconImage { Geometry = (Geometry)FindResource(IsCollapsed(row.Key) ? "Icon.ChevronRight" : "Icon.ChevronDown"), Width = 12, Height = 12 };
        toggle.Click += (_, _) => { Toggle(row.Key); Fill(); };
        panel.Children.Add(toggle);
        if (!row.IsSection)
        {
            var icon = new IconImage { Geometry = (Geometry)FindResource(row.IsFolder ? "Icon.Folder" : "Icon.Repository") };
            // フォルダが無いリポジトリは薄いアイコンの右下に黄色の三角
            if (row.Repository is { } missing && !Core.Git.HostDirectory.Exists(missing.Path))
            {
                icon.Opacity = 0.6;
                var warn = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M4,0 L8,7 L0,7 Z"), Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0xC5, 0x18)),
                    HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, -2, 0),
                };
                panel.Children.Add(new Grid { Margin = new Thickness(2, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Children = { icon, warn } });
            }
            else
            {
                icon.Margin = new Thickness(2, 0, 4, 0);
                icon.VerticalAlignment = VerticalAlignment.Center;
                panel.Children.Add(icon);
            }
        }
        panel.Children.Add(new TextBlock { Text = row.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = row.IsSection ? FontWeights.SemiBold : FontWeights.Normal });
        // 色を付けたリポジトリは行の右端に色の点
        var line = new DockPanel { LastChildFill = true };
        if (row.Repository is { Color: { } color } && TabColorBrushConverter.Brushes.TryGetValue(color, out var brush))
        {
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = brush, Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Right);
            line.Children.Add(dot);
        }
        line.Children.Add(panel);
        _tree.Items.Add(new ListViewItem { Content = line, Tag = row, HorizontalContentAlignment = HorizontalAlignment.Stretch });
    }

    private static Style RowStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Gk.Text")));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetValue(MarginProperty, new Thickness(4, 0, 4, 0));
        bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ListViewItem)) { VisualTree = bd };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Gk.List.Hover"), "Bd"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Gk.Selection"), "Bd"));
        selected.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }

    // ------------------------------------------------------------------ 右クリック

    /// <summary>リポジトリの色。開いているタブがあればタブの色も変える（タブの右クリックと同じ repositories.toml に書く）。</summary>
    private void SetColor(string path, string? color)
    {
        if (Main?.Tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase)) is { } tab) tab.SetTabColor(color);
        else
        {
            try { RepositoryList.Load().SetColor(path, color); }
            catch (IOException ex) { Core.Logging.Log.Warning($"Saving repositories.toml failed: {ex.Message}"); }
        }
        Fill();
    }

    /// <summary>
    /// Rename：木の中で名前をその場で書き換える。Enter で確定、Esc かほかを押すと取り消し。
    /// </summary>
    private void BeginRename(ListViewItem item, RepositoryListEntry repo)
    {
        if (item.Content is not DockPanel line || line.Children.OfType<StackPanel>().FirstOrDefault() is not { } panel
            || panel.Children.OfType<TextBlock>().LastOrDefault() is not { } label) return;
        var index = panel.Children.IndexOf(label);
        var box = new TextBox { Text = repo.Name, MinWidth = 160, Padding = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
        box.SetResourceReference(StyleProperty, "Gk.TextBox");
        var done = false;
        void Finish(bool commit)
        {
            if (done) return;
            done = true;
            var name = box.Text.Trim();
            // alias に書く（repositories.toml の alias = "…"）。開いているタブの名前も変わる
            if (commit && name.Length > 0 && name != repo.Name)
            {
                try { RepositoryList.Load().SetAlias(repo.Path, name); }
                catch (IOException ex) { Core.Logging.Log.Warning($"Saving repositories.toml failed: {ex.Message}"); }
                Main?.Tabs.FirstOrDefault(t => RepositoryList.Normalize(t.Path) == RepositoryList.Normalize(repo.Path))?.RefreshAlias();
            }
            Fill();
        }
        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter) { Finish(true); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Escape) { Finish(false); e.Handled = true; }
        };
        box.LostKeyboardFocus += (_, _) => Finish(false);
        // 差し替えは外してから入れる（添え字への代入は「指定されたインデックスは既に使用されています」になる）
        panel.Children.RemoveAt(index);
        panel.Children.Insert(index, box);
        Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void Tree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if ((e.OriginalSource as DependencyObject)?.FindAncestor<ListViewItem>() is not { Tag: Row row } item) { e.Handled = true; return; }
        _tree.SelectedItem = item;
        var m = new ContextMenu();
        if (row.Repository is { } repo)
        {
            Add(m, "Open", () => Open(repo));
            Add(m, "Open In File Explorer", () => RepositoryActions.Explorer($"\"{repo.Path}\""));
            m.Items.Add(new Separator());
            Add(m, "Rename", () => BeginRename(item, repo));
            Add(m, "Remove...", () => Remove([repo]));
            m.Items.Add(new Separator());
            Add(m, "Rescan Repositories...", Rescan);
            // 最後に色の丸の行（区切り線と名前の無い項目。色の付いた ● で印を付けられる）
            m.Items.Add(new Separator());
            m.Items.Add(MainWindow.ColorRow(color => SetColor(repo.Path, color), m));
        }
        else if (row.Folder is { } folder)
        {
            var repos = folder.AllRepositories.ToList();
            Add(m, $"Open All ({repos.Count})", () => { foreach (var r in repos) Open(r); }).IsEnabled = repos.Count > 0;
            m.Items.Add(new Separator());
            Add(m, "Remove...", () => Remove(repos));
            m.Items.Add(new Separator());
            Add(m, "Rescan Repositories...", Rescan);
        }
        else if (row.Key == "section:repos")
        {
            m.Items.Add(new Separator());
            Add(m, "Rescan Repositories...", Rescan);
        }
        else { e.Handled = true; return; }
        _tree.ContextMenu = m;
    }

    /// <summary>
    /// Remove...（"Do you want to remove 1 selected repositories from GitKnife?" / "The repositories will remain on disk"）。
    /// repositories.toml の表を消してパスを ignore に足す。フォルダは中のリポジトリが無くなれば木から消える。
    /// </summary>
    private void Remove(List<RepositoryListEntry> repos)
    {
        if (!Alerts.Confirm($"Do you want to remove {repos.Count} selected repositories from GitKnife?", "The repositories will remain on disk", "Remove")) return;
        try { RepositoryList.Load().Remove(repos.Select(r => r.Path)); }
        catch (IOException ex) { Core.Logging.Log.Warning($"Saving repositories.toml failed: {ex.Message}"); }
        Fill();
    }

    private void Rescan()
    {
        if (RepositoryList.Load().SourceDirectories.Count == 0)
        {
            Alerts.NotImplemented("Add a source code folder in Preferences > General first");
            return;
        }
        var vm = new ViewModels.Dialogs.RescanRepositoriesDialogViewModel();
        if (DialogWindow.Run(vm, DialogViews.For(vm))) Fill();
    }

    private static MenuItem Add(ItemsControl parent, string header, Action action)
    {
        var mi = new MenuItem { Header = new AccessText { Text = header.Replace("_", "__") } };
        AutomationProperties.SetName(mi, header);
        mi.Click += (_, _) => action();
        parent.Items.Add(mi);
        return mi;
    }

    private void Open(RepositoryListEntry r)
    {
        if (Main is { } main) _ = main.OpenAsync(r.Path);
    }

    // ------------------------------------------------------------------ 右の詳細

    private async Task ShowDetailsAsync()
    {
        if ((_tree.SelectedItem as ListViewItem)?.Tag is not Row { Repository: { } repo }) { _details.Content = null; return; }
        if (!Core.Git.HostDirectory.Exists(repo.Path)) { _details.Content = NotFound(repo); return; }
        var view = new StackPanel { Margin = new Thickness(24, 10, 20, 10) };
        AutomationProperties.SetAutomationId(view, "RepositoryDetailsUserControl");
        _details.Content = new ScrollViewer { Content = view, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var head = new DockPanel();
        var open = new Button { Content = "Open", VerticalAlignment = VerticalAlignment.Center };
        open.SetResourceReference(StyleProperty, "Gk.Button");
        open.Click += (_, _) => Open(repo);
        DockPanel.SetDock(open, Dock.Right);
        head.Children.Add(open);
        var names = new StackPanel();
        names.Children.Add(new TextBlock { Text = repo.Name, FontSize = 21 }.With(t => AutomationProperties.SetAutomationId(t, "RepositoryName")));
        var pathRow = new StackPanel { Orientation = Orientation.Horizontal };
        pathRow.Children.Add(new TextBlock { Text = repo.Path }.With(t => AutomationProperties.SetAutomationId(t, "RepositoryPath")));
        var explorer = new Button { Width = 14, Height = 14, Margin = new Thickness(4, 0, 0, 0), Content = new IconImage { Geometry = (Geometry)FindResource("Icon.OpenIn"), Width = 10, Height = 10 } };
        explorer.SetResourceReference(StyleProperty, "Gk.FlatButton");
        AutomationProperties.SetAutomationId(explorer, "OpeneInExplorerButton");
        explorer.Click += (_, _) => RepositoryActions.Explorer($"\"{repo.Path}\"");
        pathRow.Children.Add(explorer);
        names.Children.Add(pathRow);
        head.Children.Add(names);
        view.Children.Add(head);

        RepositorySummary summary;
        try { summary = await new RepositorySummaryReader(Main!.GitFor(repo.Path), repo.Path).ReadAsync(); }
        catch (Exception ex) { view.Children.Add(new TextBlock { Text = ex.Message }); return; }
        if ((_tree.SelectedItem as ListViewItem)?.Tag is not Row { Repository: { } still } || still != repo) return;
        var now = DateTimeOffset.Now;

        // 数字の枠
        var facts = new Grid { Margin = new Thickness(0, 12, 0, 0) };
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        facts.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        facts.ColumnDefinitions.Add(new ColumnDefinition());
        void Fact(int row, string label, string value, string id)
        {
            facts.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            var l = new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetRow(l, row);
            var v = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(v, id);
            Grid.SetRow(v, row);
            Grid.SetColumn(v, 1);
            facts.Children.Add(l);
            facts.Children.Add(v);
        }
        Fact(0, "Uncommitted Files:", summary.UncommittedFiles.ToString(), "ChangedFilesTextBlock");
        Fact(1, "Commits:", summary.Commits.ToString(), "CommitsTextBlock");
        Fact(2, "Initial Commit:", RepositorySummaryReader.Relative(summary.InitialCommit, now), "InitialCommitDateTextBlock");
        Fact(3, "Last Commit:", RepositorySummaryReader.Relative(summary.LastCommit, now), "LastCommitDateTextBlock");
        var remotes = new StackPanel();
        remotes.Children.Add(new TextBlock { Text = "Remotes:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 4) });
        foreach (var r in summary.Remotes)
        {
            // 名前の後ろに "Issues" と "Pull Requests" のリンク
            // （マウスを乗せても出ない）。GitKnife は Web の URL が作れるリモート（RemoteWebLinks。推定）のときだけ見せる
            var (issues, pulls) = summary.RemoteUrls.TryGetValue(r, out var url) ? RemoteWebLinks.For(url) : (null, null);
            var links = new TextBlock { Margin = new Thickness(10, 0, 0, 0), Opacity = issues is null ? 0 : 1 };
            links.Inlines.Add(WebLink("Issues", issues));
            links.Inlines.Add(new System.Windows.Documents.Run("    "));
            links.Inlines.Add(WebLink("Pull Requests", pulls));
            remotes.Children.Add(Horizontal(new IconImage { Geometry = (Geometry)FindResource("Icon.Remote"), Margin = new Thickness(0, 0, 6, 0) }, new TextBlock { Text = r }, links));
        }
        Grid.SetColumn(remotes, 2);
        Grid.SetRowSpan(remotes, 4);
        facts.Children.Add(remotes);
        view.Children.Add(Card(facts, new Thickness(14, 6, 14, 8)));

        // Summary / Statistics
        var tabs = new TabControl { Margin = new Thickness(0, 8, 0, 0) };
        tabs.SetResourceReference(StyleProperty, "Gk.TabControl");
        var summaryTab = new TabItem { Header = "Summary" };
        summaryTab.SetResourceReference(StyleProperty, "Gk.TabItem");
        var cards = new StackPanel();
        cards.SetValue(System.Windows.Documents.TextElement.FontWeightProperty, FontWeights.Normal);
        if (summary.Branches.Count > 0) cards.Children.Add(ListCard("Branches", summary.Branches, "Icon.Branch", now, subject: true));
        if (summary.Tags.Count > 0) cards.Children.Add(ListCard("Tags", summary.Tags, "Icon.Tag", now, subject: false));
        if (summary.RecentCommits.Count > 0) cards.Children.Add(ListCard("Commits", summary.RecentCommits, "Icon.Commit", now, subject: false));
        if (summary.ReadmeText is { } readme)
        {
            var text = new TextBox { Text = readme, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
            text.SetResourceReference(ForegroundProperty, "Gk.Text");
            text.SetResourceReference(FontFamilyProperty, "Gk.CodeFont");
            AutomationProperties.SetAutomationId(text, "ReadmeTextBox");
            var box = new StackPanel { Children = { new TextBlock { Text = summary.ReadmeName!.ToUpperInvariant(), FontWeight = FontWeights.SemiBold, FontFamily = (FontFamily)FindResource("Gk.CodeFont") }, text } };
            cards.Children.Add(Card(box, new Thickness(24, 14, 24, 14)));
        }
        summaryTab.Content = cards;
        tabs.Items.Add(summaryTab);
        var stats = new TabItem { Header = "Statistics", Content = new TextBlock { Text = "Not implemented yet", Margin = new Thickness(10), FontWeight = FontWeights.Normal } };
        stats.SetResourceReference(StyleProperty, "Gk.TabItem");
        AutomationProperties.SetAutomationId(stats, "StatisticsTabItem");
        tabs.Items.Add(stats);
        view.Children.Add(tabs);
    }

    /// <summary>"Branches" などの枠（アイコン、名前、件名、作者、相対時刻）。</summary>
    private FrameworkElement ListCard(string title, IReadOnlyList<SummaryItem> items, string icon, DateTimeOffset now, bool subject)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(subject ? 200 : title == "Commits" ? 400 : 130) });
        if (subject) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var i = 0; i < items.Count; i++)
        {
            var it = items[i];
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
            var col = 0;
            void Cell(UIElement e) { Grid.SetRow(e, i); Grid.SetColumn(e, col++); grid.Children.Add(e); }
            Cell(new IconImage { Geometry = (Geometry)FindResource(icon), VerticalAlignment = VerticalAlignment.Center });
            Cell(new TextBlock { Text = it.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            if (subject) Cell(new TextBlock { Text = it.Subject, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            Cell(Horizontal(new Avatar { Width = 14, Height = 14, NameText = it.Author, Email = it.Email, Margin = new Thickness(0, 0, 4, 0) }, new TextBlock { Text = it.Author, VerticalAlignment = VerticalAlignment.Center }));
            Cell(new TextBlock { Text = RepositorySummaryReader.Relative(it.Date, now), VerticalAlignment = VerticalAlignment.Center });
        }
        var box = new StackPanel { Children = { new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) }, grid } };
        return Card(box, new Thickness(24, 14, 24, 16));
    }

    private static Border Card(UIElement child, Thickness padding)
    {
        var b = new Border { Child = child, Padding = padding, Margin = new Thickness(0, 0, 0, 8), CornerRadius = new CornerRadius(2) };
        b.SetResourceReference(Border.BackgroundProperty, "Gk.Manager.Card");
        return b;
    }

    /// <summary>Remotes の "Issues" / "Pull Requests"。url が null なら押しても何もしない。</summary>
    private static System.Windows.Documents.Hyperlink WebLink(string text, string? url)
    {
        var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(text)) { TextDecorations = null };
        link.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Gk.Accent");
        link.Click += (_, _) =>
        {
            if (url is null) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { }
        };
        return link;
    }

    private static StackPanel Horizontal(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var c in children) p.Children.Add(c);
        return p;
    }

    /// <summary>フォルダが無いとき（"Repository Not Found" と Remove）。</summary>
    private FrameworkElement NotFound(RepositoryListEntry repo)
    {
        var p = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        p.Children.Add(new TextBlock { Text = "Repository Not Found", FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center });
        var remove = new Button { Content = "Remove", Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        remove.SetResourceReference(StyleProperty, "Gk.Button");
        remove.Click += (_, _) =>
        {
            try { RepositoryList.Load().Remove([repo.Path]); }
            catch (IOException ex) { Core.Logging.Log.Warning($"Saving repositories.toml failed: {ex.Message}"); }
            Fill();
        };
        p.Children.Add(remove);
        return p;
    }
}

using GitKnife.ViewModels.Dialogs;
using GitKnife.Views.Dialogs;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitKnife.Core;
using GitKnife.Core.Settings;
using GitKnife.ViewModels;
using Microsoft.Win32;

namespace GitKnife.Views;

public partial class MainWindow : Window
{
    private MainViewModel Vm => (MainViewModel)DataContext;

    public MainWindow()
    {
        InitializeComponent();
        InputBindings.Add(new KeyBinding(new RelayAction(() => OpenRepository_Click(this, new RoutedEventArgs())), Key.O, ModifierKeys.Control));
        SourceInitialized += (_, _) => RestoreLocation();
        StateChanged += (_, _) => UpdateMaximizeState();
        Closing += (_, _) => SaveLocation();
        // ウィンドウに戻ったら status を読み直す（外部のエディタで変えた分）
        // SSH のリポジトリは見張れないので、参照（向こうでの commit や checkout）も含めて読み直す（T1304。常駐のシェルで 0.2 秒ほど）
        Activated += (_, _) =>
        {
            if (Vm.SelectedTab is not { IsManager: false } tab) return;
            _ = Core.Git.SshPath.IsSshPath(tab.Path) ? tab.RefreshAsync() : tab.RefreshStatusAsync();
        };
        UpdateMaximizeState();
        AddShortcuts();
        Loaded += (_, _) => { ApplyZoom(); InitTabList(); Updater.CheckOnStartup(); };
    }

    // ---------------------------------------------------------- ショートカット

    private void AddShortcuts()
    {
        void Key(Key key, ModifierKeys mods, Action action) => InputBindings.Add(new KeyBinding(new RelayAction(action), key, mods));
        Key(System.Windows.Input.Key.G, ModifierKeys.Control | ModifierKeys.Shift, () => { if (OpenTab is { } t) RepositoryActions.NewTag(t); });
        Key(System.Windows.Input.Key.B, ModifierKeys.Control, () => { if (OpenTab is { } t) RepositoryActions.QuickCheckout(t); });
        Key(System.Windows.Input.Key.B, ModifierKeys.Control | ModifierKeys.Shift, () => { if (OpenTab is { } t) RepositoryActions.NewBranch(t); });
        // Ctrl+0 / Ctrl+1 は日本語 IME が有効だと ImeProcessed で届き KeyBinding に当たらないので、PreviewKeyDown で見る
        PreviewKeyDown += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            var key = e.Key == System.Windows.Input.Key.ImeProcessed ? e.ImeProcessedKey : e.Key;
            if (key is System.Windows.Input.Key.D0 or System.Windows.Input.Key.NumPad0) { ShowHead_Click(this, new RoutedEventArgs()); e.Handled = true; }
            else if (key is System.Windows.Input.Key.D1 or System.Windows.Input.Key.NumPad1) { FocusCommitSubject(); e.Handled = true; }
        };
        Key(System.Windows.Input.Key.P, ModifierKeys.Control, () => QuickLaunch_Click(this, new RoutedEventArgs()));
        Key(System.Windows.Input.Key.OemComma, ModifierKeys.Control, () => Preferences_Click(this, new RoutedEventArgs()));
        Key(System.Windows.Input.Key.D, ModifierKeys.Control | ModifierKeys.Shift, () => ToggleDetails_Click(this, new RoutedEventArgs()));
        Key(System.Windows.Input.Key.O, ModifierKeys.Control | ModifierKeys.Alt, () => OpenInExplorer_Click(this, new RoutedEventArgs()));
        Key(System.Windows.Input.Key.C, ModifierKeys.Control | ModifierKeys.Alt, () => OpenInConsole_Click(this, new RoutedEventArgs()));
        Key(System.Windows.Input.Key.OemPlus, ModifierKeys.Control, () => Zoom(+10));
        Key(System.Windows.Input.Key.OemMinus, ModifierKeys.Control, () => Zoom(-10));
        Key(System.Windows.Input.Key.Add, ModifierKeys.Control, () => Zoom(+10));
        Key(System.Windows.Input.Key.A, ModifierKeys.Control | ModifierKeys.Shift, () => _ = Vm.SelectedTab?.ToggleActiveBranchFilterAsync());   // Filter by Active Branch
        Key(System.Windows.Input.Key.Subtract, ModifierKeys.Control, () => Zoom(-10));
    }

    /// <summary>Ctrl+1：Local Changes に切り替えてコミットの件名欄へ。</summary>
    private void FocusCommitSubject()
    {
        if (OpenTab is not { } t) return;
        t.View = ViewModels.RepositoryView.LocalChanges;
        t.LocalChanges.RequestFocusSubject();
    }

    private void ShowHead_Click(object sender, RoutedEventArgs e)
    {
        if (OpenTab is not { } t) return;
        t.View = ViewModels.RepositoryView.AllCommits;
        t.SelectedCommit = t.Commits.FirstOrDefault(c => c.IsHead) ?? t.SelectedCommit;
    }

    private void ToggleDetails_Click(object sender, RoutedEventArgs e) => ToggleDetails();

    /// <summary>Hide Commit Details（Ctrl+Shift+D、詳細ペインの CollapseDetailsButton）。全タブ共通。</summary>
    public void ToggleDetails()
    {
        App.Settings.RevisionDetailsPaneCollapsed = !App.Settings.RevisionDetailsPaneCollapsed;
        foreach (var t in Vm.Tabs) t.NotifyDetailsCollapsedChanged();
    }

    /// <summary>File > Preferences...（Ctrl+,）。閉じたら設定を保存し、開いているタブに反映する。</summary>
    private async void Preferences_Click(object sender, RoutedEventArgs e)
    {
        var vm = new ViewModels.Dialogs.PreferencesViewModel(App.Settings, Vm.Git);
        Dialogs.DialogWindow.Run(vm, Dialogs.DialogViews.For(vm));
        vm.CustomCommands.Save();
        try { await vm.SaveGitAsync(); }
        catch (Core.Git.GitException ex) { Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
        App.SaveSettings();
        App.RaiseSettingsChanged();
        foreach (var t in Vm.Tabs.Where(t => t.References is not null)) await t.RefreshAsync();
    }

    /// <summary>Quick Launch（Ctrl+P、ツールバーの Quick Launch）。</summary>
    private void QuickLaunch_Click(object sender, RoutedEventArgs e)
        => QuickLaunchCommands.Show(this, Vm, Zoom, () => OpenRepository_Click(this, new RoutedEventArgs()),
            () => OpenInExplorer_Click(this, new RoutedEventArgs()), () => OpenInConsole_Click(this, new RoutedEventArgs()));

    /// <summary>Activity Manager を開く（今のタブのジョブ）。開いている間はボタンを押せない。</summary>
    private void ShowActivity_Click(object sender, RoutedEventArgs e)
    {
        ActivityView.Attach(Vm.SelectedTab);
        ActivityPopup.IsOpen = true;
        ActivityButton.IsEnabled = false;
    }

    /// <summary>ステータス欄の題名を押しても Activity Manager（公開リリースノート「タイトルクリックで Activity Manager」）。</summary>
    private void StatusTitle_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (ActivityButton.IsEnabled) ShowActivity_Click(sender, e);
    }

    private void ActivityPopup_Closed(object? sender, EventArgs e) => ActivityButton.IsEnabled = true;

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(+10);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(-10);

    /// <summary>UI の拡大・縮小（設定 LayoutScaling。% で 80〜200）。</summary>
    private void Zoom(int delta)
    {
        App.Settings.LayoutScaling = Math.Clamp(App.Settings.LayoutScaling + delta, 80, 200);
        ApplyZoom();
    }

    private void ApplyZoom()
    {
        var s = App.Settings.LayoutScaling / 100.0;
        RootGrid.LayoutTransform = Math.Abs(s - 1) < 0.001 ? Transform.Identity : new ScaleTransform(s, s);
    }

    // ---------------------------------------------------------- Git 操作のダイアログ（T701）

    private RepositoryViewModel? OpenTab => Vm.SelectedTab is { References: not null } tab ? tab : null;

    private void CancelJob_Click(object sender, RoutedEventArgs e) => OpenTab?.RunningJob?.Cancel();

    // Alt を押しながらならダイアログなしで実行する（QuickFetch / QuickPull / QuickPush）
    private static bool AltDown => (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
    private void Fetch_Click(object sender, RoutedEventArgs e) { if (OpenTab is { } t) { if (AltDown) _ = RepositoryActions.QuickFetch(t); else RepositoryActions.Fetch(t); } }
    private void Pull_Click(object sender, RoutedEventArgs e) { if (OpenTab is { } t) { if (AltDown) _ = RepositoryActions.QuickPull(t); else RepositoryActions.Pull(t); } }
    private void Push_Click(object sender, RoutedEventArgs e) { if (OpenTab is { } t) { if (AltDown) _ = RepositoryActions.PushNow(t); else RepositoryActions.Push(t); } }
    private void RepositorySettings_Click(object sender, RoutedEventArgs e) { if (OpenTab is { } t) RepositoryActions.RepositorySettings(t); }
    private void SaveStash_Click(object sender, RoutedEventArgs e) { if (OpenTab is { } t) RepositoryActions.SaveStash(t); }
    private void NewBranch_Click(object sender, RoutedEventArgs e) { if (OpenTab is { } t) RepositoryActions.NewBranch(t); }
    private void NewTag_Click(object sender, RoutedEventArgs e) { if (OpenTab is { } t) RepositoryActions.NewTag(t); }

    // ---------------------------------------------------------- ウィンドウの位置（A01、MainWindowLocationState）

    private void RestoreLocation()
    {
        var s = App.Settings.MainWindowLocationState;
        if (s.Width < 400 || s.Height < 300) return;
        var rect = new Rect(s.Left, s.Top, s.Width, s.Height);
        // どのモニタにも入らない位置なら使わない（起動時の可視モニタ確認）
        if (!rect.IntersectsWith(new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight))) return;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = s.Left; Top = s.Top; Width = s.Width; Height = s.Height;
        if (s.WindowState == (int)WindowState.Maximized) WindowState = WindowState.Maximized;
    }

    private void SaveLocation()
    {
        var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        App.Settings.MainWindowLocationState = new WindowLocationState
        {
            Left = (int)b.Left, Top = (int)b.Top, Width = (int)b.Width, Height = (int)b.Height,
            WindowState = WindowState == WindowState.Minimized ? (int)WindowState.Normal : (int)WindowState,
        };
    }

    private void UpdateMaximizeState()
    {
        var max = WindowState == WindowState.Maximized;
        MaximizeButton.Visibility = max ? Visibility.Collapsed : Visibility.Visible;
        RestoreButton.Visibility = max ? Visibility.Visible : Visibility.Collapsed;
        // 最大化すると枠のぶん画面外にはみ出すので内側に余白を入れる
        RootGrid.Margin = max ? new Thickness(7) : new Thickness(0);
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeRestore_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
    private void CheckForUpdates_Click(object sender, RoutedEventArgs e) => _ = Updater.CheckAsync(interactive: true);

    // ---------------------------------------------------------- タブ

    private void TabItem_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 中クリックで閉じる（A03）
        if (e.ChangedButton == MouseButton.Middle && sender is TabItem { DataContext: RepositoryViewModel tab })
        {
            Vm.CloseTabCommand.Execute(tab);
            e.Handled = true;
        }
        else if (e.ChangedButton == MouseButton.Left && sender is TabItem { DataContext: RepositoryViewModel { IsRenaming: false } t })
        {
            _dragTab = t;
            _dragStart = e.GetPosition(Tabs);
        }
    }

    // タブのドラッグで並べ替える（#4）。少し動かしてから始め、カーソルの下のタブの位置に入れる
    private RepositoryViewModel? _dragTab;
    private Point _dragStart;
    private bool _draggingTab;

    private void Tabs_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragTab is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { EndTabDrag(); return; }
        var p = e.GetPosition(Tabs);
        if (!_draggingTab)
        {
            if (Math.Abs(p.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance) return;
            _draggingTab = true;
            Tabs.CaptureMouse();
        }
        for (var i = 0; i < Vm.Tabs.Count; i++)
        {
            if (Tabs.ItemContainerGenerator.ContainerFromIndex(i) is not TabItem item) continue;
            var left = item.TranslatePoint(new Point(0, 0), Tabs).X;
            if (p.X >= left && p.X < left + item.ActualWidth || i == 0 && p.X < left || i == Vm.Tabs.Count - 1 && p.X >= left)
            {
                Vm.MoveTab(_dragTab, i);
                break;
            }
        }
        e.Handled = true;
    }

    private void Tabs_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingTab) e.Handled = true;
        EndTabDrag();
    }

    private void Tabs_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (_draggingTab && !Tabs.IsMouseCaptured) EndTabDrag();
    }

    private void EndTabDrag()
    {
        _dragTab = null;
        if (!_draggingTab) return;
        _draggingTab = false;
        if (Tabs.IsMouseCaptured) Tabs.ReleaseMouseCapture();
    }

    /// <summary>
    /// タブの右クリック：Close All、Close All But This、---、Open In Console、Open In File Explorer、---、
    /// Rename、---、Workspaces >（移す）、---、色の行（6 色の丸と、色を消す ⊗）。
    /// </summary>
    private void TabItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not TabItem { DataContext: RepositoryViewModel tab } item) return;
        item.ContextMenu = BuildTabMenu(tab);
    }

    /// <summary>タブの右クリック。縦タブの行でも同じ。</summary>
    private ContextMenu BuildTabMenu(RepositoryViewModel tab)
    {
        var e = new RoutedEventArgs();
        var m = new ContextMenu();
        MenuItem Add(ItemsControl parent, string header, Action action)
        {
            var mi = new MenuItem { Header = new AccessText { Text = header.Replace("_", "__") } };
            System.Windows.Automation.AutomationProperties.SetName(mi, header);
            mi.Click += (_, _) => action();
            parent.Items.Add(mi);
            return mi;
        }
        Add(m, "Close All", () => Vm.CloseTabs(_ => true));
        Add(m, "Close All But This", () => Vm.CloseTabs(t => t != tab));
        m.Items.Add(new Separator());
        Add(m, "Open In Console", () => { Vm.SelectedTab = tab; OpenInConsole_Click(this, e); }).IsEnabled = !tab.IsManager;
        Add(m, "Open In File Explorer", () => { Vm.SelectedTab = tab; OpenInExplorer_Click(this, e); }).IsEnabled = !tab.IsManager;
        m.Items.Add(new Separator());
        Add(m, "Rename", () => tab.BeginRename()).IsEnabled = !tab.IsManager;
        m.Items.Add(new Separator());
        var ws = new MenuItem { Header = "Workspaces", IsEnabled = !tab.IsManager };
        ToolbarMenus.AddWorkspaces(ws, Vm, n => Vm.MoveTabToWorkspace(tab, n));
        m.Items.Add(ws);
        if (!tab.IsManager)
        {
            m.Items.Add(new Separator());
            m.Items.Add(TabColorRow(tab, m));
        }
        return m;
    }

    /// <summary>タブの色の行：Red / Orange / Yellow / Green / Blue / Violet の丸と、色を消す ⊗（名前の無い 1 つの項目）。</summary>
    private static MenuItem TabColorRow(RepositoryViewModel tab, ContextMenu menu) => ColorRow(tab.SetTabColor, menu);

    /// <summary>色の丸の行（タブの右クリックと Repository Manager のリポジトリの右クリックで共通。set に色の名前か null を渡す）。</summary>
    internal static MenuItem ColorRow(Action<string?> set, ContextMenu menu)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-2, 0, 0, 0) };
        Button Dot(UIElement content, string tip, Action click)
        {
            var b = new Button { Content = content, Width = 19, Height = 18, Padding = new Thickness(0), ToolTip = tip, Focusable = false };
            System.Windows.Automation.AutomationProperties.SetName(b, tip);
            b.SetResourceReference(StyleProperty, "Gk.FlatButton");
            b.Click += (_, _) => { click(); menu.IsOpen = false; };
            return b;
        }
        foreach (var name in Core.Settings.RepositoryList.Colors)
        {
            var n = name;
            row.Children.Add(Dot(new System.Windows.Shapes.Ellipse { Width = 10, Height = 10, Fill = TabColorBrushConverter.Brushes[n] }, n, () => set(n)));
        }
        var clear = new Grid { Width = 11, Height = 11 };
        var ring = new System.Windows.Shapes.Ellipse { StrokeThickness = 1 };
        ring.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Gk.Text.Dim");
        clear.Children.Add(ring);
        var x = new System.Windows.Shapes.Path { Data = System.Windows.Media.Geometry.Parse("M3.5,3.5 L7.5,7.5 M7.5,3.5 L3.5,7.5"), StrokeThickness = 1 };
        x.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Gk.Text.Dim");
        clear.Children.Add(x);
        row.Children.Add(Dot(clear, "No Color", () => set(null)));
        return new MenuItem { Header = row, StaysOpenOnClick = true };
    }

    /// <summary>真ん中の欄の中身が変わったら下から上へずらして出す。</summary>
    private void StatusContent_Changed(object? sender, System.Windows.Data.DataTransferEventArgs e)
    {
        if (StatusContent.RenderTransform is not System.Windows.Media.TranslateTransform t) return;
        t.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new System.Windows.Media.Animation.DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } });
    }

    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox { IsVisible: true } box) { box.Focus(); box.SelectAll(); }
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: RepositoryViewModel tab }) return;
        if (e.Key == System.Windows.Input.Key.Enter) { tab.EndRename(commit: true); e.Handled = true; }
        else if (e.Key == System.Windows.Input.Key.Escape) { tab.EndRename(commit: false); e.Handled = true; }
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is TextBox { DataContext: RepositoryViewModel { IsRenaming: true } tab }) tab.EndRename(commit: true);
    }

    // ---------------------------------------------------------- メニュー

    /// <summary>File > Open Repository...：場所（この PC、WSL、SSH）を選んで開く（A23）。</summary>
    private async void OpenRepository_Click(object sender, RoutedEventArgs e)
    {
        var vm = new ViewModels.Dialogs.OpenRepositoryDialogViewModel(Vm.Settings, Vm.GitFor, SourceFolder());
        if (!Dialogs.DialogWindow.Run(vm, Dialogs.DialogViews.For(vm)) || vm.OpenedPath is not { } path) return;
        if (vm.IsRepository) await Vm.OpenAsync(path);
        else await OfferInitAsync(path);
    }

    /// <summary>リポジトリでないフォルダを開こうとしたとき、init するか聞く（文言は推定）。</summary>
    private async Task OfferInitAsync(string path)
    {
        if (!Alerts.Confirm("Init New Repository", $"'{path}' is not a git repository. Do you want to initialize a new repository here?", "Init")) return;
        var r = await Vm.GitFor(path).RunAsync(new Core.Git.GitRequest(path, ["init"]));
        if (!r.Succeeded) { Alerts.ShowGitError(r.CommandLine, r.StandardError); return; }
        await Vm.OpenAsync(path);
    }

    private async void InitRepository_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select repository", InitialDirectory = SourceFolder() };
        if (dlg.ShowDialog(this) != true) return;
        var r = await Vm.GitFor(dlg.FolderName).RunAsync(new Core.Git.GitRequest(dlg.FolderName, ["init"]));
        if (!r.Succeeded) { Alerts.ShowGitError(r.CommandLine, r.StandardError); return; }
        await Vm.OpenAsync(dlg.FolderName);
    }

    /// <summary>フォルダ選択の最初の場所：Preferences の Source Code Folder の先頭（Init New Repository と Open Repository）。</summary>
    private static string SourceFolder() => Core.Settings.RepositoryList.Load().SourceDirectories.FirstOrDefault(System.IO.Directory.Exists) ?? "";

    private void OpenInExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedTab is { } tab) RepositoryActions.Explorer($"\"{tab.Path}\"");
    }

    // ---------------------------------------------------------- View の表示オプション

    private void ViewMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        var tab = Vm.SelectedTab is { IsManager: false } t ? t : null;
        // Repository Manager のタブでも無効にしない（o02。押しても何もしない）
        foreach (var item in new[] { HideTagsMenuItem, HideStashesMenuItem, ReflogMenuItem, CollapseMergesMenuItem, ActiveBranchFilterMenuItem }) item.IsEnabled = Vm.SelectedTab is not null;
        HideTagsMenuItem.IsChecked = tab?.HideTags == true;
        HideStashesMenuItem.IsChecked = tab?.HideStashes == true;
        ReflogMenuItem.IsChecked = tab?.ShowReflog == true;
        CollapseMergesMenuItem.IsChecked = tab?.CollapseAllMerges == true;
        ActiveBranchFilterMenuItem.Header = tab?.ActiveBranchFilterHeader ?? "Filter by Active Branch";
    }

    private void NewWorktree_Click(object sender, RoutedEventArgs e) { if (OpenTab is { } t) RepositoryActions.NewWorktree(t); }
    /// <summary>Repository > Bisect（Quick Launch の "Bisect" と同じ `bisect start`）。</summary>
    private void Bisect_Click(object sender, RoutedEventArgs e) { if (OpenTab is { } t) RepositoryActions.RunDirect(t, "Bisect", ["bisect", "start"]); }

    private void HideTags_Click(object sender, RoutedEventArgs e) => _ = Vm.SelectedTab?.ToggleHideTagsAsync();
    private void HideStashes_Click(object sender, RoutedEventArgs e) => _ = Vm.SelectedTab?.ToggleHideStashesAsync();
    private void Reflog_Click(object sender, RoutedEventArgs e) => _ = Vm.SelectedTab?.ToggleReflogAsync();
    private void CollapseMerges_Click(object sender, RoutedEventArgs e) => _ = Vm.SelectedTab?.ToggleCollapseAllMergesAsync();
    private void ActiveBranchFilter_Click(object sender, RoutedEventArgs e) => _ = Vm.SelectedTab?.ToggleActiveBranchFilterAsync();

    private void OpenInConsole_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.SelectedTab is not { } tab) return;
        // Windows Terminal があればそれ、なければ cmd（N01。ターミナルの選択は Preferences > Integration で後から）。WSL はその WSL のシェル
        RepositoryActions.OpenConsole(tab.Path);
    }

    // ---------------------------------------------------------- ドラッグ＆ドロップで開く（B03。Repository Manager は "Drop repository here to add"）

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>フォルダを落とすとタブで開く。リポジトリでなければ init するか聞く（文言は推定）。</summary>
    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths.Where(System.IO.Directory.Exists))
        {
            var git = Vm.GitFor(path);
            var top = await git.RunAsync(new Core.Git.GitRequest(path, ["rev-parse", "--show-toplevel"]));
            if (top.Succeeded) await Vm.OpenAsync(System.IO.Path.GetFullPath(git.Host.ToLocalPath(top.StandardOutput.Trim())));
            else await OfferInitAsync(path);
        }
    }

    public void ApplyPatchFromQuickLaunch() => ApplyPatch_Click(this, new RoutedEventArgs());

    /// <summary>Repository > Apply Patch…（ファイル選択 "Select patch" → Apply Patch ダイアログ）。</summary>
    private void ApplyPatch_Click(object sender, RoutedEventArgs e)
    {
        if (OpenTab is not { } t) return;
        var dlg = new OpenFileDialog { Title = "Select patch", Filter = "Git Patch (*.*)|*.*" };
        if (t.Settings.RecentPatchDirectory is { } d && System.IO.Directory.Exists(d)) dlg.InitialDirectory = d;
        if (dlg.ShowDialog(this) != true) return;
        t.Settings.RecentPatchDirectory = System.IO.Path.GetDirectoryName(dlg.FileName);
        RepositoryActions.Run(t, new ViewModels.Dialogs.ApplyPatchDialogViewModel(t, dlg.FileName));
    }

    /// <summary>
    /// Repository > Git LFS。初期化前は "Initialize Git LFS"（`lfs install --local`）、初期化後は
    /// Add Track Pattern...、---、Status (Locks)...、---、Fetch...、Pull...、---、Prune（`lfs prune --verbose`）、---、Deinitialize Git LFS（`lfs uninstall --local`）。
    /// 初期化したかは .git/config の [filter "lfs"] で見る。
    /// </summary>
    private void LfsMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != LfsMenu) return;
        LfsMenu.Items.Clear();
        var t = OpenTab;
        MenuItem Item(string header, Action? action)
        {
            var mi = new MenuItem { Header = header, IsEnabled = Vm.SelectedTab is not null };   // Repository Manager のタブでも無効にしない（o02）
            mi.Click += (_, _) => { if (t is null) return; if (action is null) Alerts.NotImplemented(header); else action(); };
            LfsMenu.Items.Add(mi);
            return mi;
        }
        if (t is null || !IsLfsInitialized(t))
        {
            Item("Initialize Git LFS", () => RepositoryActions.RunDirect(t!, "Initialize Git LFS", ["lfs", "install", "--local"]));
            return;
        }
        Item("Add Track Pattern...", () => RepositoryActions.Run(t, new ViewModels.Dialogs.LfsTrackDialogViewModel(t)));
        LfsMenu.Items.Add(new Separator());
        Item("Status (Locks)...", () => Dialogs.DialogWindow.Run(new ViewModels.Dialogs.LfsStatusDialogViewModel(t), out _));
        LfsMenu.Items.Add(new Separator());
        Item("Fetch...", () => RepositoryActions.Run(t, new ViewModels.Dialogs.LfsRemoteDialogViewModel(t, pull: false)));
        Item("Pull...", () => RepositoryActions.Run(t, new ViewModels.Dialogs.LfsRemoteDialogViewModel(t, pull: true)));
        LfsMenu.Items.Add(new Separator());
        Item("Prune", () => RepositoryActions.RunDirect(t, "Prune", ["lfs", "prune", "--verbose"]));
        LfsMenu.Items.Add(new Separator());
        Item("Deinitialize Git LFS", () => RepositoryActions.RunDirect(t, "Deinitialize Git LFS", ["lfs", "uninstall", "--local"]));
    }

    /// <summary>
    /// Repository > Git Flow。未初期化は "Initialize Git Flow..." だけ、
    /// 初期化済み（gitflow.branch.master / develop がある）は Start Feature / Release / Hotfix と Deinitialize Git Flow。
    /// </summary>
    private void GitFlowMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != GitFlowMenu) return;
        GitFlowMenu.Items.Clear();
        var t = OpenTab;
        void Item(string header, Action<ViewModels.RepositoryViewModel> action)
        {
            var mi = new MenuItem { Header = header, IsEnabled = Vm.SelectedTab is not null };
            mi.Click += (_, _) => { if (t is not null) action(t); };
            GitFlowMenu.Items.Add(mi);
        }
        if (t?.References?.GitFlow is not { } flow)
        {
            Item("Initialize Git Flow...", r => RepositoryActions.Run(r, new ViewModels.Dialogs.GitFlowInitDialogViewModel(r)));
            return;
        }
        Item("Start Feature...", r => RepositoryActions.GitFlowStart(r, flow, Core.Git.Model.GitFlowKind.Feature));
        Item("Start Release...", r => RepositoryActions.GitFlowStart(r, flow, Core.Git.Model.GitFlowKind.Release));
        Item("Start Hotfix...", r => RepositoryActions.GitFlowStart(r, flow, Core.Git.Model.GitFlowKind.Hotfix));
        GitFlowMenu.Items.Add(new Separator());
        Item("Deinitialize Git Flow", r => RepositoryActions.RunDirect(r, "Deinitialize Git Flow", Core.Git.Commands.GitArgs.GitFlowDeinit()));
    }

    private static bool IsLfsInitialized(ViewModels.RepositoryViewModel repo)
    {
        try
        {
            var config = System.IO.Path.Combine(repo.CommonGitDirectory ?? System.IO.Path.Combine(repo.Path, ".git"), "config");
            return System.IO.File.ReadLines(config).Any(l => l.Trim().Equals("[filter \"lfs\"]", StringComparison.OrdinalIgnoreCase));
        }
        catch (System.IO.IOException) { return false; }
    }

    /// <summary>File > Clone...。親フォルダの既定は最初のソースフォルダ（無ければユーザーのフォルダ）。終わったら新しいタブで開く。</summary>
    private async void Clone_Click(object sender, RoutedEventArgs e)
    {
        var parent = Core.Settings.RepositoryList.Load().SourceDirectories.FirstOrDefault(System.IO.Directory.Exists)
                     ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var vm = new ViewModels.Dialogs.CloneDialogViewModel(Vm.GitFor, parent);
        if (Dialogs.DialogWindow.Run(vm, Dialogs.DialogViews.For(vm))) await Vm.OpenAsync(vm.TargetPath);
    }

    // ---------------------------------------------------------- ツールバーのドロップダウン（ToolbarMenus）

    private void StashDropdown_Click(object sender, RoutedEventArgs e) => ToolbarMenus.Show(StashButton, ToolbarMenus.Stash(OpenTab));
    private void BranchDropdown_Click(object sender, RoutedEventArgs e) => ToolbarMenus.Show(BranchButton, ToolbarMenus.Branch(OpenTab));
    private void OpenInDropdown_Click(object sender, RoutedEventArgs e)
        => ToolbarMenus.Show((FrameworkElement)sender, ToolbarMenus.OpenIn(OpenTab, () => OpenInConsole_Click(this, e), () => OpenInExplorer_Click(this, e)));
    private void AppearanceDropdown_Click(object sender, RoutedEventArgs e) => ToolbarMenus.Show((FrameworkElement)sender, ToolbarMenus.Appearance(o =>
    {
        App.Settings.RevisionListOrientation = o;
        foreach (var t in Vm.Tabs) t.NotifyLayoutChanged();
    }));
    private void WorkspacesDropdown_Click(object sender, RoutedEventArgs e) => ToolbarMenus.Show((FrameworkElement)sender, ToolbarMenus.Workspaces(Vm));

    private void SwitchTheme_Click(object sender, RoutedEventArgs e) => ThemeManager.Toggle(App.Settings);

    /// <summary>Help > About GitKnife（AboutDialogViewModel）。</summary>
    private async void About_Click(object sender, RoutedEventArgs e)
    {
        string version;
        try { version = await Vm.Git.Git.GetVersionAsync(); } catch { version = "?"; }
        Dialogs.DialogWindow.Run(new ViewModels.Dialogs.AboutDialogViewModel(Vm.Git.Git.Path, version), out _);
    }
}

/// <summary>InputBinding 用の小さな ICommand。</summary>
public sealed class RelayAction(Action action) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => action();
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitKnife.Core.Git;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels;

/// <summary>メインウィンドウ。タブ＝リポジトリの一覧と、選択中のタブ。</summary>
public sealed partial class MainViewModel : ObservableObject
{
    public AppSettings Settings { get; }
    public GitProcess Git { get; }

    private readonly Dictionary<GitHost, GitProcess> _hostGits = [];

    /// <summary>そのパスの接続先の git（T1301。WSL のパスはその WSL の中の git、ほかは <see cref="Git"/>）。</summary>
    public GitProcess GitFor(string path)
    {
        var host = GitHost.FromPath(path);
        if (host == Git.Host) return Git;
        lock (_hostGits)
        {
            if (!_hostGits.TryGetValue(host, out var git)) _hostGits[host] = git = Git.WithHost(host);
            return git;
        }
    }
    public ObservableCollection<RepositoryViewModel> Tabs { get; } = [];
    private readonly Stack<string> _closedTabs = new();

    [ObservableProperty] private RepositoryViewModel? _selectedTab;

    /// <summary>ツールバーの Workspaces の文字（設定は変更を通知しないので VM から通知する）。</summary>
    public string ActiveWorkspace => Settings.Workspaces.ActiveWorkspace;

    public string Title => Settings.Workspaces.ShowInTitle ? Settings.Workspaces.ActiveWorkspace : Core.ProductInfo.Name;

    public MainViewModel(AppSettings settings, GitProcess git)
    {
        Settings = settings;
        Tabs.CollectionChanged += (_, e) =>
        {
            // 名前は読み込みの後や Rename で変わるので、そのたびに付け直す
            foreach (var t in e.NewItems?.OfType<RepositoryViewModel>() ?? [])
                t.PropertyChanged += (_, pe) => { if (pe.PropertyName == nameof(RepositoryViewModel.Name)) UpdateTabSuffixes(); };
            // 読み込む前に閉じたタブは読まない
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) _unloaded.Clear();
            foreach (var t in e.OldItems?.OfType<RepositoryViewModel>() ?? []) _unloaded.Remove(t);
            UpdateTabSuffixes();
        };
        // ダイアログの操作の中で動いた git を Activity Manager のジョブに書く
        git.Completed += r => ActivityJob.Current?.Append(r);
        Git = git;
        // 自動 fetch（10 分おきに `fetch --prune <remote> --progress`。設定 FetchRemotesAutomatically）
        _autoFetch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
        _autoFetch.Tick += async (_, _) => await AutoFetchAsync();
        _autoFetch.Start();
        // 状態の定期更新（設定 AutomaticStatusUpdateInterval 秒。0 なら止める。ファイルの監視で拾えない変更の取りこぼし対策。
        // 単位が秒で、選んでいるタブだけを読むのは推定）
        _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(Math.Max(5, settings.AutomaticStatusUpdateInterval)) };
        _statusTimer.Tick += async (_, _) =>
        {
            if (Settings.AutomaticStatusUpdateInterval <= 0 || System.Windows.Application.Current?.MainWindow?.IsActive != true) return;
            if (SelectedTab is { IsManager: false } tab) await tab.RefreshStatusAsync();
        };
        _statusTimer.Start();
    }

    private readonly System.Windows.Threading.DispatcherTimer _statusTimer;

    public async Task OpenAsync(string path)
    {
        var existing = Tabs.FirstOrDefault(t => string.Equals(t.Path, path, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) { SelectedTab = existing; return; }
        var vm = new RepositoryViewModel(GitFor(path), Settings, path);
        Tabs.Add(vm);
        SelectedTab = vm;
        await vm.RefreshAsync();
        RememberWorkspace();
        RememberRepository(path);
    }

    /// <summary>
    /// 開いたリポジトリを Repository Manager の一覧（repositories.toml）に入れ、opened を今にする（Quick Launch の Recent Repositories はこの新しい順）。
    /// settings.json の RepositoryManager は使わない（T110）。
    /// </summary>
    private static void RememberRepository(string path)
    {
        try { Core.Settings.RepositoryList.Load().Touch(path); }
        catch (System.IO.IOException ex) { Core.Logging.Log.Warning($"Saving repositories.toml failed: {ex.Message}"); }
    }

    /// <summary>Repository Manager の一覧に入れる（既にあれば何もしない）。</summary>
    public static void AddToRepositoryList(string path)
    {
        try { Core.Settings.RepositoryList.Load().Add(path); }
        catch (System.IO.IOException ex) { Core.Logging.Log.Warning($"Saving repositories.toml failed: {ex.Message}"); }
    }

    private readonly System.Windows.Threading.DispatcherTimer _autoFetch;

    /// <summary>開いている全リポジトリのリモートを fetch する（Activity Manager の Background に出す）。</summary>
    private async Task AutoFetchAsync()
    {
        if (!Settings.FetchRemotesAutomatically) return;
        foreach (var tab in Tabs.Where(t => !t.IsManager && t.References is not null).ToList())
            await tab.AutoFetchAsync();
    }

    /// <summary>ワークスペースの名前や「題名に出す」を変えたとき（ツールバーの Workspaces の文字と窓の題名）。</summary>
    public void NotifyWorkspaceChanged()
    {
        OnPropertyChanged(nameof(ActiveWorkspace));
        OnPropertyChanged(nameof(Title));
    }

    /// <summary>タブの右クリック > Close All / Close All But This（Repository Manager のタブも閉じる）。</summary>
    public void CloseTabs(Func<RepositoryViewModel, bool> which)
    {
        foreach (var t in Tabs.Where(which).ToList()) CloseTabCommand.Execute(t);
    }

    /// <summary>タブの右クリック > Workspaces > 名前：そのタブを別のワークスペースへ移す（今のワークスペースからは閉じる）。</summary>
    public void MoveTabToWorkspace(RepositoryViewModel tab, string workspace)
    {
        if (tab.IsManager || workspace == Settings.Workspaces.ActiveWorkspace) return;
        var ws = Settings.Workspaces.All.FirstOrDefault(w => w.Name == workspace);
        if (ws is null) { ws = new Workspace { Name = workspace }; Settings.Workspaces.All.Add(ws); }
        if (!ws.Repositories.Contains(tab.Path, StringComparer.OrdinalIgnoreCase)) ws.Repositories.Add(tab.Path);
        CloseTabCommand.Execute(tab);
        App.SaveSettings();
    }

    /// <summary>
    /// ワークスペースを切り替える（ツールバーの Workspaces）。今のタブを今のワークスペースに覚え、閉じて、切り替え先のタブを開く。
    /// </summary>
    public async Task SwitchWorkspaceAsync(string name)
    {
        if (name == Settings.Workspaces.ActiveWorkspace) return;
        RememberWorkspace();
        // タブを閉じる間に今のワークスペースのタブ一覧を書き換えないように
        _switchingWorkspace = true;
        foreach (var t in Tabs.Where(t => !t.IsManager).ToList()) Tabs.Remove(t);
        Settings.Workspaces.ActiveWorkspace = name;
        _switchingWorkspace = false;
        NotifyWorkspaceChanged();
        await OpenActiveWorkspaceTabsAsync();
        App.SaveSettings();
    }

    private Task OpenActiveWorkspaceTabsAsync()
    {
        var ws = Settings.Workspaces.All.FirstOrDefault(w => w.Name == Settings.Workspaces.ActiveWorkspace);
        if (ws is null) return Task.CompletedTask;
        return RestoreTabsAsync(ws.Repositories.Where(CanRestore).ToList(), ws.ActiveRepository);
    }

    /// <summary>
    /// 復元するタブ：ローカル・WSL はフォルダがあるものだけ。SSH は待たせないよう確かめずに開く（開けなければタブにエラーが出る）。
    /// ここで落としたタブは RememberWorkspace でワークスペースからも消えるので、起動時とワークスペースの切り替えで同じものを使う。
    /// </summary>
    public static bool CanRestore(string path) => Core.Git.SshPath.IsSshPath(path) || System.IO.Directory.Exists(path);

    /// <summary>
    /// ワークスペースのタブをまとめて開く（起動時の復元とワークスペースの切り替え）。タブはすぐに全部作り、
    /// 選ぶタブ（ActiveRepository、無ければ最後のタブ）を先に読み、残りは左から順に後で読む（LoadPendingTabsAsync）。
    /// 返す Task は選んだタブの読み込みまで。
    /// </summary>
    public async Task RestoreTabsAsync(IReadOnlyList<string> paths, string? active)
    {
        RepositoryViewModel? last = null;
        // 空の TabControl に足すと最初のタブが選ばれるので、足し終えるまでは選んでも読まない
        _addingTabs = true;
        foreach (var p in paths)
        {
            last = Tabs.FirstOrDefault(t => string.Equals(t.Path, p, StringComparison.OrdinalIgnoreCase));
            if (last is not null) continue;
            last = new RepositoryViewModel(GitFor(p), Settings, p);
            _unloaded.Add(last);
            Tabs.Add(last);
        }
        _addingTabs = false;
        var select = (active is null ? null : Tabs.FirstOrDefault(t => t.Path == active)) ?? last;
        if (select is not null)
        {
            SelectedTab = select;
            LoadIfUnloaded(select);
        }
        RememberWorkspace();
        TouchRestoredTabs(paths, active);
        var selectedLoad = _selectedLoad;
        _ = LoadPendingTabsAsync();
        if (selectedLoad is not null) await selectedLoad;
    }

    /// <summary>まだ読んでいないタブ（RestoreTabsAsync で作っただけのもの）。選ばれたら先に読む（OnSelectedTabChanged）。</summary>
    private readonly List<RepositoryViewModel> _unloaded = [];
    private Task? _selectedLoad;
    private bool _loadingPending;
    private bool _addingTabs;

    private void LoadIfUnloaded(RepositoryViewModel tab)
    {
        if (!_addingTabs && _unloaded.Remove(tab)) _selectedLoad = tab.RefreshAsync();
    }

    private async Task LoadPendingTabsAsync()
    {
        if (_loadingPending) return;
        _loadingPending = true;
        try
        {
            while (true)
            {
                // 選んだタブの読み込みが済むまで待ち、その後も 1 つ読むごとに入力と描画を先に通す
                if (_selectedLoad is { } s) await s;
                await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background);
                if (_unloaded.Count == 0) break;
                var tab = _unloaded[0];
                _unloaded.RemoveAt(0);
                await tab.RefreshAsync();
            }
        }
        finally { _loadingPending = false; }
    }

    /// <summary>
    /// Workspaces > Import from Fork...：Workspaces・Repository Manager・タブの色を Fork のもので置き換え（ForkImport）、
    /// Fork の今のワークスペースのタブを開き直す。Repository Manager のタブは一覧を読み直すために作り直す。
    /// 今のタブは覚えない（置き換えるので）。
    /// </summary>
    public async Task ImportFromForkAsync(Core.Settings.ForkImportSource source)
    {
        var hadManager = Tabs.Any(t => t.IsManager);
        _switchingWorkspace = true;
        foreach (var t in Tabs.ToList())
        {
            t.StopWatching();
            Tabs.Remove(t);
        }
        Core.Settings.ForkImport.Apply(source, Settings);
        _switchingWorkspace = false;
        App.SaveSettings();
        NotifyWorkspaceChanged();
        await OpenActiveWorkspaceTabsAsync();
        var selected = SelectedTab;
        if (hadManager) NewTab();
        if (selected is not null) SelectedTab = selected;
        App.SaveSettings();
    }

    /// <summary>Ctrl+T：Repository Manager のタブ（1 つだけ。あればそれを選ぶ）。</summary>
    [RelayCommand]
    private void NewTab()
    {
        var manager = Tabs.FirstOrDefault(t => t.IsManager);
        if (manager is null)
        {
            manager = RepositoryViewModel.CreateManager(Git, Settings);
            Tabs.Add(manager);
        }
        SelectedTab = manager;
    }

    /// <summary>アクティブなワークスペースのタブ一覧を settings に書く（再起動時に復元。ログ "Restore workspace 'Home' with N tabs"）。</summary>
    private bool _switchingWorkspace;

    private void RememberWorkspace()
    {
        if (_switchingWorkspace) return;
        var ws = Settings.Workspaces.All.FirstOrDefault(w => w.Name == Settings.Workspaces.ActiveWorkspace);
        if (ws is null)
        {
            ws = new Workspace { Name = Settings.Workspaces.ActiveWorkspace };
            Settings.Workspaces.All.Add(ws);
        }
        ws.Repositories = Tabs.Where(t => !t.IsManager).Select(t => t.Path).ToList();
        ws.ActiveRepository = SelectedTab is { IsManager: false } t ? t.Path : ws.ActiveRepository;
    }

    /// <summary>同じ名前のタブに親フォルダの名前を付ける（RepositoryViewModel.TabName）。タブの増減と名前の変更のたびに呼ぶ。</summary>
    public void UpdateTabSuffixes()
    {
        foreach (var group in Tabs.Where(t => !t.IsManager).GroupBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            var dup = group.Count() > 1;
            foreach (var t in group)
                t.TabSuffix = dup ? System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(t.Path.TrimEnd('\\', '/')) ?? "") : null;
        }
    }

    partial void OnSelectedTabChanged(RepositoryViewModel? value)
    {
        // タブを選ぶだけでは Quick Launch の Recent の順は変わらない
        RememberWorkspace();
        if (value is not null) LoadIfUnloaded(value);
    }

    /// <summary>
    /// タブを復元した後、Recent の同じ秒の順を決める：選んでいたタブ、その左を右から順に、次に右を左から順に。
    /// </summary>
    public void TouchRestoredTabs(IReadOnlyList<string> paths, string? active)
    {
        var i = active is null ? -1 : paths.ToList().FindIndex(p => string.Equals(p, active, StringComparison.OrdinalIgnoreCase));
        var order = i < 0 ? paths.ToList() : [paths[i], .. paths.Take(i).Reverse(), .. paths.Skip(i + 1)];
        try
        {
            var list = Core.Settings.RepositoryList.Load();
            var now = DateTimeOffset.UtcNow;
            foreach (var p in order) list.Touch(p, now);
        }
        catch (System.IO.IOException) { }
    }

    [RelayCommand]
    private void CloseTab(RepositoryViewModel? tab)
    {
        tab ??= SelectedTab;
        if (tab is null) return;
        var index = Tabs.IndexOf(tab);
        tab.StopWatching();
        Tabs.Remove(tab);
        _closedTabs.Push(tab.Path);
        if (Tabs.Count > 0) SelectedTab = Tabs[Math.Clamp(index, 0, Tabs.Count - 1)];
        RememberWorkspace();
    }

    /// <summary>タブをドラッグで並べ替える（#4）。並びはワークスペースに残す。</summary>
    public void MoveTab(RepositoryViewModel tab, int index)
    {
        var from = Tabs.IndexOf(tab);
        index = Math.Clamp(index, 0, Tabs.Count - 1);
        if (from < 0 || from == index) return;
        Tabs.Move(from, index);
        SelectedTab = tab;
        RememberWorkspace();
    }

    [RelayCommand]
    private async Task RestoreClosedTabAsync()
    {
        if (_closedTabs.TryPop(out var path)) await OpenAsync(path);
    }

    [RelayCommand]
    private void SelectNextTab()
    {
        if (Tabs.Count == 0 || SelectedTab is null) return;
        SelectedTab = Tabs[(Tabs.IndexOf(SelectedTab) + 1) % Tabs.Count];
    }

    [RelayCommand]
    private void SelectPreviousTab()
    {
        if (Tabs.Count == 0 || SelectedTab is null) return;
        SelectedTab = Tabs[(Tabs.IndexOf(SelectedTab) - 1 + Tabs.Count) % Tabs.Count];
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (SelectedTab is not null) await SelectedTab.RefreshAsync();
    }
}

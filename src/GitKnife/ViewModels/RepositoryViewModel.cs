using System.IO;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitKnife.Core.Git;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Git.Model;
using GitKnife.Core.Graph;
using GitKnife.Core.Logging;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels;

public enum RepositoryView { LocalChanges, AllCommits }
public enum SidebarTab { Branches, Search, Service }
public enum DetailsMode { Commit, Changes, FileTree }

/// <summary>1 タブ＝1 リポジトリ。</summary>
public sealed partial class RepositoryViewModel : ObservableObject, IRevisionDetailsHost
{
    private readonly GitProcess _git;
    private readonly AppSettings _settings;
    private RepositoryReader? _reader;

    public string Path { get; }
    /// <summary>この worktree の git ディレクトリと、worktree で共有する git ディレクトリ（開いた後で決まる）。</summary>
    public string? GitDirectory => _reader?.GitDirectory;
    /// <summary>git が返すリポジトリの場所（rev-parse --show-toplevel）。割り当てたドライブ（U: → //wsl.localhost/...）では Path と書き方が違うので、worktree のパスと比べるときはこちら。</summary>
    public string WorkingDirectory => _reader?.WorkingDirectory ?? Path;
    public string? CommonGitDirectory => _reader?.CommonGitDirectory;
    public GitProcess Git => _git;
    /// <summary>Activity Manager のジョブ（新しいものが先頭）。</summary>
    public System.Collections.ObjectModel.ObservableCollection<ActivityJob> Jobs { get; } = [];

    /// <summary>
    /// リモートの "Fetch 'x' Automatically" が外れているか（git config の remote.&lt;name&gt;.disableimplicitfetch）。
    /// メニューを作るときに読むので、git を呼ばず .git/config を読む。
    /// </summary>
    public bool IsImplicitFetchDisabled(string remote)
    {
        try
        {
            var config = System.IO.Path.Combine(CommonGitDirectory ?? System.IO.Path.Combine(Path, ".git"), "config");
            var inSection = false;
            foreach (var raw in Core.Git.HostFile.ReadLines(config))
            {
                var line = raw.Trim();
                if (line.StartsWith('[')) { inSection = line.Equals($"[remote \"{remote}\"]", StringComparison.OrdinalIgnoreCase); continue; }
                if (!inSection) continue;
                var eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim().Equals("disableimplicitfetch", StringComparison.OrdinalIgnoreCase))
                    return line[(eq + 1)..].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (System.IO.IOException) { }
        return false;
    }

    /// <summary>自動 fetch（リモートごとに `fetch --prune <remote> --progress`。Background のジョブ。失敗しても知らせない）。</summary>
    public async Task AutoFetchAsync()
    {
        var remotes = References?.Remotes.Select(r => r.Name).Where(r => !IsImplicitFetchDisabled(r)).ToList() ?? [];
        if (remotes.Count == 0) return;
        foreach (var remote in remotes)
        {
            var job = StartJob($"Fetch '{remote}'", background: true);
            using (job.Enter())
            {
                try
                {
                    await RunGitAsync(["fetch", "--prune", remote, "--progress"]);
                    job.Finish(true, job.OutputText.Contains("->", StringComparison.Ordinal) ? null : "Already up to date");
                }
                catch (GitException ex) { job.Finish(false, ex.Result.StandardError.Trim().Split('\n')[0]); }
                catch (OperationCanceledException) { job.Finish(false, "Canceled"); }
            }
        }
        await RefreshAsync();
    }

    public ActivityJob StartJob(string name, bool background = false)
    {
        var job = new ActivityJob(name, background);
        Jobs.Insert(0, job);
        job.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ActivityJob.IsRunning)) { OnPropertyChanged(nameof(RunningJob)); OnJobStateChanged(job); }
            if (e.PropertyName == nameof(ActivityJob.Percent)) OnPropertyChanged(nameof(StatusPercent));
        };
        OnPropertyChanged(nameof(RunningJob));
        if (!background) ShowStatusJob(job);
        return job;
    }

    // ---- ツールバーの真ん中の欄に出すジョブ ----
    // 実行中：1 行目に回る印と "Pull 'origin/master'"、2 行目に "Pulling..."、右に ×、下端に割合の青いバー。
    // 終わったら 2 行目に結果（"Everything is up to date"）とバーをいっぱいにして約 3 秒出し、リポジトリ名とブランチに戻る。切り替わるときは上へずれる。

    private System.Windows.Threading.DispatcherTimer? _statusJobTimer;

    /// <summary>真ん中の欄に出しているジョブ（null ならリポジトリ名とブランチ）。</summary>
    public ActivityJob? StatusJob { get; private set; }
    public bool ShowsStatusJob => StatusJob is not null;
    public bool IsStatusJobRunning => StatusJob is { IsRunning: true };
    public string StatusLine1 => StatusJob?.Name ?? StatusTitle;
    public string StatusLine2 => StatusJob is not { } job ? CurrentBranchText
        : job.IsRunning ? RunningText(job.Name)
        : job.Details.Length > 0 ? job.Details.Split('\n')[0].Trim() : job.IsFailed ? "Failed" : "Done";
    /// <summary>下端のバーの値（0〜100）。終わったら 100、割合がまだ無ければ少しだけ（動かない短いバー）。</summary>
    public double StatusPercent => StatusJob is not { } job ? 0 : !job.IsRunning ? 100 : job.Percent ?? 4;

    /// <summary>2 行目の動作中の文（Pull なら "Pulling..."。ほかも同じ形）。</summary>
    private static string RunningText(string jobName)
    {
        var verb = jobName.Split(' ', 2)[0];
        return verb switch
        {
            "Pull" => "Pulling...",
            "Fetch" => "Fetching...",
            "Push" => "Pushing...",
            "Clone" => "Cloning...",
            "Checkout" => "Checking out...",
            "Merge" => "Merging...",
            "Rebase" => "Rebasing...",
            _ => "Running...",
        };
    }

    private void ShowStatusJob(ActivityJob? job)
    {
        _statusJobTimer?.Stop();
        StatusJob = job;
        OnPropertyChanged(nameof(StatusJob));
        OnStatusChanged();
    }

    private void OnStatusChanged()
    {
        OnPropertyChanged(nameof(ShowsStatusJob));
        OnPropertyChanged(nameof(IsStatusJobRunning));
        OnPropertyChanged(nameof(StatusLine1));
        OnPropertyChanged(nameof(StatusLine2));
        OnPropertyChanged(nameof(StatusPercent));
        OnPropertyChanged(nameof(ShowBranchIcon));
    }

    private void OnJobStateChanged(ActivityJob job)
    {
        if (job != StatusJob || job.IsRunning) return;
        OnStatusChanged();
        _statusJobTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusJobTimer.Tick += (_, _) => { if (StatusJob == job) ShowStatusJob(Jobs.FirstOrDefault(j => j.IsRunning && !j.IsBackground)); };
        _statusJobTimer.Start();
    }

    /// <summary>今動いているジョブ（ステータス欄の CancelButton を出す。新しいものから）。</summary>
    public ActivityJob? RunningJob => Jobs.FirstOrDefault(j => j.IsRunning);
    /// <summary>
    /// タブの名前。Rename した名前（repositories.toml の alias）があればそれ。追加の worktree は "basic: wt-test"（元のリポジトリ名: フォルダ名）。
    /// </summary>
    public string Name
    {
        get
        {
            if (IsManager) return "Repository Manager";
            if (Alias is { Length: > 0 } alias) return alias;
            var folder = System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
            if (_reader is { } r && !string.Equals(r.GitDirectory.TrimEnd('\\', '/'), r.CommonGitDirectory.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                var main = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(r.CommonGitDirectory.TrimEnd('\\', '/')) ?? "");
                if (main.Length > 0) return $"{main}: {folder}";
            }
            return folder;
        }
    }
    /// <summary>
    /// Repository Manager のタブ（Ctrl+T）。リポジトリを持たず、git は読まない。
    /// ステータス欄は "Welcome to GitKnife!" / "Open a repository to start"。
    /// </summary>
    public bool IsManager { get; init; }
    /// <summary>ステータス欄の 1 行目（TitleTextBlock）。</summary>
    public string StatusTitle => IsManager ? "Welcome to GitKnife!" : Name;
    public bool ShowBranchIcon => !IsManager && StatusJob is null;

    /// <summary>
    /// タブに出す名前。同じ名前のタブがほかにあれば親フォルダの名前を括弧で付ける（例：\\wsl.localhost\…\TV\epgrec と …\epgrec\epgrec が
    /// "epgrec (TV)" と "epgrec (epgrec)"。UIA の名前も同じ）。付けるかどうかは MainViewModel が決める。
    /// </summary>
    public string TabName => TabSuffix is { Length: > 0 } s ? $"{Name} ({s})" : Name;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TabName))] private string? _tabSuffix;

    /// <summary>repositories.toml の alias（Repository Manager かタブの Rename。最初に読んだものを覚える。変えたら RefreshAlias）。</summary>
    private string? Alias => _alias ??= IsManager ? "" : LoadAlias();
    private string? _alias;
    private string LoadAlias()
    {
        try { return Core.Settings.RepositoryList.Load().Get(Path)?.Alias ?? ""; }
        catch (System.IO.IOException) { return ""; }
    }

    /// <summary>alias を読み直してタブの名前を出し直す（Repository Manager で Rename したとき）。</summary>
    public void RefreshAlias()
    {
        _alias = null;
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TabName));
        OnPropertyChanged(nameof(StatusTitle));
        OnPropertyChanged(nameof(StatusLine1));
    }

    /// <summary>タブの Rename（見出しがその場で入力欄になる）。</summary>
    [ObservableProperty] private bool _isRenaming;
    [ObservableProperty] private string _renameText = "";

    public void BeginRename()
    {
        RenameText = Name;
        IsRenaming = true;
    }

    public void EndRename(bool commit)
    {
        if (!IsRenaming) return;
        IsRenaming = false;
        var name = RenameText.Trim();
        if (!commit || name.Length == 0 || name == Name || IsManager) return;
        // Repository Manager の Rename と同じ alias に書く
        try { Core.Settings.RepositoryList.Load().SetAlias(Path, name); }
        catch (System.IO.IOException ex) { Core.Logging.Log.Warning($"Saving repositories.toml failed: {ex.Message}"); }
        RefreshAlias();
    }

    /// <summary>Local Changes に ignore されたファイルも出す（"Show Ignored Files"。タブごと、保存しない）。</summary>
    public bool ShowIgnored { get; set; }

    /// <summary>
    /// untracked を隠す（"Hide Untracked Files"）。worktree-config.toml の hide_untracked_files に保存する。
    /// </summary>
    public bool HideUntracked
    {
        get => LoadWorktreeConfig()?.HideUntrackedFiles ?? false;
        set
        {
            if (LoadWorktreeConfig() is not { } c) return;
            c.EnsureDefaults();
            c.HideUntrackedFiles = value;
            try { c.Save(); } catch (System.IO.IOException ex) { Log.Warning($"Saving worktree config failed: {ex.Message}"); }
        }
    }

    /// <summary>.gitmodules の submodule（読み直すたびに更新）。</summary>
    public IReadOnlyList<Core.Git.SubmoduleInfo> Submodules { get; private set; } = [];
    public IReadOnlyList<string> FilterReferences { get; private set; } = [];
    public IReadOnlyList<string> HiddenReferences { get; private set; } = [];
    private IReadOnlyList<string> _expandedFilter = [];
    public bool IsFiltered => FilterReferences.Count > 0;
    /// <summary>一覧の上の帯の参照（"'feature/*', 'origin/feature/*'"。Filter by Active Branch は "'main', 'origin/main'"）。</summary>
    public string FilterBarText => string.Join(", ", _expandedFilter.Select(ReferenceFilter.Display));

    // ---------------------------------------------------------- View の表示オプション（worktree-config.toml）

    /// <summary>Hide Tags（hide_tags）：タグのラベルを出さず、タグからは辿らない。</summary>
    public bool HideTags { get; private set; }
    /// <summary>Hide Stashes in Commit List（hide_stashes）。</summary>
    public bool HideStashes { get; private set; }
    /// <summary>Collapse All Merges (Show First Parent)（collapse_all_mode）：`--first-parent`。</summary>
    public bool CollapseAllMerges { get; private set; }
    /// <summary>Show Lost Commits (Reflog)。設定ファイルに書かない（このタブのあいだだけ）。</summary>
    public bool ShowReflog { get; private set; }

    /// <summary>一覧の上の帯。Reflog が先。</summary>
    public bool ShowStatusBar => ShowReflog || IsFiltered;
    public string StatusBarHeader => ShowReflog ? "Reflog mode enabled" : "Filtered by:";
    public string StatusBarButtonText => ShowReflog ? "Exit" : "Clear filter";
    /// <summary>View の最後の項目（Filter が付いていると "Clear Branch Filter"）。</summary>
    public string ActiveBranchFilterHeader => IsFiltered ? "Clear Branch Filter" : "Filter by Active Branch";

    private async Task SetWorktreeOptionAsync(Action<Core.Settings.WorktreeConfig> change)
    {
        if (LoadWorktreeConfig() is not { } c) return;
        c.EnsureDefaults();
        change(c);
        try { c.Save(); } catch (System.IO.IOException ex) { Log.Warning($"Saving worktree config failed: {ex.Message}"); }
        await RefreshAsync();
    }

    public Task ToggleHideTagsAsync() => SetWorktreeOptionAsync(c => c.HideTags = !HideTags);
    public Task ToggleHideStashesAsync() => SetWorktreeOptionAsync(c => c.HideStashes = !HideStashes);
    /// <summary>Collapse All Merges。切り替えると個別に開閉したマージ（collapse_toggled_shas）は空に戻る。</summary>
    public Task ToggleCollapseAllMergesAsync() => SetWorktreeOptionAsync(c => { c.CollapseAllMode = !CollapseAllMerges; c.CollapseToggledShas = []; });

    /// <summary>
    /// マージを 1 つたたむ・開く（← / →、ノードのクリック）。worktree-config.toml の collapse_toggled_shas に 40 桁の SHA を足し引きする
    /// （Collapse All Merges のときは「開いたマージ」の一覧になる）。マージでない行では何もしない。
    /// </summary>
    public Task SetMergeCollapsedAsync(CommitRow row, bool collapsed)
    {
        var c = row.Commit;
        var isMerge = (c.Parents.Count > 1 && !c.IsStash) || c.IsCollapsedMerge;
        if (!isMerge || c.IsCollapsedMerge == collapsed) return Task.CompletedTask;
        return SetWorktreeOptionAsync(w =>
        {
            var list = (w.CollapseToggledShas ?? []).ToList();
            if (!list.Remove(c.Sha)) list.Add(c.Sha);
            w.CollapseToggledShas = list;
        });
    }
    /// <summary>Filter by Active Branch（filter_references = ["HEAD"]）/ Clear Branch Filter（空に）。</summary>
    public Task ToggleActiveBranchFilterAsync() => SetWorktreeOptionAsync(c => c.FilterReferences = IsFiltered ? [] : ["HEAD"]);

    public async Task ToggleReflogAsync()
    {
        ShowReflog = !ShowReflog;
        await RefreshAsync();
    }

    /// <summary>帯のボタン（Exit / Clear filter）。</summary>
    [RelayCommand]
    private async Task StatusBarButton()
    {
        if (ShowReflog) await ToggleReflogAsync();
        else await ClearFilter();
    }

    /// <summary>サイドバーの FilterButton / HideButton。付いていれば外し、無ければ足す。worktree-config.toml に書いて読み直す。</summary>
    public async Task ToggleReferenceFilterAsync(SidebarItem item, bool hide)
    {
        if (LoadWorktreeConfig() is not { } c) return;
        c.EnsureDefaults();
        var list = (hide ? c.HiddenReferences : c.FilterReferences).ToList();
        if (item.FilterPatterns.All(list.Contains)) list.RemoveAll(item.FilterPatterns.Contains);
        else list.AddRange(item.FilterPatterns.Where(p => !list.Contains(p)));
        if (hide) c.HiddenReferences = list; else c.FilterReferences = list;
        try { c.Save(); } catch (System.IO.IOException ex) { Log.Warning($"Saving worktree config failed: {ex.Message}"); }
        await RefreshAsync();
    }

    /// <summary>サイドバーの PinButton：config.toml の pinned_references に足す・外す。</summary>
    public async Task TogglePinAsync(SidebarItem item)
    {
        if (item.Reference is not { } r || CommonGitDirectory is not { } common) return;
        var config = Core.Settings.RepositoryConfig.Load(common);
        config.EnsureDefaults();
        var list = config.PinnedReferences.ToList();
        if (!list.Remove(r.CanonicalName)) list.Add(r.CanonicalName);
        config.PinnedReferences = list;
        try { config.Save(); } catch (System.IO.IOException ex) { Log.Warning($"Saving repository config failed: {ex.Message}"); }
        await RefreshAsync();
    }

    /// <summary>帯の Clear filter。</summary>
    [RelayCommand]
    private async Task ClearFilter()
    {
        if (LoadWorktreeConfig() is not { } c) return;
        c.EnsureDefaults();
        c.FilterReferences = [];
        try { c.Save(); } catch (System.IO.IOException) { }
        await RefreshAsync();
    }

    private Core.Settings.WorktreeConfig? LoadWorktreeConfig() => GitDirectory is { } dir ? Core.Settings.WorktreeConfig.Load(dir) : null;

    private bool _sidebarExpandedLoaded;

    private void SaveSidebarExpanded()
    {
        if (LoadWorktreeConfig() is not { } c) return;
        c.EnsureDefaults();
        c.ExpandedSidebarItems = Sidebar.ExpandedPaths();
        try { c.Save(); } catch (System.IO.IOException ex) { Log.Warning($"Saving worktree config failed: {ex.Message}"); }
    }

    private bool _draftLoaded;

    /// <summary>
    /// コミットメッセージの下書き（worktree-config.toml の draft_message）。Local Changes から別の表示に移ったときに書く（件名だけなら普通の文字列、
    /// 本文があれば "件名\n\n本文" の複数行の文字列）。タブを閉じるときも書く。開いたときに件名が空なら読み込む。
    /// </summary>
    public void SaveDraft()
    {
        if (IsManager || LoadWorktreeConfig() is not { } c) return;
        var message = LocalChanges.Description.Trim().Length > 0 ? LocalChanges.Subject + "\n\n" + LocalChanges.Description.TrimEnd() : LocalChanges.Subject;
        if (c.DraftMessage == message) return;
        c.EnsureDefaults();
        c.DraftMessage = message;
        try { c.Save(); } catch (System.IO.IOException ex) { Log.Warning($"Saving worktree config failed: {ex.Message}"); }
    }

    private void LoadDraft(string? draft)
    {
        if (_draftLoaded) return;
        _draftLoaded = true;
        if (string.IsNullOrEmpty(draft) || LocalChanges.Subject.Length > 0) return;
        var text = draft.Replace("\r", "");
        var nl = text.IndexOf('\n');
        LocalChanges.Subject = nl < 0 ? text : text[..nl];
        LocalChanges.Description = nl < 0 ? "" : text[(nl + 1)..].TrimStart('\n');
    }

    partial void OnViewChanged(RepositoryView oldValue, RepositoryView newValue)
    {
        if (oldValue == RepositoryView.LocalChanges && newValue != RepositoryView.LocalChanges) SaveDraft();
    }
    public static RepositoryViewModel CreateManager(GitProcess git, AppSettings settings) => new(git, settings, "") { IsManager = true };
    public SidebarViewModel Sidebar { get; } = new();
    public LocalChangesViewModel LocalChanges { get; }
    public RevisionDetailsViewModel Details { get; }
    public NotificationBarViewModel Notification { get; } = new();
    /// <summary>コミット詳細を畳んでいる（Ctrl+Shift+D、設定 RevisionDetailsPaneCollapsed。全タブ共通）。</summary>
    public bool DetailsCollapsed => _settings.RevisionDetailsPaneCollapsed;
    public void NotifyDetailsCollapsedChanged() => OnPropertyChanged(nameof(DetailsCollapsed));

    /// <summary>コミット一覧の縦レイアウト（Appearance > Vertical。設定 RevisionListOrientation：0 = Vertical、1 = Horizontal。全タブ共通）。</summary>
    public bool IsVerticalLayout => _settings.RevisionListOrientation == 0;
    public void NotifyLayoutChanged() => OnPropertyChanged(nameof(IsVerticalLayout));
    /// <summary>コミット詳細の読み取り。リポジトリを開くまで null。</summary>
    public RevisionReader? RevisionReader { get; private set; }
    public AppSettings Settings => _settings;
    /// <summary>Local Changes の操作。リポジトリを開くまで null。</summary>
    public WorkingTree? WorkingTree { get; private set; }
    public ObservableCollection<CommitRow> Commits { get; } = [];

    [ObservableProperty] private RepositoryReferences? _references;
    [ObservableProperty] private RepositoryStatus? _status;
    [ObservableProperty] private RepositoryView _view = RepositoryView.AllCommits;
    [ObservableProperty] private SidebarTab _sidebarTab = SidebarTab.Branches;
    [ObservableProperty] private DetailsMode _detailsMode = DetailsMode.Commit;
    [ObservableProperty] private CommitRow? _selectedCommit;
    [ObservableProperty] private bool _isBusy;

    /// <summary>既定のメインブランチ：main、無くて master があれば master。</summary>
    public static string DefaultMainBranch(IReadOnlyCollection<string> localBranches)
        => localBranches.Contains("main") || !localBranches.Contains("master") ? "main" : "master";

    /// <summary>Lean Branching のメインブランチ（Repository Settings の "Main Branch:"。config.toml の lean_branching_main_branch、無ければ既定）。</summary>
    public string LeanMainBranch
        => (CommonGitDirectory is { } common ? Core.Settings.RepositoryConfig.Load(common).LeanBranchingMainBranch : null)
           ?? DefaultMainBranch(References?.LocalBranches.Select(b => b.ShortName).ToList() ?? []);
    /// <summary>Issue Tracker のルール（コミット詳細のメッセージのリンク。Controls/IssueLinkText）。</summary>
    [ObservableProperty] private IReadOnlyList<Core.Settings.IssueTrackerRule> _issueRules = [];
    [ObservableProperty] private string? _error;
    /// <summary>グラフ列に必要な列数（全行の最大）。</summary>
    [ObservableProperty] private int _graphLanes = 1;

    public string CurrentBranchText => IsManager ? "Open a repository to start" : References?.HeadBranch ?? (References?.HeadSha is { } sha ? sha[..9] : "");
    public string LocalChangesText => Status is { ChangeCount: > 0 } s ? $"Local Changes ({s.ChangeCount})" : "Local Changes";
    public int AheadCount => References?.Head?.Ahead ?? 0;
    public int BehindCount => References?.Head?.Behind ?? 0;
    public bool HasLocalChanges => Status?.ChangeCount > 0;

    /// <summary>タブの色（repositories.toml の color。"Red" など。null は無し）。</summary>
    public string? TabColor
    {
        get => _tabColor ??= IsManager ? "" : Core.Settings.RepositoryList.Load().GetColor(Path) ?? "";
        private set => _tabColor = value ?? "";
    }
    private string? _tabColor;

    /// <summary>タブの左の点：色を付けたらその色（変更が無くても出す。推定）、無ければ未コミットの変更があるときだけ灰色。</summary>
    public bool ShowTabDot => HasLocalChanges || !string.IsNullOrEmpty(TabColor);

    public void SetTabColor(string? color)
    {
        try { Core.Settings.RepositoryList.Load().SetColor(Path, color); }
        catch (System.IO.IOException ex) { Log.Warning($"Saving repositories.toml failed: {ex.Message}"); }
        TabColor = color;
        OnPropertyChanged(nameof(TabColor));
        OnPropertyChanged(nameof(ShowTabDot));
    }

    public RepositoryViewModel(GitProcess git, AppSettings settings, string path)
    {
        _git = git;
        _settings = settings;
        Path = path;
        LocalChanges = new LocalChangesViewModel(this);
        Details = new RevisionDetailsViewModel(this);
        Sidebar.Navigate += NavigateToSidebarItem;
        Search = new SearchViewModel(this);
        Find = new CommitFindViewModel(this);
        Find.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(CommitFindViewModel.Term)) OnPropertyChanged(nameof(ListHighlightTerm)); };
        Search.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(SearchViewModel.HighlightTerm)) OnPropertyChanged(nameof(ListHighlightTerm)); };
    }

    /// <summary>サイドバーの Search タブ。</summary>
    public SearchViewModel Search { get; }

    /// <summary>コミット一覧の Ctrl+F の検索パネル。</summary>
    public CommitFindViewModel Find { get; }

    /// <summary>コミット一覧の件名で色を付ける語（Ctrl+F の一覧内検索が先、無ければ Search タブの Commit Message の検索語）。</summary>
    public string ListHighlightTerm => Find.Term.Length > 0 ? Find.Term : Search.HighlightTerm;

    RepositoryViewModel IRevisionDetailsHost.Repository => this;

    /// <summary>一覧（All Commits）でそのコミットを選ぶ（一覧に無ければ何もしない）。</summary>
    public void NavigateToCommit(string sha)
    {
        if (Commits.FirstOrDefault(c => c.Commit.Sha == sha) is not { } row) return;
        View = RepositoryView.AllCommits;
        SelectedCommit = row;
    }

    /// <summary>サイドバーでブランチ・タグ・stash を選ぶと、一覧（All Commits）でそのコミットを選ぶ。</summary>
    private void NavigateToSidebarItem(SidebarItem item)
    {
        var sha = item.Reference?.TargetSha ?? item.Stash?.Sha;
        if (sha is null || Commits.FirstOrDefault(c => c.Commit.Sha == sha) is not { } row) return;
        View = RepositoryView.AllCommits;
        SelectedCommit = row;
    }

    /// <summary>サイドバーで選んだ 2 つのコミットを比べる（古い方が先。一覧で 2 つ選んだときと同じ表示）。</summary>
    public void CompareSidebarItems(SidebarItem a, SidebarItem b)
    {
        var rows = new[] { a, b }.Select(i => i.Reference?.TargetSha ?? i.Stash?.Sha)
            .Select(sha => sha is null ? null : Commits.FirstOrDefault(c => c.Commit.Sha == sha)).ToList();
        if (rows[0] is not { } r0 || rows[1] is not { } r1 || r0 == r1) return;
        View = RepositoryView.AllCommits;
        var (older, newer) = Commits.IndexOf(r0) > Commits.IndexOf(r1) ? (r0, r1) : (r1, r0);
        _ = Details.LoadCompareAsync(older, newer);
    }

    partial void OnReferencesChanged(RepositoryReferences? value)
    {
        OnPropertyChanged(nameof(CurrentBranchText));
        OnPropertyChanged(nameof(StatusLine2));
        OnPropertyChanged(nameof(AheadCount));
        OnPropertyChanged(nameof(BehindCount));
    }

    partial void OnSelectedCommitChanged(CommitRow? value) => _ = Details.LoadAsync(value);

    partial void OnDetailsModeChanged(DetailsMode value)
    {
        if (value == DetailsMode.FileTree) _ = Details.LoadTreeAsync();
    }

    partial void OnStatusChanged(RepositoryStatus? value)
    {
        OnPropertyChanged(nameof(LocalChangesText));
        OnPropertyChanged(nameof(HasLocalChanges));
        OnPropertyChanged(nameof(ShowTabDot));
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        if (IsManager) return;
        try
        {
            IsBusy = true;
            Error = null;
            var generation = ++_refreshGeneration;
            var startTicks = Environment.TickCount64;
            Interlocked.Exchange(ref _lastRefreshStartTicks, startTicks);
            Interlocked.Exchange(ref _lastFullRefreshStartTicks, startTicks);
            if (!await EnsureOpenAsync()) return;
            var refs = await _reader!.ReadReferencesAsync();
            var statusTask = _reader!.ReadStatusAsync(ShowIgnored);
            // サイドバーの Filter / Hide（worktree-config.toml）
            var wt = LoadWorktreeConfig();
            FilterReferences = wt?.FilterReferences ?? [];
            HiddenReferences = wt?.HiddenReferences ?? [];
            HideTags = wt?.HideTags ?? false;
            HideStashes = wt?.HideStashes ?? false;
            CollapseAllMerges = wt?.CollapseAllMode ?? false;
            LoadDraft(wt?.DraftMessage);
            var toggled = (wt?.CollapseToggledShas ?? []).ToHashSet();
            // View の表示オプション。Reflog のあいだは Filter を効かせない（帯も Reflog の方）
            var effectiveFilter = ShowReflog ? [] : ReferenceFilter.Expand(refs, FilterReferences);
            var start = ReferenceFilter.StartPoints(refs, effectiveFilter, HiddenReferences);
            var loaded = await _reader!.ReadCommitsAsync(new CommitQuery
            {
                Order = (RevisionSortOrder)_settings.RevisionSortOrder,
                MaxCount = _settings.MaxCommitCount,
                OnlyReferences = start ?? [],
                StashesWithReferences = effectiveFilter.Count == 0,
                IncludeTags = !HideTags,
                IncludeStashes = !HideStashes,
                IncludeReflog = ShowReflog,
            }, refs);
            var status = await statusTask;
            // 後から始めた読み直しがあれば、古い内容で上書きしない（Issue #12）
            if (generation != _refreshGeneration) return;
            // マージの折りたたみ（Collapse All Merges も含めて、読んだあとでたたむ）
            var commits = Core.Graph.MergeCollapse.Apply(loaded, sha => Core.Graph.MergeCollapse.IsCollapsed(sha, CollapseAllMerges, toggled));
            _expandedFilter = effectiveFilter;
            OnPropertyChanged(nameof(FilterBarText));
            OnPropertyChanged(nameof(IsFiltered));
            OnPropertyChanged(nameof(ShowStatusBar));
            OnPropertyChanged(nameof(StatusBarHeader));
            OnPropertyChanged(nameof(StatusBarButtonText));
            OnPropertyChanged(nameof(ActiveBranchFilterHeader));
            var graph = GraphLayout.Compute(commits);
            var rows = CommitRow.Build(commits, graph, refs, hideTags: HideTags, hiddenReferences: HiddenReferences);
            var selectedSha = SelectedCommit?.Commit.Sha;
            References = refs;
            Status = status;
            UpdateNotification();
            LocalChanges.Load(Status);
            LocalChanges.LoadRecentMessages(rows);
            var repoConfig = CommonGitDirectory is { } common ? Core.Settings.RepositoryConfig.Load(common) : null;
            var pins = repoConfig?.PinnedReferences ?? [];
            // コミットのメッセージの課題番号のリンク（Preferences と Repository Settings の両方がオンのとき）
            IssueRules = _settings.ShowBugtrackerLinks && repoConfig?.ShowBugtrackerLinks != false && CommonGitDirectory is { } gitDir
                ? Core.Settings.IssueTracker.Load(gitDir, Path) : [];
            Submodules = Core.Git.GitModules.Read(Path);
            var changedSubmodules = Status.Entries.Where(e => e.IsSubmodule).Select(e => e.Path).ToHashSet();
            if (!_sidebarExpandedLoaded)
            {
                // 最初の読み込みで worktree-config.toml の expanded_sidebar_items を当て、変えたら書く
                _sidebarExpandedLoaded = true;
                Sidebar.UseSavedExpanded(LoadWorktreeConfig()?.ExpandedSidebarItems);
                Sidebar.ExpandedChanged += SaveSidebarExpanded;
            }
            // worktree のパスと比べるのは git が返す場所（割り当てたドライブ U: は git では //wsl.localhost/... になる。2026-10-27 main だけなのに Worktrees が出た）
            Sidebar.Load(refs, _settings, loaded.GroupBy(c => c.Sha).ToDictionary(g => g.Key, g => g.First().CommitTime), WorkingDirectory, ReferenceFilter.Expand(refs, FilterReferences), HiddenReferences, pins,
                Submodules, changedSubmodules);
            OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(TabName));
            OnPropertyChanged(nameof(StatusTitle));
            OnPropertyChanged(nameof(StatusLine1));
            GraphLanes = graph.Count == 0 ? 1 : graph.Max(g => g.Width);
            Commits.Clear();
            foreach (var r in rows) Commits.Add(r);
            SelectedCommit = Commits.FirstOrDefault(c => c.Commit.Sha == selectedSha) ?? Commits.FirstOrDefault(c => c.IsHead) ?? Commits.FirstOrDefault();
            Find.Refresh(select: false);
            Log.Info($"Refresh '{Name}' data. {commits.Count} commits, status {Status.ChangeCount} files");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Error($"Refresh '{Name}' failed: {ex}");
        }
        finally { IsBusy = false; }
    }

    private async Task<bool> EnsureOpenAsync()
    {
        _reader ??= await RepositoryReader.OpenAsync(_git, Path);
        if (_reader is null) { Error = $"'{Path}' is not a git repository"; return false; }
        WorkingTree ??= _reader.CreateWorkingTree();
        RevisionReader ??= _reader.CreateRevisionReader();
        StartWatching();
        return true;
    }

    // ------------------------------------------------------------------ 自動更新

    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _debounce;
    private bool _pendingFull;
    private long _lastEventTicks;
    private long _lastRefreshStartTicks;
    private long _lastFullRefreshStartTicks;
    private int _refreshGeneration;

    /// <summary>
    /// 作業ツリーと .git を見張り、変わったら少し待ってから読み直す。
    /// 参照（HEAD、refs、packed-refs）が変わったら全体、それ以外は status だけ。.git/objects や logs、*.lock は無視する。
    /// </summary>
    private void StartWatching()
    {
        if (_watcher is not null || _reader is null) return;
        // SSH のリポジトリは見張れない（変更は定期の status と窓の復帰で拾う。T1304）
        if (Core.Git.SshPath.IsSshPath(_reader.WorkingDirectory)) return;
        try
        {
            _watcher = new FileSystemWatcher(_reader.WorkingDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                InternalBufferSize = 64 * 1024,
            };
            _watcher.Changed += OnFileEvent;
            _watcher.Created += OnFileEvent;
            _watcher.Deleted += OnFileEvent;
            _watcher.Renamed += OnFileEvent;
            _watcher.Error += (_, _) => Schedule(full: true);
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Log.Warning($"FileSystemWatcher '{Name}' failed: {ex.Message}"); }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        var gitDir = _reader!.GitDirectory;
        var full = e.FullPath;
        if (full.StartsWith(gitDir, StringComparison.OrdinalIgnoreCase))
        {
            var rel = full.Length > gitDir.Length ? full[(gitDir.Length + 1)..].Replace(System.IO.Path.DirectorySeparatorChar, '/') : "";
            if (rel.EndsWith(".lock", StringComparison.Ordinal) || rel.StartsWith("objects", StringComparison.Ordinal) || rel.StartsWith("logs", StringComparison.Ordinal)
                || rel.StartsWith("gitknife", StringComparison.Ordinal) || rel == "COMMITMESSAGE" || rel.Length == 0) return;
            Schedule(full: rel is "HEAD" or "packed-refs" or "MERGE_HEAD" or "REBASE_HEAD" || rel.StartsWith("refs", StringComparison.Ordinal));
            return;
        }
        Schedule(full: false);
    }

    private void Schedule(bool full)
    {
        lock (this)
        {
            _lastEventTicks = Environment.TickCount64;
            Postpone(full);
        }
    }

    /// <summary>通知の時刻を変えずに待ち直す（時刻を進めると、自分の操作の読み直しで済む通知まで読み直してしまう）。</summary>
    private void Postpone(bool full)
    {
        lock (this)
        {
            _pendingFull |= full;
            _debounce ??= new System.Threading.Timer(_ => OnDebounced());
            _debounce.Change(400, Timeout.Infinite);
        }
    }

    private void OnDebounced()
    {
        bool full;
        lock (this) { full = _pendingFull; _pendingFull = false; }
        System.Windows.Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            // 自分の操作の途中（ステージなど）は、その後の読み直しに任せる
            if (IsBusy || LocalChanges.IsWorking) { Postpone(full); return; }
            // 通知より後に読み始めていれば読まない。読み始めた後の参照の変更は必ず読み直す（窓に戻っても status しか読まない。Issue #12）
            if (Core.Git.RefreshGate.CanSkip(full, Environment.TickCount64, Interlocked.Read(ref _lastEventTicks),
                    Interlocked.Read(ref _lastRefreshStartTicks), Interlocked.Read(ref _lastFullRefreshStartTicks))) return;
            if (full) await RefreshAsync(); else await RefreshStatusAsync();
        });
    }

    /// <summary>タブを閉じたとき。</summary>
    public void StopWatching()
    {
        SaveDraft();
        _watcher?.Dispose();
        _watcher = null;
        _debounce?.Dispose();
        _debounce = null;
    }

    /// <summary>status だけ読み直す（ステージ操作の後。コミット一覧は変わらない）。</summary>
    public async Task RefreshStatusAsync()
    {
        if (IsManager) return;
        try
        {
            if (!await EnsureOpenAsync()) return;
            Interlocked.Exchange(ref _lastRefreshStartTicks, Environment.TickCount64);
            Status = await _reader!.ReadStatusAsync(ShowIgnored);
            UpdateNotification();
            LocalChanges.Load(Status);
            Sidebar.SetChangedSubmodules(Status.Entries.Where(e => e.IsSubmodule).Select(e => e.Path).ToHashSet());
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Error($"Refresh status '{Name}' failed: {ex}");
        }
    }

    private void UpdateNotification()
    {
        if (_reader is null || References is null) return;
        var conflicts = Status?.Entries.Count(e => e.IsConflicted) ?? 0;
        var state = _reader.ReadOperationState(References);
        // 操作の印が無くても競合したファイルがあれば帯を出す（2026-10-27 Frieve-Editor：Pull の autostash を戻したときの競合）
        if (state.Operation == RepositoryOperation.None && conflicts > 0) state = new OperationState(RepositoryOperation.Unmerged, "", References?.HeadBranch ?? "");
        Notification.Update(state, conflicts, References?.HeadSha);
    }

    /// <summary>base より後の、HEAD までの最初の親の列（Interactive Rebase の対象。新しい順）。</summary>
    public async Task<IReadOnlyList<CommitInfo>> ReadCommitsSinceAsync(string baseSha)
    {
        if (!await EnsureOpenAsync() || References is null) return [];
        return await _reader!.ReadCommitsAsync(new CommitQuery
        {
            Order = RevisionSortOrder.Topological, FirstParent = true, IncludeStashes = false, OnlyReferences = [$"{baseSha}..HEAD"],
        }, References);
    }

    /// <summary>競合中のファイルがあるか（操作が競合で止まったかの判定）。</summary>
    public async Task<bool> HasConflictsAsync()
    {
        if (_reader is null) return false;
        try { return (await _reader.ReadStatusAsync()).Entries.Any(e => e.IsConflicted); }
        catch { return false; }
    }

    /// <summary>git を実行して失敗なら GitException（ダイアログの実行に使う）。progress には stderr の進捗行が来る。</summary>
    public async Task<GitResult> RunGitAsync(string[] args, Action<string>? progress = null, CancellationToken ct = default, IReadOnlyDictionary<string, string>? environment = null)
    {
        // Preferences の "Enable verbose Git output"：操作（ジョブ）の中の git に GIT_TRACE を付け、Activity Manager の出力に trace の行を出す
        if (_settings.VerboseGitOutput && ActivityJob.Current is { IsRunning: true })
        {
            var env = environment is null ? new Dictionary<string, string>() : new Dictionary<string, string>(environment);
            env["GIT_TRACE"] = "1";
            env["GIT_TRACE_PERFORMANCE"] = "1";
            environment = env;
        }
        // ジョブの中で呼ばれ、呼んだ側が止め方を渡していなければ、ジョブの CancelButton で止まるようにする
        if (!ct.CanBeCanceled && ActivityJob.Current is { IsRunning: true } job) ct = job.Token;
        // 進捗の行はジョブにも渡す（StatusProgressBar の割合）
        var running = ActivityJob.Current is { IsRunning: true } current ? current : null;
        Action<string>? onProgress = running is null ? progress : line => { progress?.Invoke(line); running.ReportProgress(line); };
        var r = await _git.RunAsync(new GitRequest(Path, args) { OnProgress = onProgress, Environment = environment }, ct);
        return r.ThrowIfFailed();
    }

    /// <summary>
    /// 現在のブランチを push する（Commit and Push。`-c push.default=upstream push origin refs/heads/main --verbose --progress`）。
    /// upstream が無ければ最初のリモートに --set-upstream で送る。
    /// </summary>
    public async Task PushCurrentBranchAsync()
    {
        var refs = References;
        if (refs?.Head is not { } head || refs.Remotes.Count == 0) return;
        var remote = head.Upstream is { } up && up.StartsWith("refs/remotes/", StringComparison.Ordinal)
            ? up["refs/remotes/".Length..].Split('/', 2)[0]
            : refs.Remotes[0].Name;
        var r = await _git.RunAsync(new GitRequest(Path, GitArgs.Push(remote, head.ShortName, setUpstream: head.Upstream is null)));
        r.ThrowIfFailed();
    }

    [RelayCommand] private void ShowLocalChanges() => View = RepositoryView.LocalChanges;
    [RelayCommand] private void ShowAllCommits() => View = RepositoryView.AllCommits;
}

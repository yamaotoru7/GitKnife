using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Git.Model;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>Fetch。</summary>
public sealed partial class FetchDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public override string Title => "Fetch";
    /// <summary>Activity Manager の名前（"Fetch all"。リモートを選んだときは推定）。</summary>
    public override string ActivityName => FetchAll ? "Fetch all" : $"Fetch '{SelectedRemote}'";
    /// <summary>何も取ってこなかったら "Already up to date"。</summary>
    public override string ActivitySummary(string output) => output.Contains("->", StringComparison.Ordinal) ? base.ActivitySummary(output) : "Already up to date";
    public override string Subtitle => "Fetch latest changes from remote repository";
    public override string SubmitText => "Fetch";

    public IReadOnlyList<string> Remotes { get; }
    [ObservableProperty] private string? _selectedRemote;
    /// <summary>"Fetch all remotes"（設定 Fetch_FetchAllRemotes）。オンなら Remote は選べない。</summary>
    [ObservableProperty] private bool _fetchAll;

    public FetchDialogViewModel(RepositoryViewModel repo)
    {
        _repo = repo;
        Remotes = repo.References?.Remotes.Select(r => r.Name).ToList() ?? [];
        SelectedRemote = UpstreamRemote(repo) ?? Remotes.FirstOrDefault();
        FetchAll = repo.Settings.Fetch_FetchAllRemotes;
    }

    public override bool CanSubmit => FetchAll || SelectedRemote is not null;
    partial void OnSelectedRemoteChanged(string? value) => RaiseCanSubmit();
    partial void OnFetchAllChanged(bool value) => RaiseCanSubmit();

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.Fetch_FetchAllRemotes = FetchAll;
        await _repo.RunGitAsync(FetchAll ? GitArgs.FetchAll() : GitArgs.Fetch(SelectedRemote!), Progress, ct);
    }

    /// <summary>現在のブランチの upstream のリモート名。</summary>
    internal static string? UpstreamRemote(RepositoryViewModel repo)
        => repo.References?.Head?.Upstream is { } up && up.StartsWith("refs/remotes/", StringComparison.Ordinal) ? up["refs/remotes/".Length..].Split('/', 2)[0] : null;
}

/// <summary>Pull。自前の autostash は（stash save → pull → stash pop --index）。</summary>
public sealed partial class PullDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public override string Title => "Pull";
    public override double DialogWidth => 520;
    public override string Subtitle => "Pull remote branches and merge them into your local branch";
    public override string SubmitText => "Pull";

    public IReadOnlyList<string> Remotes { get; }
    [ObservableProperty] private string? _selectedRemote;
    [ObservableProperty] private IReadOnlyList<string> _branches = [];
    [ObservableProperty] private string? _selectedBranch;
    /// <summary>"Into:" に出す現在のブランチ。</summary>
    public string Into { get; }
    [ObservableProperty] private bool _rebase;
    [ObservableProperty] private bool _stashAndReapply;

    public PullDialogViewModel(RepositoryViewModel repo)
    {
        _repo = repo;
        Remotes = repo.References?.Remotes.Select(r => r.Name).ToList() ?? [];
        Into = repo.References?.HeadBranch ?? "";
        Rebase = repo.Settings.Pull_Rebase;
        StashAndReapply = repo.Settings.Pull_StashAndReapply;
        SelectedRemote = FetchDialogViewModel.UpstreamRemote(repo) ?? Remotes.FirstOrDefault();
    }

    /// <summary>upstream（"origin/main"）。</summary>
    private string? Upstream => _repo.References?.Head?.Upstream is { } up ? up["refs/remotes/".Length..] : null;

    partial void OnSelectedRemoteChanged(string? value)
    {
        Branches = _repo.References?.RemoteBranches.Where(b => b.RemoteName == value).Select(b => b.ShortName).ToList() ?? [];
        SelectedBranch = Branches.Contains(Upstream ?? "") ? Upstream : Branches.FirstOrDefault(b => b == $"{value}/{Into}") ?? Branches.FirstOrDefault();
        RaiseCanSubmit();
    }

    partial void OnSelectedBranchChanged(string? value) => RaiseCanSubmit();

    public override bool CanSubmit => SelectedRemote is not null && SelectedBranch is not null && Into.Length > 0;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.Pull_Rebase = Rebase;
        _repo.Settings.Pull_StashAndReapply = StashAndReapply;
        var remote = SelectedRemote!;
        // upstream のブランチなら名前を渡さない
        string? branch = SelectedBranch == Upstream ? null : SelectedBranch![(remote.Length + 1)..];
        await RunMayConflictAsync(_repo, () => RunWithAutostashAsync(_repo, "Pull", StashAndReapply, () => _repo.RunGitAsync(GitArgs.Pull(remote, Rebase, branch), Progress, ct), ct));
    }

    /// <summary>
    /// 変更があれば自前の stash を作ってから action を行い、成功したら戻す。
    /// action が失敗したら stash は残す（変更を失わないため）。
    /// </summary>
    internal static async Task RunWithAutostashAsync(RepositoryViewModel repo, string operation, bool enabled, Func<Task> action, CancellationToken ct)
    {
        var stash = enabled && repo.HasLocalChanges;
        if (stash) await repo.RunGitAsync(GitArgs.StashSave(GitArgs.AutostashMessage(operation, DateTime.Now)), ct: ct);
        await action();
        if (stash) await repo.RunGitAsync(GitArgs.StashPop(0), ct: ct);
    }
}

/// <summary>Push。</summary>
/// <summary>
/// 複数のブランチの Push（サイドバーで複数選択 → "Push 2 branches to 'origin'..."）。
/// 一覧は "ff2 → origin/main"（upstream。無ければ同じ名前）だが、送るのは同じ名前：`push origin refs/heads/a refs/heads/b --set-upstream --atomic --verbose --progress`。
/// </summary>
public sealed partial class MultiPushDialogViewModel(RepositoryViewModel repo, string remote, IReadOnlyList<ReferenceInfo> branches) : DialogViewModel
{
    public override string Title => "Push";
    public override string Subtitle => $"Push {branches.Count} branches to remote repository";
    public override string SubmitText => $"Push {branches.Count} branches";
    public override double DialogWidth => 520;
    public IReadOnlyList<string> BranchNames { get; } = branches
        .Select(b => $"{b.ShortName}  →  {(b.Upstream is { } up ? up.Replace("refs/remotes/", "") : $"{remote}/{b.ShortName}")}").ToList();

    public override Task SubmitAsync(CancellationToken ct)
        => repo.RunGitAsync(["push", remote, .. branches.Select(b => b.CanonicalName), "--set-upstream", "--atomic", "--verbose", "--progress"], Progress, ct);
}

public sealed partial class PushDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public override string Title => "Push";
    public override string Subtitle => "Push your local changes to remote repository";
    public override string SubmitText => "Push";

    public IReadOnlyList<string> Branches { get; }
    [ObservableProperty] private string? _selectedBranch;
    public IReadOnlyList<string> Remotes { get; }
    /// <summary>リモートが 1 つなら Remote の行は出さない。</summary>
    public bool ShowRemotes => Remotes.Count > 1;
    [ObservableProperty] private string? _selectedRemote;
    /// <summary>"To:" の候補（リモートのブランチ名。先頭は既定の行き先）。</summary>
    [ObservableProperty] private IReadOnlyList<string> _targets = [];
    [ObservableProperty] private string? _selectedTarget;
    /// <summary>"default (origin/main)" / "new (origin/topic)"。既定の行き先を選んでいるときだけ。</summary>
    [ObservableProperty] private string? _targetDisplay;
    [ObservableProperty] private bool _isNewBranch;
    [ObservableProperty] private bool _createTracking = true;
    [ObservableProperty] private bool _allTags;
    [ObservableProperty] private bool _force;

    public PushDialogViewModel(RepositoryViewModel repo)
    {
        _repo = repo;
        Branches = repo.References?.LocalBranches.Select(b => b.ShortName).ToList() ?? [];
        Remotes = repo.References?.Remotes.Select(r => r.Name).ToList() ?? [];
        AllTags = repo.Settings.Push_PushAllTags;
        SelectedRemote = FetchDialogViewModel.UpstreamRemote(repo) ?? Remotes.FirstOrDefault();
        SelectedBranch = repo.References?.HeadBranch ?? Branches.FirstOrDefault();
    }

    private ReferenceInfo? Local => _repo.References?.LocalBranches.FirstOrDefault(b => b.ShortName == SelectedBranch);

    partial void OnSelectedBranchChanged(string? value) => UpdateTargets();
    partial void OnSelectedRemoteChanged(string? value) => UpdateTargets();

    /// <summary>既定の行き先：upstream がそのリモートにあればそれ、無ければ同じ名前の新しいブランチ。</summary>
    private string? DefaultTarget
    {
        get
        {
            if (SelectedBranch is null || SelectedRemote is null) return null;
            if (Local?.Upstream is { } up && up.StartsWith($"refs/remotes/{SelectedRemote}/", StringComparison.Ordinal)) return up[$"refs/remotes/{SelectedRemote}/".Length..];
            return SelectedBranch;
        }
    }

    private void UpdateTargets()
    {
        var remoteBranches = _repo.References?.RemoteBranches.Where(b => b.RemoteName == SelectedRemote).Select(b => b.ShortName[(SelectedRemote!.Length + 1)..]).ToList() ?? [];
        var def = DefaultTarget;
        Targets = def is null ? remoteBranches : [def, .. remoteBranches.Where(b => b != def)];
        SelectedTarget = def;
        UpdateTargetDisplay();
        RaiseCanSubmit();
    }

    partial void OnSelectedTargetChanged(string? value) => UpdateTargetDisplay();

    private void UpdateTargetDisplay()
    {
        var exists = _repo.References?.RemoteBranches.Any(b => b.ShortName == $"{SelectedRemote}/{SelectedTarget}") == true;
        IsNewBranch = SelectedTarget is not null && !exists;
        TargetDisplay = SelectedTarget is null ? null
            : SelectedTarget == DefaultTarget ? $"{(IsNewBranch ? "new" : "default")} ({SelectedRemote}/{SelectedTarget})"
            : $"{SelectedRemote}/{SelectedTarget}";
    }

    public override bool CanSubmit => SelectedBranch is not null && SelectedRemote is not null && SelectedTarget is not null;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.Push_PushAllTags = AllTags;
        var args = GitArgs.Push(SelectedRemote!, SelectedBranch!, setUpstream: IsNewBranch && CreateTracking, Force, AllTags, SelectedTarget);
        await _repo.RunGitAsync(args, Progress, ct);
    }
}

/// <summary>
/// Add Remote / Edit Remote（サイドバーのリモートの右クリック）。
/// 追加："Add Remote" / "Add new remote repository reference"、RemoteNameTextBox（最初は "origin"）、RepositoryUrlTextBox、SyncCheckBox、Submit "Add New Remote" → `remote add <name> <url>`。
/// 編集："Edit Remote" / "Edit URL of the remote repository"、Submit "Edit" → `remote set-url <古い名前> <url>` → `remote rename <古い名前> <新しい名前>`（変えたものだけ）。
/// 名前が空・既にある、URL が空、何も変えていないときは押せない。HTTPS / SSH の切り替えは未実装。
/// </summary>
public sealed partial class RemoteEditDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private readonly RemoteInfo? _original;

    public RemoteEditDialogViewModel(RepositoryViewModel repo, RemoteInfo? original)
    {
        _repo = repo;
        _original = original;
        _name = original?.Name ?? "origin";
        _url = original?.FetchUrl ?? "";
    }

    public bool IsNew => _original is null;
    public override string Title => IsNew ? "Add Remote" : "Edit Remote";
    public override string Subtitle => IsNew ? "Add new remote repository reference" : "Edit URL of the remote repository";
    public override string SubmitText => IsNew ? "Add New Remote" : "Edit";
    public override double DialogWidth => 630;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _name;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _url;
    [ObservableProperty] private bool _syncSubmoduleUrl = true;
    [ObservableProperty] private string _connectionStatus = "";

    public override bool CanSubmit
    {
        get
        {
            var name = Name.Trim();
            if (name.Length == 0 || Url.Trim().Length == 0 || name.Contains(' ')) return false;
            var taken = _repo.References?.Remotes.Any(r => r.Name == name && r.Name != _original?.Name) == true;
            if (taken) return false;
            return IsNew || name != _original!.Name || Url.Trim() != _original.FetchUrl;
        }
    }

    public async Task TestConnectionAsync()
    {
        ConnectionStatus = "Testing...";
        try
        {
            await _repo.RunGitAsync(["ls-remote", "--heads", Url.Trim()]);
            ConnectionStatus = "Connection successful";
        }
        catch (Core.Git.GitException ex) { ConnectionStatus = ex.Result.StandardError.Trim().Split('\n')[0]; }
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var name = Name.Trim();
        var url = Url.Trim();
        if (_original is null)
        {
            await _repo.RunGitAsync(["remote", "add", name, url], ct: ct);
            return;
        }
        if (url != _original.FetchUrl) await _repo.RunGitAsync(["remote", "set-url", _original.Name, url], ct: ct);
        if (name != _original.Name) await _repo.RunGitAsync(["remote", "rename", _original.Name, name], ct: ct);
    }
}

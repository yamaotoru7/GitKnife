using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Git.Model;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>チェックアウトするときの作業ツリーの変更の扱い（Create Branch / Checkout の "Local changes:"）。</summary>
public enum LocalChangesMode { DontChange, StashAndReapply, Discard }

/// <summary>起点（ブランチ・タグ・コミット）。表示名と git に渡す参照。</summary>
public sealed record RevisionTarget(string Display, string Ref)
{
    /// <summary>merge などに渡す名前（ブランチ・タグは短い名前、コミットは SHA）。</summary>
    public string Name => Ref.StartsWith("refs/", StringComparison.Ordinal) ? Display : Ref;
    public static RevisionTarget Branch(string name) => new(name, $"refs/heads/{name}");
    public static RevisionTarget Tag(string name) => new(name, $"refs/tags/{name}");
    public static RevisionTarget Remote(string shortName) => new(shortName, $"refs/remotes/{shortName}");
    public static RevisionTarget Commit(CommitInfo c) => new(c.ShortSha, c.Sha);
}

/// <summary>ブランチ名として使えるか（git check-ref-format の主な規則）。</summary>
internal static class RefName
{
    public static string? Problem(string name, RepositoryViewModel repo, string? exceptExisting = null)
    {
        if (name.Length == 0) return "";
        if (name.Any(c => char.IsWhiteSpace(c) || c is '~' or '^' or ':' or '?' or '*' or '[' or '\\') || name.Contains("..") || name.Contains("@{")
            || name.StartsWith('/') || name.EndsWith('/') || name.EndsWith('.') || name.EndsWith(".lock", StringComparison.Ordinal) || name.StartsWith('-'))
            return "Invalid branch name";
        if (name != exceptExisting && repo.References?.LocalBranches.Any(b => b.ShortName == name) == true) return "Branch already exists";
        return null;
    }
}

/// <summary>Create Branch。</summary>
public sealed partial class CreateBranchDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public RevisionTarget Start { get; }
    public override string Title => "Create Branch";
    public override double DialogWidth => 600;
    public override string Subtitle => "Use '/' as a path separator to create folders";
    public override string SubmitText => Checkout ? "Create and Checkout" : "Create";

    [ObservableProperty] private string _branchName = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SubmitText), nameof(ShowLocalChanges))] private bool _checkout = true;
    [ObservableProperty] private LocalChangesMode _localChanges = LocalChangesMode.DontChange;
    /// <summary>"Local changes:" の 3 択は、チェックアウトするときで作業ツリーに変更があるときだけ（変更の無い BPM では出なかった）。</summary>
    public bool ShowLocalChanges => Checkout && _repo.HasLocalChanges;

    public CreateBranchDialogViewModel(RepositoryViewModel repo, RevisionTarget start)
    {
        _repo = repo;
        Start = start;
        Checkout = repo.Settings.CreateBranch_Checkout;
    }

    partial void OnBranchNameChanged(string value)
    {
        var p = RefName.Problem(value.Trim(), _repo);
        StatusMessage = string.IsNullOrEmpty(p) ? null : p;
        StatusIsWarning = !string.IsNullOrEmpty(p);
        RaiseCanSubmit();
    }

    public override bool CanSubmit => RefName.Problem(BranchName.Trim(), _repo) is null;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.CreateBranch_Checkout = Checkout;
        var args = GitArgs.CreateBranchFrom(BranchName.Trim(), Start.Ref, Checkout);
        if (!Checkout) { await _repo.RunGitAsync(args, ct: ct); return; }
        await CheckoutDialogViewModel.RunWithLocalChanges(_repo, LocalChanges, "Checkout", args, Progress, ct);
    }
}

/// <summary>
/// Checkout。リモートブランチは同名のローカルがあればそれに、無ければ追跡ブランチを作って切り替える（"Track Remote Branch"）。
/// </summary>
public sealed partial class CheckoutDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private readonly string[] _args;
    public override string Title { get; }
    public override double DialogWidth => Title == "Track Remote Branch" ? 540 : 500;
    public override string Subtitle => "Switch to another branch";
    public override string SubmitText => "Checkout";
    public string SwitchTo { get; }
    [ObservableProperty] private LocalChangesMode _localChanges = LocalChangesMode.DontChange;

    public CheckoutDialogViewModel(RepositoryViewModel repo, ReferenceInfo branch)
    {
        _repo = repo;
        if (branch.Kind == ReferenceKind.RemoteBranch)
        {
            var local = branch.ShortName[(branch.RemoteName!.Length + 1)..];
            if (repo.References?.LocalBranches.Any(b => b.ShortName == local) == true)
            {
                Title = "Checkout Branch";
                _args = GitArgs.Checkout(local);
            }
            else
            {
                Title = "Track Remote Branch";
                _args = GitArgs.CheckoutTrack(local, branch.ShortName);
            }
            SwitchTo = local;
        }
        else
        {
            Title = "Checkout Branch";
            SwitchTo = branch.ShortName;
            _args = GitArgs.Checkout(branch.ShortName);
        }
        LocalChanges = repo.Settings.Checkout_StashAndReapply ? LocalChangesMode.StashAndReapply : LocalChangesMode.DontChange;
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.Checkout_StashAndReapply = LocalChanges == LocalChangesMode.StashAndReapply;
        await RunWithLocalChanges(_repo, LocalChanges, "Checkout", _args, Progress, ct);
    }

    /// <summary>
    /// 変更の扱いに従ってチェックアウトする。StashAndReapply は自前の stash（Pull / Rebase と同じ手順）、
    /// Discard は `reset --hard` してから。
    /// </summary>
    internal static async Task RunWithLocalChanges(RepositoryViewModel repo, LocalChangesMode mode, string operation, string[] args, Action<string> progress, CancellationToken ct)
    {
        if (mode == LocalChangesMode.Discard && repo.HasLocalChanges) await repo.RunGitAsync(["reset", "--hard", "--quiet"], ct: ct);
        await PullDialogViewModel.RunWithAutostashAsync(repo, operation, mode == LocalChangesMode.StashAndReapply, () => repo.RunGitAsync(args, progress, ct), ct);
    }
}

/// <summary>Quick checkout（Ctrl+B）。ローカルとリモートのブランチから選ぶ。リモートは同名のローカルがあればそれ、無ければ追跡ブランチを作る。</summary>
public sealed partial class QuickCheckoutDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public override string Title => "Checkout Branch";
    public override double DialogWidth => 500;
    public override string Subtitle => "Type to filter branches";
    public override string SubmitText => "Checkout";
    public IReadOnlyList<string> Branches { get; }
    [ObservableProperty] private string? _selectedBranch;

    public QuickCheckoutDialogViewModel(RepositoryViewModel repo)
    {
        _repo = repo;
        var refs = repo.References;
        Branches = (refs?.LocalBranches.Where(b => !b.IsHead).Select(b => b.ShortName) ?? [])
            .Concat(refs?.RemoteBranches.Select(b => b.ShortName) ?? []).ToList();
        SelectedBranch = Branches.FirstOrDefault();
    }

    partial void OnSelectedBranchChanged(string? value) => RaiseCanSubmit();
    public override bool CanSubmit => SelectedBranch is not null;

    public override Task SubmitAsync(CancellationToken ct)
    {
        var refs = _repo.References!;
        if (refs.LocalBranches.FirstOrDefault(b => b.ShortName == SelectedBranch) is { } local)
            return _repo.RunGitAsync(GitArgs.Checkout(local.ShortName), Progress, ct);
        var remote = refs.RemoteBranches.First(b => b.ShortName == SelectedBranch);
        var name = remote.ShortName[(remote.RemoteName!.Length + 1)..];
        return _repo.RunGitAsync(refs.LocalBranches.Any(b => b.ShortName == name) ? GitArgs.Checkout(name) : GitArgs.CheckoutTrack(name, remote.ShortName), Progress, ct);
    }
}

/// <summary>Create Tag。メッセージは一時ファイルで渡す。</summary>
public sealed partial class CreateTagDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public RevisionTarget Target { get; }
    public override string Title => "Create Tag";
    public override double DialogWidth => 590;
    public override string Subtitle => "Create annotated tag at the selected point";
    public override string SubmitText => "Create";
    [ObservableProperty] private string _tagName = "";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _push;

    public CreateTagDialogViewModel(RepositoryViewModel repo, RevisionTarget target)
    {
        _repo = repo;
        Target = target;
        Push = repo.Settings.CreateTag_Push;
    }

    partial void OnTagNameChanged(string value)
    {
        var exists = _repo.References?.Tags.Any(t => t.ShortName == value.Trim()) == true;
        StatusMessage = exists ? "Tag already exists" : null;
        StatusIsWarning = exists;
        RaiseCanSubmit();
    }

    public override bool CanSubmit => TagName.Trim().Length > 0 && !TagName.Any(char.IsWhiteSpace) && _repo.References?.Tags.Any(t => t.ShortName == TagName.Trim()) != true;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.CreateTag_Push = Push;
        var dir = Path.Combine(Path.GetTempPath(), "GitKnife");
        Core.Git.HostDirectory.CreateDirectory(dir);
        var file = Path.Combine(dir, "gitknife-tagMessage.txt");
        await Core.Git.HostFile.WriteAllTextAsync(file, Message, new UTF8Encoding(false), ct);
        await _repo.RunGitAsync(GitArgs.CreateTagAt(TagName.Trim(), Target.Ref, file), ct: ct);
        if (!Push) return;
        var remotes = _repo.References?.Remotes ?? [];
        // リモートが複数なら全部へ（1 つずつ push する）
        if (remotes.Count > 1)
            foreach (var r in remotes) await _repo.RunGitAsync(GitArgs.PushTag(r.Name, TagName.Trim()), Progress, ct);
        else if (remotes.FirstOrDefault() is { } remote)
            await _repo.RunGitAsync(GitArgs.PushTag(FetchDialogViewModel.UpstreamRemote(_repo) ?? remote.Name, TagName.Trim()), Progress, ct);
    }

    /// <summary>"Push"。リモートが複数なら "Push to all remotes"（BPM）。</summary>
    public string PushText => _repo.References?.Remotes.Count > 1 ? "Push to all remotes" : "Push";
}

/// <summary>Delete Branch。常に --force（マージ済みかは見ない）。</summary>
public sealed partial class DeleteBranchDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private readonly ReferenceInfo _branch;
    public override string Title => "Delete Branch";
    public override double DialogWidth => 520;
    public override string Subtitle => "Delete local branch from your repository";
    public override string SubmitText => "Delete";
    public string BranchName => _branch.ShortName;
    /// <summary>"Also delete remote branch" の横の upstream 名。</summary>
    public string? UpstreamName => _branch.Upstream?.Replace("refs/remotes/", "");
    public bool HasUpstream => UpstreamName is not null && !_branch.UpstreamGone;
    [ObservableProperty] private bool _deleteRemote;

    /// <summary>別の worktree でチェックアウトしているブランチ（"Also delete worktree"）。</summary>
    public string? WorktreePath { get; }
    public bool HasWorktree => WorktreePath is not null;
    /// <summary>チェックの横のフォルダ名（"basic-wt"）。</summary>
    public string WorktreeName => WorktreePath is { } p ? System.IO.Path.GetFileName(p.TrimEnd('\\')) : "";
    /// <summary>既定はオフ。</summary>
    [ObservableProperty] private bool _deleteWorktree;

    public DeleteBranchDialogViewModel(RepositoryViewModel repo, ReferenceInfo branch)
    {
        _repo = repo;
        _branch = branch;
        // for-each-ref の worktreepath は "/" 区切り。"\" 区切りにして worktree remove に渡す
        if (branch.WorktreePath is { } wt)
        {
            var path = wt.Replace('/', '\\');
            if (!string.Equals(System.IO.Path.GetFullPath(path).TrimEnd('\\'), System.IO.Path.GetFullPath(repo.WorkingDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                WorktreePath = path;
        }
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        // worktree を先に消してからブランチを消す
        if (DeleteWorktree && WorktreePath is { } wt) await _repo.RunGitAsync(["worktree", "remove", "--force", wt], ct: ct);
        await _repo.RunGitAsync(GitArgs.DeleteBranch(_branch.ShortName), ct: ct);
        if (DeleteRemote && UpstreamName is { } up)
        {
            var parts = up.Split('/', 2);
            await _repo.RunGitAsync(GitArgs.DeleteRemoteBranch(parts[0], parts[1]), Progress, ct);
        }
    }
}

/// <summary>
/// Delete Branches（複数選択の "Delete 2 Branches..."。`branch --delete --force a b`）。
/// "Also delete corresponding remote branches" は upstream が残っているブランチが無いと押せない（リモート側の消し方は単体の Delete と同じにしている＝推定）。
/// </summary>
public sealed partial class DeleteBranchesDialogViewModel(RepositoryViewModel repo, IReadOnlyList<ReferenceInfo> branches) : DialogViewModel
{
    public override string Title => "Delete Branches";
    public override string Subtitle => "Delete local branches from your repository";
    public override string SubmitText => $"Delete {branches.Count} branches";
    public override double DialogWidth => 570;
    public IReadOnlyList<string> BranchNames { get; } = branches.Select(b => b.ShortName).ToList();
    public bool HasUpstreams => branches.Any(b => b.Upstream is not null && !b.UpstreamGone);
    [ObservableProperty] private bool _deleteRemote;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        await repo.RunGitAsync(["branch", "--delete", "--force", .. BranchNames], ct: ct);
        if (!DeleteRemote) return;
        foreach (var b in branches.Where(b => b.Upstream is not null && !b.UpstreamGone))
        {
            var parts = b.Upstream!.Replace("refs/remotes/", "").Split('/', 2);
            await repo.RunGitAsync(GitArgs.DeleteRemoteBranch(parts[0], parts[1]), Progress, ct);
        }
    }
}

/// <summary>
/// Add Submodule（Submodules の右クリック > Add New Submodule...）。
/// "Repository URL:"、"Folder:"（自動では入らない）、"Fetch nested submodules"。`submodule add --force --progress &lt;url&gt; &lt;folder&gt;`。
/// Fetch nested submodules を付けたときは続けて `submodule update --init --recursive --progress -- &lt;folder&gt;`（推定）。
/// </summary>
public sealed partial class AddSubmoduleDialogViewModel(RepositoryViewModel repo) : DialogViewModel
{
    public override string Title => "Add Submodule";
    public override string Subtitle => "Add new submodule repository reference";
    public override string SubmitText => "Add Submodule";
    public override double DialogWidth => 590;
    [ObservableProperty] private string _url = "";
    [ObservableProperty] private string _folder = "";
    [ObservableProperty] private bool _fetchNested;

    partial void OnUrlChanged(string value) => RaiseCanSubmit();
    partial void OnFolderChanged(string value) => RaiseCanSubmit();

    public override bool CanSubmit => Url.Trim().Length > 0 && Folder.Trim().Length > 0;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var folder = Folder.Trim().Replace('\\', '/');
        await repo.RunGitAsync(["submodule", "add", "--force", "--progress", Url.Trim(), folder], Progress, ct);
        // Fetch nested submodules：入れ子が無い sublib では submodule add だけ
        if (!FetchNested) return;
        var nested = await repo.Git.RunAsync(new Core.Git.GitRequest($"{repo.Path}/{folder}", ["ls-files", "--", ".gitmodules"]), ct);
        if (nested.Succeeded && nested.StandardOutput.Trim().Length > 0)
            await repo.RunGitAsync(["submodule", "update", "--init", "--recursive", "--progress", "--", folder], Progress, ct);
    }
}

/// <summary>Rename Local Branch。同じ名前のままだと押せない。</summary>
public sealed partial class RenameBranchDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private readonly string _old;
    public override string Title => "Rename Local Branch";
    public override double DialogWidth => 500;
    public override string Subtitle => "Rename local branch";
    public override string SubmitText => "Rename";
    [ObservableProperty] private string _branchName;

    public RenameBranchDialogViewModel(RepositoryViewModel repo, ReferenceInfo branch)
    {
        _repo = repo;
        _old = branch.ShortName;
        _branchName = branch.ShortName;
        _upstream = branch.Upstream is { } up ? repo.References?.RemoteBranches.FirstOrDefault(r => r.CanonicalName == up) : null;
    }

    private readonly ReferenceInfo? _upstream;

    /// <summary>"Also rename origin/x"（RenameRemoteBranchCheckbox。upstream があるときだけ。既定はオフ）。</summary>
    [ObservableProperty] private bool _renameRemote;
    public bool HasUpstream => _upstream is not null;
    public string RenameRemoteText => _upstream is { } u ? $"Also rename {u.ShortName}" : "";

    partial void OnBranchNameChanged(string value)
    {
        var p = RefName.Problem(value.Trim(), _repo, _old);
        StatusMessage = string.IsNullOrEmpty(p) ? null : p;
        StatusIsWarning = !string.IsNullOrEmpty(p);
        RaiseCanSubmit();
    }

    public override bool CanSubmit => BranchName.Trim() != _old && RefName.Problem(BranchName.Trim(), _repo, _old) is null;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var name = BranchName.Trim();
        await _repo.RunGitAsync(GitArgs.RenameBranch(_old, name), ct: ct);
        if (!RenameRemote || _upstream is not { } up) return;
        // リモートのブランチ名（"origin/bugfix/greeting" → remote "origin"、名前 "bugfix/greeting"）。新しい名前はローカルと同じ
        var slash = up.ShortName.IndexOf('/');
        var remote = up.ShortName[..slash];
        var oldRemoteName = up.ShortName[(slash + 1)..];
        await _repo.RunGitAsync(GitArgs.RenameRemoteBranch(remote, up.TargetSha, oldRemoteName, name), ct: ct);
        await _repo.RunGitAsync(GitArgs.SetUpstream($"refs/remotes/{remote}/{name}", name), ct: ct);
    }
}

/// <summary>Delete Tag。</summary>
public sealed partial class DeleteTagDialogViewModel(RepositoryViewModel repo, string tag) : DialogViewModel
{
    public override string Title => "Delete Tag";
    public override double DialogWidth => 520;
    public override string Subtitle => "Delete tag from your repository";
    public override string SubmitText => "Delete";
    public string TagName => tag;
    [ObservableProperty] private bool _deleteFromRemotes;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        await repo.RunGitAsync(GitArgs.DeleteTag(tag), ct: ct);
        if (DeleteFromRemotes)
            foreach (var r in repo.References?.Remotes ?? [])
                await repo.RunGitAsync(GitArgs.DeleteRemoteTag(r.Name, tag), Progress, ct);
    }
}

/// <summary>Save Stash。"Stage new files" なら untracked を add してから。</summary>
public sealed partial class SaveStashDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public override string Title => "Save stash";
    public override double DialogWidth => 590;
    public override string Subtitle => "Save your local changes to a new stash";
    public override string SubmitText => "Save Stash";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _stageNewFiles;

    public SaveStashDialogViewModel(RepositoryViewModel repo)
    {
        _repo = repo;
        StageNewFiles = repo.Settings.SaveStash_StageNewFiles;
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.SaveStash_StageNewFiles = StageNewFiles;
        if (StageNewFiles)
        {
            var status = await _repo.RunGitAsync(GitArgs.StatusUntracked(), ct: ct);
            var untracked = status.StandardOutput.Split('\0').Where(e => e.StartsWith("?? ", StringComparison.Ordinal)).Select(e => e[3..]).ToList();
            if (untracked.Count > 0) await _repo.WorkingTree!.StageAsync(untracked, ct);
        }
        await _repo.RunGitAsync(GitArgs.StashSave(Message.Trim()), ct: ct);
    }
}

/// <summary>Save stash の一覧の 1 行（チェックボックス、アイコン、パス）。</summary>
public sealed partial class PartialStashFile(string path, bool isChecked) : ObservableObject
{
    public string Path { get; } = path;
    [ObservableProperty] private bool _isChecked = isChecked;
}

/// <summary>
/// ファイルの右クリック > Stash N File(s)...。題名 "Save stash"、変更のある全ファイルを PartialStashListBox に並べ、
/// 右クリックしたファイルにチェック。StashMessageTextBox（"Stash message (optional)"）。`git stash push -- &lt;paths&gt;`。
/// </summary>
public sealed partial class PartialStashDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public override string Title => "Save stash";
    public override string Subtitle => "Save your local modifications to a new stash. BOTH staged and unstaged changes will be stashed";
    public override string SubmitText => "Save Stash";
    public override double DialogWidth => 590;
    [ObservableProperty] private string _message = "";
    public IReadOnlyList<PartialStashFile> Files { get; }

    public PartialStashDialogViewModel(RepositoryViewModel repo, IEnumerable<string> allPaths, IEnumerable<string> selected)
    {
        _repo = repo;
        var set = selected.ToHashSet();
        Files = allPaths.Distinct().Select(p => new PartialStashFile(p, set.Contains(p))).ToList();
        foreach (var f in Files) f.PropertyChanged += (_, _) => RaiseCanSubmit();
    }

    public override bool CanSubmit => Files.Any(f => f.IsChecked);

    public override Task SubmitAsync(CancellationToken ct)
        => _repo.RunGitAsync(GitArgs.StashPushPaths(Message.Trim(), Files.Where(f => f.IsChecked).Select(f => f.Path)), ct: ct);
}

/// <summary>Apply Stash。削除オンなら pop、オフなら apply（どちらも --index）。</summary>
public sealed partial class ApplyStashDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private readonly StashInfo _stash;
    public override string Title => "Apply Stash";
    public override double DialogWidth => 540;
    public override string Subtitle => "Apply stashed changes to the working directory";
    public override string SubmitText => "Apply";
    public string StashText => $"{_stash.RefName}: {_stash.Message}";
    [ObservableProperty] private bool _deleteAfterApply;

    public ApplyStashDialogViewModel(RepositoryViewModel repo, StashInfo stash)
    {
        _repo = repo;
        _stash = stash;
        DeleteAfterApply = repo.Settings.ApplyStash_DeleteAfterApply;
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.ApplyStash_DeleteAfterApply = DeleteAfterApply;
        await RunMayConflictAsync(_repo, () => _repo.RunGitAsync(DeleteAfterApply ? GitArgs.StashPop(_stash.Index) : GitArgs.StashApply(_stash.Index), ct: ct));
    }
}

/// <summary>stash の Delete（確認だけ）。</summary>
public sealed class DeleteStashDialogViewModel(RepositoryViewModel repo, StashInfo stash) : DialogViewModel
{
    public override string Title => "Delete Stash";
    public override string Subtitle => "Delete stashed changes";
    public override string SubmitText => "Delete";
    public string StashText => $"{stash.RefName}: {stash.Message}";
    public override Task SubmitAsync(CancellationToken ct) => repo.RunGitAsync(GitArgs.StashDrop(stash.Index), ct: ct);
}

/// <summary>
/// Create Worktree（Branch ▼ > New Worktree...、ブランチの Checkout as Worktree...）。
/// "Create branch and checkout it in a separate worktree"、Start from（toggleButton、既定は今のブランチ）、BranchNameTextBox、PathTextBox（既定は
/// "&lt;リポジトリ&gt;-worktrees\&lt;ブランチ名&gt;"。自分で変えるまでブランチ名に合わせる）、Submit "Create"。
/// `worktree add -b &lt;branch&gt; &lt;path（/ 区切り）&gt; &lt;起点の sha&gt;`。作ったら新しいタブで開く（タブ名は "basic: wt-test" の形）。
/// </summary>
public sealed partial class CreateWorktreeDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private bool _pathEdited;
    private bool _settingPath;

    public CreateWorktreeDialogViewModel(RepositoryViewModel repo, string? startFrom = null, string? existingBranch = null, string? remoteBranch = null)
    {
        _repo = repo;
        RemoteBranch = remoteBranch;
        ExistingBranch = remoteBranch is null ? existingBranch : remoteBranch.Split('/', 2)[^1];
        Branches = repo.References?.LocalBranches.Select(b => b.ShortName).ToList() ?? [];
        _startFrom = startFrom ?? repo.References?.HeadBranch ?? Branches.FirstOrDefault();
        UpdatePath();
    }

    /// <summary>
    /// 既にあるブランチを別の worktree でチェックアウトする（ブランチの右クリック > Checkout as Worktree...）。
    /// "Checkout Branch as Worktree" / "Checkout branch in separate worktree"、"Local Branch:"、既定の場所は "-worktrees\feature-login"（/ は -）、`worktree add &lt;path&gt; &lt;branch&gt;`。
    /// </summary>
    public string? ExistingBranch { get; }
    public bool IsNewBranch => ExistingBranch is null;

    /// <summary>
    /// リモートブランチの Checkout as Worktree...："Checkout Remote Branch as Worktree" / "Checkout remote branch in separate worktree"、
    /// "Remote Branch:"、既定の場所は "-worktrees\bugfix-greeting"（リモート名を除いた名前）。同じ名前のローカルブランチがあれば `worktree add &lt;path&gt; bugfix/greeting`。
    /// </summary>
    public string? RemoteBranch { get; }

    public override string Title => RemoteBranch is not null ? "Checkout Remote Branch as Worktree" : IsNewBranch ? "Create Worktree" : "Checkout Branch as Worktree";
    public override string Subtitle => RemoteBranch is not null ? "Checkout remote branch in separate worktree"
        : IsNewBranch ? "Create branch and checkout it in a separate worktree" : "Checkout branch in separate worktree";
    public override string SubmitText => "Create";
    public override double DialogWidth => 590;
    public IReadOnlyList<string> Branches { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string? _startFrom;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _branchName = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _path = "";

    public override bool CanSubmit => (IsNewBranch ? StartFrom is not null && RefName.Problem(BranchName.Trim(), _repo) is null : true)
                                      && Path.Trim().Length > 0 && !Core.Git.HostDirectory.Exists(Path.Trim());

    partial void OnBranchNameChanged(string value) => UpdatePath();
    partial void OnPathChanged(string value) { if (!_settingPath) _pathEdited = true; }

    private void UpdatePath()
    {
        if (_pathEdited) return;
        var root = _repo.Path.TrimEnd('\\', '/');
        _settingPath = true;
        var leaf = IsNewBranch ? BranchName.Trim() : ExistingBranch!.Replace('/', '-');
        Path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(root) ?? root, System.IO.Path.GetFileName(root) + "-worktrees", leaf);
        _settingPath = false;
    }

    /// <summary>作った worktree のフォルダ（閉じた後でタブを開く）。</summary>
    public string CreatedPath => Path.Trim();

    public override async Task SubmitAsync(CancellationToken ct)
    {
        if (RemoteBranch is not null && _repo.References?.LocalBranches.Any(b => b.ShortName == ExistingBranch) != true)
        {
            // ローカルブランチが無いときは追跡ブランチを作る（推定）
            await _repo.RunGitAsync(["worktree", "add", "--track", "-b", ExistingBranch!, Path.Trim().Replace('\\', '/'), RemoteBranch], Progress, ct);
            return;
        }
        if (!IsNewBranch)
        {
            await _repo.RunGitAsync(["worktree", "add", Path.Trim().Replace('\\', '/'), ExistingBranch!], Progress, ct);
            return;
        }
        var start = _repo.References?.LocalBranches.FirstOrDefault(b => b.ShortName == StartFrom)?.TargetSha ?? StartFrom!;
        await _repo.RunGitAsync(["worktree", "add", "-b", BranchName.Trim(), Path.Trim().Replace('\\', '/'), start], Progress, ct);
    }
}

/// <summary>
/// Branch ▼ > Start Branch on 'main'...（Lean Branching）。"Start Branch" / "Use '/' as a path separator to create folders"、
/// "Start branch on:" main、BranchNameTextBox（"Enter branch name"）、Local changes の 3 択、Submit "Create" → `checkout --no-track -b &lt;name&gt; refs/heads/main`。
/// </summary>
public sealed partial class StartBranchDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public StartBranchDialogViewModel(RepositoryViewModel repo, string mainBranch)
    {
        _repo = repo;
        MainBranch = mainBranch;
    }

    public override string Title => "Start Branch";

    public override double DialogWidth => 610;
    public override string Subtitle => "Use '/' as a path separator to create folders";
    public override string SubmitText => "Create";
    public string MainBranch { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _branchName = "";
    [ObservableProperty] private LocalChangesMode _localChanges = LocalChangesMode.DontChange;
    public override bool CanSubmit => RefName.Problem(BranchName.Trim(), _repo) is null;

    public override Task SubmitAsync(CancellationToken ct)
        => CheckoutDialogViewModel.RunWithLocalChanges(_repo, LocalChanges, "Checkout", GitArgs.CreateBranchFrom(BranchName.Trim(), $"refs/heads/{MainBranch}", true), Progress, ct);
}

/// <summary>
/// Stash ▼ > Save Snapshot...。"Save snapshot" / "Save your local changes to a new stash, but keep them in the working directory"、
/// StashMessageTextBox、Stage new files、Submit "Save Snapshot" → `stash create &lt;message&gt;` → `stash store &lt;sha&gt;`（作業ツリーはそのまま）。
/// </summary>
public sealed partial class SaveSnapshotDialogViewModel(RepositoryViewModel repo) : DialogViewModel
{
    public override string Title => "Save snapshot";
    public override double DialogWidth => 590;
    public override string Subtitle => "Save your local changes to a new stash, but keep them in the working directory";
    public override string SubmitText => "Save Snapshot";
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _stageNewFiles;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        if (StageNewFiles)
        {
            var status = await repo.RunGitAsync(GitArgs.StatusUntracked(), ct: ct);
            var untracked = status.StandardOutput.Split('\0').Where(e => e.StartsWith("?? ", StringComparison.Ordinal)).Select(e => e[3..]).ToList();
            if (untracked.Count > 0) await repo.WorkingTree!.StageAsync(untracked, ct);
        }
        string[] create = Message.Trim().Length > 0 ? ["stash", "create", Message.Trim()] : ["stash", "create"];
        var sha = (await repo.RunGitAsync(create, ct: ct)).StandardOutput.Trim();
        if (sha.Length == 0) return;   // 変更が無い
        await repo.RunGitAsync(["stash", "store", sha], ct: ct);
    }
}

/// <summary>
/// Lean Branching の Finish 'feature/login' (Merge into 'main')...。"Finish Branch" / "Finish 'feature/login' and merge it into 'main'"、
/// "Merge:" と "Into:"。main が origin より遅れていると "You must checkout and sync 'main' first" で押せない。
/// Finish → `checkout --progress main` → `merge feature/login --no-ff`（ブランチは消さない）。
/// </summary>
public sealed partial class FinishBranchDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public FinishBranchDialogViewModel(RepositoryViewModel repo, string branch, string main)
    {
        _repo = repo;
        Branch = branch;
        Main = main;
        var mainRef = repo.References?.LocalBranches.FirstOrDefault(b => b.ShortName == main);
        if (mainRef is null || mainRef.Behind > 0)
        {
            StatusIsWarning = true;
            StatusMessage = $"You must checkout and sync '{main}' first";
        }
    }

    public override string Title => "Finish Branch";
    public override string Subtitle => $"Finish '{Branch}' and merge it into '{Main}'";
    public override string SubmitText => "Finish";
    public override double DialogWidth => 610;
    public string Branch { get; }
    public string Main { get; }
    public override bool CanSubmit => !StatusIsWarning;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        await _repo.RunGitAsync(["checkout", "--progress", Main], Progress, ct);
        await RunMayConflictAsync(_repo, () => _repo.RunGitAsync(["merge", Branch, "--no-ff"], Progress, ct));
    }
}

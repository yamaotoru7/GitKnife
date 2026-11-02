using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Git.Model;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>コミット 1 つを対象にするダイアログの共通部分（"7cabd6e  Add changelog" の表示、幅 590）。</summary>
public abstract partial class CommitDialogViewModel(RepositoryViewModel repo, CommitInfo commit) : DialogViewModel
{
    protected RepositoryViewModel Repo { get; } = repo;
    public CommitInfo Commit { get; } = commit;
    public string CommitText => $"{Commit.ShortSha}  {Commit.Subject}";
    public string Branch => Repo.References?.HeadBranch ?? "HEAD";
    public override double DialogWidth => 590;

    /// <summary>
    /// 事前判定：merge-tree --write-tree で、base..commit の変更を HEAD に当てて競合するかを見る（開いたときに判定して文言を出す）。
    /// </summary>
    protected async Task PrecheckAsync(string @base, string theirs, string ok, string conflict)
    {
        try
        {
            await Repo.RunGitAsync(["merge-tree", "--write-tree", "--merge-base", @base, "HEAD", theirs]);
            StatusMessage = ok;
            StatusIsSuccess = true;
        }
        catch (Core.Git.GitException)
        {
            StatusMessage = conflict;
            StatusIsWarning = true;
        }
        catch { }
    }
}

/// <summary>Cherry Pick（`cherry-pick <sha>`）。</summary>
public sealed partial class CherryPickDialogViewModel : CommitDialogViewModel
{
    public override string Title => "Cherry Pick";
    public override double DialogWidth => 620;
    public override string Subtitle => "Apply changes of the individual commit";
    public override string SubmitText => "Cherry Pick";
    [ObservableProperty] private bool _commitChanges = true;
    [ObservableProperty] private bool _appendOrigin;

    public CherryPickDialogViewModel(RepositoryViewModel repo, CommitInfo commit) : base(repo, commit)
    {
        if (commit.Parents.Count > 0)
            _ = PrecheckAsync(commit.Parents[0], commit.Sha, "Cherry pick can be done without conflicts", "Cherry pick will cause conflicts");
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        // -x の位置（`cherry-pick -x <古い sha> <新しい sha>`）。--no-commit の位置は推定
        string[] args = ["cherry-pick", .. AppendOrigin ? new[] { "-x" } : [], .. CommitChanges ? [] : new[] { "--no-commit" }, Commit.Sha];
        await RunMayConflictAsync(Repo, () => Repo.RunGitAsync(args, Progress, ct));
    }
}

/// <summary>
/// 複数のコミットの Cherry Pick（2 つ選んで右クリック > Cherry-pick…）。"Commits to apply:" に一覧の上から並べ、
/// Submit は "Cherry Pick 2 commits"。git には古い順に渡す（`cherry-pick <古い> <新しい>`。順序とオプションの位置は推定）。
/// 複数のときは事前判定（"Cherry pick will cause conflicts"）をしない。
/// </summary>
public sealed partial class MultiCherryPickDialogViewModel(RepositoryViewModel repo, IReadOnlyList<CommitInfo> commitsTopFirst) : DialogViewModel
{
    public override string Title => "Cherry Pick";
    public override string Subtitle => "Apply changes of the individual commit";
    public override string SubmitText => $"Cherry Pick {commitsTopFirst.Count} commits";
    public override double DialogWidth => 620;
    public IReadOnlyList<string> Revisions { get; } = commitsTopFirst.Select(c => $"{c.ShortSha}  {c.Subject}").ToList();
    [ObservableProperty] private bool _commitChanges = true;
    [ObservableProperty] private bool _appendOrigin;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        string[] args = ["cherry-pick", .. AppendOrigin ? new[] { "-x" } : [], .. CommitChanges ? [] : new[] { "--no-commit" }, .. commitsTopFirst.Reverse().Select(c => c.Sha)];
        await RunMayConflictAsync(repo, () => repo.RunGitAsync(args, Progress, ct));
    }
}

/// <summary>Revert（`revert --no-edit <sha>`）。</summary>
public sealed partial class RevertDialogViewModel : CommitDialogViewModel
{
    public override string Title => "Revert";
    public override double DialogWidth => 590;
    public override string Subtitle => "Revert changes of the individual commit";
    public override string SubmitText => "Revert";
    [ObservableProperty] private bool _commitChanges = true;

    public RevertDialogViewModel(RepositoryViewModel repo, CommitInfo commit) : base(repo, commit)
    {
        if (commit.Parents.Count > 0)
            _ = PrecheckAsync(commit.Sha, commit.Parents[0], "Revert can be done without conflicts", "Revert will cause conflicts");
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        string[] args = CommitChanges ? ["revert", "--no-edit", Commit.Sha] : ["revert", "--no-commit", Commit.Sha];
        await RunMayConflictAsync(Repo, () => Repo.RunGitAsync(args, Progress, ct));
    }
}

/// <summary>Reset の種類（ResetTypeCombobox。Mixed の文言、Soft / Hard は推定）。</summary>
public sealed record ResetTypeItem(string Mode, string Name, string Description, string Color)
{
    public override string ToString() => $"{Name}  {Description}";
    public static readonly IReadOnlyList<ResetTypeItem> All =
    [
        new("--soft", "Soft", "Keep all changes. Stage differences", "#57C26B"),
        new("--mixed", "Mixed", "Keep all changes. Unstage differences", "#E8B83A"),
        new("--hard", "Hard", "Discard all changes", "#E5534B"),
    ];
}

/// <summary>Reset Current Branch to Revision（`reset --mixed <sha>`）。</summary>
public sealed partial class ResetDialogViewModel : CommitDialogViewModel
{
    public override string Title => "Reset Current Branch to Revision";
    public override double DialogWidth => 590;
    public override string Subtitle => $"Move the '{Branch}' branch HEAD to the selected revision";
    public override string SubmitText => "Reset";
    public IReadOnlyList<ResetTypeItem> Types => ResetTypeItem.All;
    [ObservableProperty] private ResetTypeItem _selectedType = ResetTypeItem.All[1];

    public ResetDialogViewModel(RepositoryViewModel repo, CommitInfo commit) : base(repo, commit) { }

    public override Task SubmitAsync(CancellationToken ct) => Repo.RunGitAsync(["reset", SelectedType.Mode, Commit.Sha], Progress, ct);
}

/// <summary>Checkout Commit（`checkout --progress <sha>`。detached HEAD になる）。</summary>
public sealed partial class CheckoutCommitDialogViewModel : CommitDialogViewModel
{
    public override string Title => "Checkout Commit";
    public override double DialogWidth => 590;
    public override string Subtitle => "Checkout a particular revision. Repository will be in detached HEAD state.";
    public override string SubmitText => "Checkout Commit";
    [ObservableProperty] private LocalChangesMode _localChanges = LocalChangesMode.DontChange;

    public CheckoutCommitDialogViewModel(RepositoryViewModel repo, CommitInfo commit) : base(repo, commit) { }

    public override Task SubmitAsync(CancellationToken ct)
        => CheckoutDialogViewModel.RunWithLocalChanges(Repo, LocalChanges, "Checkout", ["checkout", "--progress", Commit.Sha], Progress, ct);
}

public enum PatchSource { Range, Commit, Stash }

/// <summary>
/// "Create Patch" / "Save commit as patch"。Save で OS の保存ダイアログ（"Save patch as..."）→ git の出力をそのまま書く。
/// - 2 つ選んだコミット（"Revisions:"）：`format-patch --stdout &lt;古い方&gt;~..&lt;新しい方&gt;`、既定の名前 "basic-3b121ca-9401224.patch"
/// - コミット 1 つ（"Revision:"）：`format-patch --stdout --max-count=1 &lt;sha&gt;`、既定の名前 "basic-3b121ca-Document options"
/// - stash（"Revision:"）：`stash show -p --include-untracked &lt;sha&gt;`、既定の名前 "basic-b467ce1-WIP greeting experiment"（ファイル名に使えない文字は消す）
/// </summary>
public sealed partial class CreatePatchDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private readonly PatchSource _source;
    private readonly string _older;
    private readonly string _newer;
    private readonly string _subject;
    public override string Title => "Create Patch";
    public override double DialogWidth => 590;
    public override string Subtitle => "Save commit as patch";
    public override string SubmitText => "Save";
    public string RevisionsLabel => _source == PatchSource.Range ? "Revisions:" : "Revision:";
    public System.Collections.ObjectModel.ObservableCollection<string> Revisions { get; } = [];

    /// <summary>2 つ選んだコミットの範囲。</summary>
    public CreatePatchDialogViewModel(RepositoryViewModel repo, string older, string newer) : this(repo, PatchSource.Range, older, newer, "") { }

    /// <summary>コミット 1 つか stash（subject は一覧の件名。stash はサイドバーの名前）。</summary>
    public CreatePatchDialogViewModel(RepositoryViewModel repo, PatchSource source, string sha, string subject) : this(repo, source, sha, sha, subject) { }

    private CreatePatchDialogViewModel(RepositoryViewModel repo, PatchSource source, string older, string newer, string subject)
    {
        _repo = repo;
        _source = source;
        _older = older;
        _newer = newer;
        _subject = subject;
        if (source == PatchSource.Range) _ = LoadAsync();
        else Revisions.Add($"{newer[..7]}  {subject}");
    }

    private async Task LoadAsync()
    {
        try
        {
            var r = await _repo.RunGitAsync(["log", "--no-show-signature", "--format=%h  %s", $"{_older}~..{_newer}"]);
            foreach (var line in r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Revisions.Add(line.TrimEnd('\r'));
        }
        catch (Core.Git.GitException) { }
    }

    private string DefaultFileName => _source == PatchSource.Range
        ? $"{_repo.Name}-{_newer[..7]}-{_older[..7]}.patch"
        : $"{_repo.Name}-{_newer[..7]}-{new string(_subject.Where(ch => !Path.GetInvalidFileNameChars().Contains(ch)).ToArray())}";

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save patch as...",
            FileName = DefaultFileName,
            DefaultExt = ".patch",
            Filter = "Patches (*.patch)|*.patch",
            InitialDirectory = _repo.Settings.RecentPatchDirectory is { } d && Core.Git.HostDirectory.Exists(d) ? d : null,
        };
        if (dlg.ShowDialog() != true) throw new OperationCanceledException();
        _repo.Settings.RecentPatchDirectory = Path.GetDirectoryName(dlg.FileName);
        string[] args = _source switch
        {
            PatchSource.Range => GitArgs.FormatPatchRange(_older, _newer),
            PatchSource.Commit => ["format-patch", "--stdout", "--max-count=1", _newer],
            _ => ["stash", "show", "-p", "--include-untracked", _newer],
        };
        var r = await _repo.RunGitAsync(args, ct: ct);
        await Core.Git.HostFile.WriteAllTextAsync(dlg.FileName, r.StandardOutput, ct);
    }
}

/// <summary>
/// stash の Rename...。"Rename Stash" / "Update stash message"、StashNameTextBox（最初は今の名前）、Submit "Rename"（変えるまで押せない）。
/// git に stash の名前を変えるコマンドは無いので、作り直す：`show --no-patch` で木と親を読み → `commit-tree &lt;tree&gt; -p … -m &lt;名前&gt;`
/// → `stash store --message &lt;名前&gt; &lt;新しい sha&gt;` → `reflog --format=… stash` で古いものの位置を探して `stash drop stash@{n}`。
/// </summary>
public sealed partial class RenameStashDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private readonly StashInfo _stash;
    private readonly string _original;

    public RenameStashDialogViewModel(RepositoryViewModel repo, StashInfo stash, string currentName)
    {
        _repo = repo;
        _stash = stash;
        _original = currentName;
        _message = currentName;
    }

    public override string Title => "Rename Stash";

    public override double DialogWidth => 500;
    public override string Subtitle => "Update stash message";
    public override string SubmitText => "Rename";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _message;
    public override bool CanSubmit => Message.Trim().Length > 0 && Message.Trim() != _original;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        const char Sep = '\u001f';
        var message = Message.Trim();
        var info = (await _repo.RunGitAsync(["show", "--no-patch", $"--pretty=format:%H%x1f%T%x1f%P", _stash.RefName], ct: ct)).StandardOutput.Trim().Split(Sep);
        var oldSha = info[0];
        var tree = info[1];
        var parents = info.Length > 2 ? info[2].Split(' ', StringSplitOptions.RemoveEmptyEntries) : [];
        var newSha = (await _repo.RunGitAsync(["commit-tree", tree, .. parents.SelectMany(p => new[] { "-p", p }), "-m", message], ct: ct)).StandardOutput.Trim();
        await _repo.RunGitAsync(["stash", "store", "--message", message, newSha], ct: ct);
        // 新しいものが stash@{0} に入ったので、古いものの位置を探して消す
        var reflog = (await _repo.RunGitAsync(["reflog", "--format=%H%x1f%gD", "stash"], ct: ct)).StandardOutput;
        var old = reflog.Split('\n').Select(l => l.Trim().Split(Sep)).FirstOrDefault(f => f.Length == 2 && f[0] == oldSha);
        if (old is not null) await _repo.RunGitAsync(["stash", "drop", old[1]], ct: ct);
    }
}

/// <summary>
/// リモートブランチの Delete...。"Delete Branch" / "Delete branch from remote repository"、"Branch:" + origin/main。
/// `push &lt;remote&gt; --delete refs/heads/&lt;branch&gt;` → そのリモートブランチを追っていたローカルブランチは `branch --unset-upstream &lt;local&gt;`。
/// </summary>
public sealed partial class DeleteRemoteBranchDialogViewModel(RepositoryViewModel repo, ReferenceInfo remoteBranch) : DialogViewModel
{
    public override string Title => "Delete Branch";
    public override double DialogWidth => 520;
    public override string Subtitle => "Delete branch from remote repository";
    public override string SubmitText => "Delete";
    public string BranchText => remoteBranch.ShortName;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var remote = remoteBranch.RemoteName!;
        var branch = remoteBranch.ShortName[(remote.Length + 1)..];
        await repo.RunGitAsync(["push", remote, "--delete", $"refs/heads/{branch}"], Progress, ct);
        foreach (var local in repo.References?.References.Where(r => r.Kind == ReferenceKind.LocalBranch && r.Upstream == remoteBranch.CanonicalName) ?? [])
            await repo.RunGitAsync(["branch", "--unset-upstream", local.ShortName], ct: ct);
    }
}

/// <summary>
/// タグの Show Tag Details...。"Tag Details"、"Tag:" + 名前、"Message:" に
/// `for-each-ref --format=%(taggername) %(taggeremail) %(taggerdate)%0a%0a%(contents) refs/tags/&lt;tag&gt;` の出力。Close だけ。
/// </summary>
public sealed partial class TagDetailsDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public TagDetailsDialogViewModel(RepositoryViewModel repo, string tag)
    {
        _repo = repo;
        TagName = tag;
        _ = LoadAsync();
    }

    public override string Title => "Tag Details";
    public override string Subtitle => "";
    public override string SubmitText => "Close";
    public override bool ShowSubmit => false;
    public override string CancelText => "Close";
    public override double DialogWidth => 650;
    public string TagName { get; }
    [ObservableProperty] private string _message = "";

    private async Task LoadAsync()
    {
        try
        {
            var r = await _repo.RunGitAsync(["for-each-ref", "--format=%(taggername) %(taggeremail) %(taggerdate)%0a%0a%(contents)", $"refs/tags/{TagName}"]);
            Message = r.StandardOutput.TrimEnd();
        }
        catch (Core.Git.GitException) { }
    }

    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// Repository > Apply Patch…。OS のファイル選択（"Select patch"）の後に "Apply Patch" / "Apply Patch"、
/// LocationLabel + PathTextBox + BrowseButton、フッターに事前判定（`apply --check &lt;path&gt;` → "Patch can be applied without conflicts"）、Submit "Apply" → `apply --3way &lt;path&gt;`。
/// format-patch の形のパッチ（先頭が "From &lt;sha&gt;"）なら "Create commit"（CreateCommitsCheckBox、既定はオフ）を出し、オンなら `am --3way &lt;path&gt;`。
/// </summary>
public sealed partial class ApplyPatchDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private CancellationTokenSource? _check;

    public ApplyPatchDialogViewModel(RepositoryViewModel repo, string path)
    {
        _repo = repo;
        _path = path;
        _ = CheckAsync();
    }

    public override string Title => "Apply Patch";
    public override string Subtitle => "Apply Patch";
    public override string SubmitText => "Apply";
    public override double DialogWidth => 590;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _path;
    public override bool CanSubmit => Core.Git.HostFile.Exists(Path.Trim());

    [ObservableProperty] private bool _createCommit;
    [ObservableProperty] private bool _isMailPatch;

    partial void OnPathChanged(string value) => _ = CheckAsync();

    private static bool LooksLikeMailPatch(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            var first = reader.ReadLine() ?? "";
            return System.Text.RegularExpressions.Regex.IsMatch(first, "^From [0-9a-f]{40} ");
        }
        catch (IOException) { return false; }
    }

    private async Task CheckAsync()
    {
        _check?.Cancel();
        var cts = _check = new CancellationTokenSource();
        StatusMessage = null;
        IsMailPatch = Core.Git.HostFile.Exists(Path.Trim()) && LooksLikeMailPatch(Path.Trim());
        if (!IsMailPatch) CreateCommit = false;
        if (!Core.Git.HostFile.Exists(Path.Trim())) return;
        try
        {
            await Task.Delay(150, cts.Token);
            var r = await _repo.Git.RunAsync(new Core.Git.GitRequest(_repo.Path, ["apply", "--check", Path.Trim()]), cts.Token);
            if (cts.IsCancellationRequested) return;
            StatusIsWarning = !r.Succeeded;
            StatusIsSuccess = r.Succeeded;
            StatusMessage = r.Succeeded ? "Patch can be applied without conflicts" : r.StandardError.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim();
        }
        catch (OperationCanceledException) { }
    }

    public override async Task SubmitAsync(CancellationToken ct)
        => await RunMayConflictAsync(_repo, () => _repo.RunGitAsync([CreateCommit ? "am" : "apply", "--3way", Path.Trim()], Progress, ct));
}

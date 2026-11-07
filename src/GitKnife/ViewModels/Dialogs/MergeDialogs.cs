using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git.Commands;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Merge Option の選択肢：名前、説明、オプション。No Fast-Forward と Squash の間に無効の区切り（高さ 1px の項目）。
/// </summary>
public sealed record MergeOptionItem(MergeOption Value, string Name, string Description, string Flag, bool IsSeparator = false)
{
    public bool IsSelectable => !IsSeparator;
    public override string ToString() => Name;
    public static readonly IReadOnlyList<MergeOptionItem> All =
    [
        new(MergeOption.Default, "Default", "Fast-forward if possible", ""),
        new(MergeOption.NoFastForward, "No Fast-Forward", "Always create a merge commit", "--no-ff"),
        new((MergeOption)(-1), "", "", "", IsSeparator: true),
        new(MergeOption.Squash, "Squash", "Squash merge", "--squash"),
        new(MergeOption.DontCommit, "Don't Commit", "Merge without commit", "--no-commit"),
    ];
}

/// <summary>
/// Merge。開いたときに merge-base + merge-tree で競合を事前に調べ、"Merge will cause conflicts" を出す。
/// </summary>
public sealed partial class MergeDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public override string Title => "Merge Branch";
    public override double DialogWidth => 560;
    public override string Subtitle => "Merge branch into another one";
    public override string SubmitText => "Merge";
    public RevisionTarget Source { get; }
    public string Into { get; }
    public IReadOnlyList<MergeOptionItem> Options => MergeOptionItem.All;
    [ObservableProperty] private MergeOptionItem _selectedOption;

    public MergeDialogViewModel(RepositoryViewModel repo, RevisionTarget source)
    {
        _repo = repo;
        Source = source;
        Into = repo.References?.HeadBranch ?? "";
        _selectedOption = Options.FirstOrDefault(o => o.IsSelectable && (int)o.Value == repo.Settings.MergeType) ?? Options[1];
        _ = PrecheckAsync();
    }

    private async Task PrecheckAsync()
    {
        try
        {
            var name = Source.Name;
            var b = await _repo.RunGitAsync(GitArgs.MergeBase(name, Into));
            var tree = await _repo.RunGitAsync(GitArgs.MergeTree(b.StandardOutput.Trim(), name, Into));
            // 3 引数の merge-tree は競合するとき <<<<<<< を含む差分を出す
            if (tree.StandardOutput.Contains("+<<<<<<<", StringComparison.Ordinal))
            {
                StatusMessage = "Merge will cause conflicts";
                StatusIsWarning = true;
            }
            else
            {
                StatusMessage = "Merge can be done without conflicts";
                StatusIsSuccess = true;
            }
        }
        catch { /* 事前判定は参考なので、失敗しても実行はできる */ }
    }

    public override bool CanSubmit => Into.Length > 0;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.MergeType = (int)SelectedOption.Value;
        await RunMayConflictAsync(_repo, () => _repo.RunGitAsync(GitArgs.Merge(Source.Name, SelectedOption.Value), Progress, ct));
    }
}

/// <summary>
/// Rebase。開いたときに merge-base + replay で事前判定。実行は自前の autostash + `-c core.commentChar=^ rebase`。
/// </summary>
public sealed partial class RebaseDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    public override string Title => "Rebase";
    public override string Subtitle => "Copy commits from one branch to another";
    public override string SubmitText => "Rebase";
    public override double DialogWidth => 570;
    public string Branch { get; }
    public RevisionTarget Onto { get; }
    [ObservableProperty] private bool _autostash;
    [ObservableProperty] private bool _updateRefs;

    /// <summary>
    /// 動くコミット（`onto..HEAD`）を指しているほかのローカルブランチ。あるときだけ "Update dependent branches" と一覧（LocalBranchesListBox）を出す。
    /// </summary>
    public System.Collections.ObjectModel.ObservableCollection<string> DependentBranches { get; } = [];
    [ObservableProperty] private bool _hasDependentBranches;

    public RebaseDialogViewModel(RepositoryViewModel repo, RevisionTarget onto)
    {
        _repo = repo;
        Onto = onto;
        Branch = repo.References?.HeadBranch ?? "";
        Autostash = repo.Settings.RebaseAutostash;
        UpdateRefs = repo.Settings.RebaseUpdateRefs;
        _ = PrecheckAsync();
        _ = FindDependentBranchesAsync();
    }

    private async Task FindDependentBranchesAsync()
    {
        try
        {
            var r = await _repo.RunGitAsync(["rev-list", $"{Onto.Ref}..HEAD"]);
            var moving = r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet();
            foreach (var b in _repo.References?.LocalBranches ?? [])
                if (!b.IsHead && moving.Contains(b.TargetSha)) DependentBranches.Add(b.ShortName);
            HasDependentBranches = DependentBranches.Count > 0;
        }
        catch { /* 参考なので失敗しても実行はできる */ }
    }

    private async Task PrecheckAsync()
    {
        try
        {
            var b = (await _repo.RunGitAsync(GitArgs.MergeBase(Onto.Name, Branch))).StandardOutput.Trim();
            // replay は参照を書き換えないように --ref-action=print で（git 2.5x の既定は update で、ダイアログを開いただけで main が書き換わった）。
            // そのオプションが無い古い git（既定で表示だけ）は付けずにやり直す
            try { await _repo.RunGitAsync(GitArgs.ReplayPreview(Onto.Ref, b, Branch)); }
            catch (Core.Git.GitException ex) when (ex.Result.StandardError.Contains("ref-action", StringComparison.Ordinal) || ex.Result.StandardError.Contains("unknown option", StringComparison.Ordinal))
            {
                await _repo.RunGitAsync(GitArgs.ReplayPreview(Onto.Ref, b, Branch, printOnly: false));
            }
            StatusMessage = "Rebase can be done without conflicts";
            StatusIsSuccess = true;
        }
        catch (Core.Git.GitException ex) when (ex.Result.ExitCode == 1)   // 競合。128 などほかの失敗は判定できないので何も出さない
        {
            StatusMessage = "Rebase will cause conflicts";
            StatusIsWarning = true;
        }
        catch { }
    }

    public override bool CanSubmit => Branch.Length > 0;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.RebaseAutostash = Autostash;
        _repo.Settings.RebaseUpdateRefs = UpdateRefs;
        string[] args = ["-c", "core.commentChar=^", "rebase", .. UpdateRefs && HasDependentBranches ? new[] { "--update-refs" } : [], Onto.Ref];
        await RunMayConflictAsync(_repo, () => PullDialogViewModel.RunWithAutostashAsync(_repo, "Rebase", Autostash, () => _repo.RunGitAsync(args, Progress, ct), ct));
    }
}

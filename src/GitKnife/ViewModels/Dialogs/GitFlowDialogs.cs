using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Git.Model;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>Git Flow のダイアログの共通部分（git-commands.md「Git Flow」）。</summary>
public static class GitFlow
{
    /// <summary>
    /// 初期化・Start・Finish の前に作業ツリーを自分で調べ、変更があれば git を呼ばずに Git Error "Error: Working tree contains changes" を出す
    /// （untracked だけのときも出す）。
    /// </summary>
    public static void RequireCleanWorkingTree(RepositoryViewModel repo)
    {
        if (repo.HasLocalChanges) throw new GitErrorMessageException("Error: Working tree contains changes");
    }

    public static string KindTitle(GitFlowKind kind) => kind switch { GitFlowKind.Feature => "feature", GitFlowKind.Release => "release", _ => "hotfix" };
    public static string KindName(GitFlowKind kind) => kind switch { GitFlowKind.Feature => "Feature", GitFlowKind.Release => "Release", _ => "Hotfix" };
}

/// <summary>
/// Repository > Git Flow > Initialize Git Flow...。
/// Production / Development Branch と Feature / Release / Hotfix / Version Tag の接頭辞。Submit "Initialize Git Flow"。
/// 実行は `branch --no-track` ×2（失敗しても続ける）→ `config gitflow.*` → `flow init -d`。
/// </summary>
public sealed partial class GitFlowInitDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;

    public GitFlowInitDialogViewModel(RepositoryViewModel repo)
    {
        _repo = repo;
        // Production Branch の既定は main（無くて master があれば master）
        var locals = repo.References?.LocalBranches.Select(b => b.ShortName).ToHashSet() ?? [];
        _masterBranch = !locals.Contains("main") && locals.Contains("master") ? "master" : "main";
    }

    public override string Title => "Initialize Git Flow";
    public override string Subtitle => "Start using Git Flow by initializing it inside an existing git repository";
    public override string SubmitText => "Initialize Git Flow";
    public override double DialogWidth => 540;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _masterBranch;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _developBranch = "develop";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _featurePrefix = "feature/";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _releasePrefix = "release/";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _hotfixPrefix = "hotfix/";
    [ObservableProperty] private string _versionTagPrefix = "";

    /// <summary>空の欄があればその文言。</summary>
    private string? Problem =>
        MasterBranch.Trim().Length == 0 ? "Production branch name can't be empty"
        : DevelopBranch.Trim().Length == 0 ? "Development branch name can't be empty"
        : FeaturePrefix.Trim().Length == 0 ? "Feature branch prefix can't be empty"
        : ReleasePrefix.Trim().Length == 0 ? "Release branch prefix can't be empty"
        : HotfixPrefix.Trim().Length == 0 ? "Hotfix branch prefix can't be empty"
        : null;

    public override bool CanSubmit => Problem is null;

    partial void OnMasterBranchChanged(string value) => StatusMessage = Problem;
    partial void OnDevelopBranchChanged(string value) => StatusMessage = Problem;
    partial void OnFeaturePrefixChanged(string value) => StatusMessage = Problem;
    partial void OnReleasePrefixChanged(string value) => StatusMessage = Problem;
    partial void OnHotfixPrefixChanged(string value) => StatusMessage = Problem;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        GitFlow.RequireCleanWorkingTree(_repo);
        var master = MasterBranch.Trim();
        var develop = DevelopBranch.Trim();
        foreach (var args in GitArgs.GitFlowInitBranches(master, develop))
        {
            // 既にあるブランチは "a branch named 'main' already exists" で失敗するが、そのまま続ける
            try { await _repo.RunGitAsync(args, Progress, ct); }
            catch (Core.Git.GitException) { }
        }
        // フックの場所は .git/hooks の / 区切りの絶対パス
        var gitDir = _repo.CommonGitDirectory ?? System.IO.Path.Combine(_repo.Path, ".git");
        var hooks = System.IO.Path.Combine(gitDir, "hooks").Replace('\\', '/');
        foreach (var args in GitArgs.GitFlowInitConfig(master, develop, FeaturePrefix.Trim(), ReleasePrefix.Trim(), HotfixPrefix.Trim(), VersionTagPrefix.Trim(), hooks))
            await _repo.RunGitAsync(args, Progress, ct);
        await _repo.RunGitAsync(GitArgs.GitFlowInit(), Progress, ct);
    }
}

/// <summary>
/// Start Git Flow feature / release / hotfix。1 行目のラベルは接頭辞（FeaturePrefixTextBlock など）と名前の欄、2 行目 "Start at:" とブランチの選択
/// （feature / release は develop、hotfix は master のブランチが選ばれている）。副題の 'develop' / 'master' は実際のブランチ名ではなく固定の文字（hotfix は main でも 'master'）。
/// → `flow &lt;kind&gt; start &lt;name&gt; &lt;start at&gt;`。
/// </summary>
public sealed partial class GitFlowStartDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;

    public GitFlowStartDialogViewModel(RepositoryViewModel repo, GitFlowConfig config, GitFlowKind kind)
    {
        _repo = repo;
        Kind = kind;
        Prefix = config.PrefixOf(kind);
        Branches = repo.References?.LocalBranches.Select(b => b.ShortName).ToList() ?? [];
        _startAt = kind == GitFlowKind.Hotfix ? config.Master : config.Develop;
    }

    public GitFlowKind Kind { get; }
    public string Prefix { get; }
    public IReadOnlyList<string> Branches { get; }
    public string KindName => GitFlow.KindName(Kind);

    public override string Title => $"Start Git Flow {GitFlow.KindTitle(Kind)}";
    public override string Subtitle => $"Create a new {GitFlow.KindTitle(Kind)} branch based on '{(Kind == GitFlowKind.Hotfix ? "master" : "develop")}' and switch to it";
    public override string SubmitText => $"Start {KindName}";
    public override double DialogWidth => 520;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _name = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string? _startAt;

    public override bool CanSubmit => Name.Trim().Length > 0 && !string.IsNullOrEmpty(StartAt) && RefName.Problem(Prefix + Name.Trim(), _repo) is null;

    partial void OnNameChanged(string value)
    {
        var p = value.Trim().Length == 0 ? null : RefName.Problem(Prefix + value.Trim(), _repo);
        StatusMessage = string.IsNullOrEmpty(p) ? null : p;
    }

    public override Task SubmitAsync(CancellationToken ct)
    {
        GitFlow.RequireCleanWorkingTree(_repo);
        return _repo.RunGitAsync(GitArgs.GitFlowStart(Kind, Name.Trim(), StartAt!), Progress, ct);
    }
}

/// <summary>
/// Finish Git Flow feature / release / hotfix。"Feature:" などとブランチの選択、Release / Hotfix は "Message:"（タグのメッセージ）、チェックボックス。
/// チェックは設定 GitFlowFinish*（Finish したときに保存）。タグのメッセージは一時ファイルに書いて -f で渡す。
/// </summary>
public sealed partial class GitFlowFinishDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private readonly GitFlowConfig _config;

    public GitFlowFinishDialogViewModel(RepositoryViewModel repo, GitFlowConfig config, GitFlowKind kind, string branch)
    {
        _repo = repo;
        _config = config;
        Kind = kind;
        Branches = repo.References?.LocalBranches.Select(b => b.ShortName).Where(b => config.Classify(b)?.Kind == kind).ToList() ?? [];
        _branch = branch;
        var s = repo.Settings;
        (_deleteBranches, _rebase, _noFastForward, _backMerge) = kind switch
        {
            GitFlowKind.Feature => (s.GitFlowFinishFeature_DeleteBranches, s.GitFlowFinishFeature_Rebase, s.GitFlowFinishFeature_NoFastForward, false),
            GitFlowKind.Release => (s.GitFlowFinishRelease_DeleteBranches, false, false, s.GitFlowFinishRelease_BackMergeMaster),
            _ => (s.GitFlowFinishHotfix_DeleteBranches, false, false, false),
        };
    }

    public GitFlowKind Kind { get; }
    public IReadOnlyList<string> Branches { get; }
    public string BranchLabel => GitFlow.KindName(Kind) + ":";
    public bool IsFeature => Kind == GitFlowKind.Feature;
    public bool IsRelease => Kind == GitFlowKind.Release;
    public bool HasMessage => Kind != GitFlowKind.Feature;
    public string MessagePlaceholder => $"Enter {GitFlow.KindTitle(Kind)} message (optional)";

    public override string Title => $"Finish Git Flow {GitFlow.KindTitle(Kind)}";
    public override string Subtitle => Kind == GitFlowKind.Feature
        ? "Finish the feature and merge it into the develop branch"
        : $"Finish the {GitFlow.KindTitle(Kind)} and merge it into the develop and master branches";
    public override string SubmitText => "Finish";
    public override double DialogWidth => Kind == GitFlowKind.Feature ? 490 : 530;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string? _branch;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private bool _deleteBranches;
    [ObservableProperty] private bool _rebase;
    [ObservableProperty] private bool _noFastForward;
    [ObservableProperty] private bool _backMerge;

    public override bool CanSubmit => Branch is not null && _config.Classify(Branch)?.Kind == Kind;

    public override string ActivityName => $"Finishing '{Branch}'";

    public override async Task SubmitAsync(CancellationToken ct)
    {
        GitFlow.RequireCleanWorkingTree(_repo);
        var name = _config.Classify(Branch!)!.Value.Name;
        var s = _repo.Settings;
        StatusMessage = $"Finishing '{Branch}'...";
        switch (Kind)
        {
            case GitFlowKind.Feature:
                (s.GitFlowFinishFeature_DeleteBranches, s.GitFlowFinishFeature_Rebase, s.GitFlowFinishFeature_NoFastForward) = (DeleteBranches, Rebase, NoFastForward);
                await _repo.RunGitAsync(GitArgs.GitFlowFinishFeature(name, DeleteBranches, Rebase, NoFastForward), Progress, ct);
                break;
            case GitFlowKind.Release:
                (s.GitFlowFinishRelease_DeleteBranches, s.GitFlowFinishRelease_BackMergeMaster) = (DeleteBranches, BackMerge);
                await _repo.RunGitAsync(GitArgs.GitFlowFinishRelease(name, WriteTagMessage(), DeleteBranches, BackMerge), Progress, ct);
                break;
            default:
                s.GitFlowFinishHotfix_DeleteBranches = DeleteBranches;
                await _repo.RunGitAsync(GitArgs.GitFlowFinishHotfix(name, WriteTagMessage(), DeleteBranches), Progress, ct);
                break;
        }
    }

    /// <summary>タグのメッセージの一時ファイル（UTF-8、BOM なし）。空でも作り、空のまま -f を付ける。</summary>
    private string WriteTagMessage()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GitKnife");
        Core.Git.HostDirectory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, "gitknife-tagMessage.txt");
        Core.Git.HostFile.WriteAllText(path, Message, new System.Text.UTF8Encoding(false));
        return path;
    }
}

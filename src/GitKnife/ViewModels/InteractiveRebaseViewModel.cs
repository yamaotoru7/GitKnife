using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;
using GitKnife.ViewModels.Dialogs;
using GitKnife.Views;

namespace GitKnife.ViewModels;

/// <summary>
/// ActionsComboBox の項目（色の点・名前・説明・キー）。
/// 最後の 2 つ（Move Up / Move Down）は動作ではなく、選ぶと行を動かす（Action が "move-up" / "move-down"、点なし）。
/// </summary>
public sealed record RebaseActionItem(string Action, string Name, string? Color, string Description, string Key)
{
    public override string ToString() => Name;
    public bool IsMove => Color is null;
    /// <summary>Squash / Fixup の点はテーマの色（Gk.Rebase.Squash）。</summary>
    public bool IsMeld => Action is "squash" or "fixup";

    public static readonly IReadOnlyList<RebaseActionItem> All =
    [
        new("pick", "Pick", "#34C84A", "Use Commit", "P"),
        new("edit", "Edit", "#FDBE41", "Stop for amending", "E"),
        new("reword", "Reword", "#FDBE41", "Edit the commit message", "R"),
        new("squash", "Squash", "#E1E1E1", "Meld commit into previous one and keep message", "S"),
        new("fixup", "Fixup", "#E1E1E1", "Meld commit into previous one and discard message", "F"),
        new("drop", "Drop", "#FA4B49", "Remove commit", "D"),
    ];
    public static readonly RebaseActionItem MoveUp = new("move-up", "Move Up", null, "", "↑");
    public static readonly RebaseActionItem MoveDown = new("move-down", "Move Down", null, "", "↓");
    public static readonly IReadOnlyList<RebaseActionItem> Menu = [.. All, MoveUp, MoveDown];
    public static RebaseActionItem Get(string action) => All.First(a => a.Action == action);
}

/// <summary>squash の線（動作の欄と件名の間）。Source は点、Through は squash の鎖の途中の Drop、Target は矢印（鎖が入る行）。</summary>
public enum RebaseGraphKind { None, Source, Through, Target }

/// <summary>Interactive Rebase の 1 行。</summary>
public sealed partial class RebaseStepViewModel(InteractiveRebaseViewModel owner, CommitInfo commit, RebaseActionItem action) : ObservableObject
{
    public CommitInfo Commit { get; } = commit;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsReword), nameof(IsMeld), nameof(IsDrop), nameof(MenuSelection))] private RebaseActionItem _action = action;
    /// <summary>reword の新しいメッセージ（件名と説明）。MessageEdited が false のあいだは使わない（元のメッセージか、squash を合わせたもの）。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Subject))] private string _message = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Subject))] private bool _messageEdited;
    /// <summary>squash が入ってきたので自動で Reword にした（squash が無くなれば Pick に戻す。Squash → Fixup で相手が Pick に戻る）。</summary>
    public bool AutoReword { get; set; }

    [ObservableProperty] private RebaseGraphKind _graphKind;
    /// <summary>上の行から線が来る。</summary>
    [ObservableProperty] private bool _graphTop;
    /// <summary>この行のメッセージの窓が出ている（その行を太字にする）。</summary>
    [ObservableProperty] private bool _isRewording;

    public bool IsReword => Action.Action == "reword";
    public bool IsMeld => Action.IsMeld;
    public bool IsDrop => Action.Action == "drop";
    public string OriginalMessage => Commit.Body.Length > 0 ? $"{Commit.Subject}\n\n{Commit.Body}" : Commit.Subject;
    /// <summary>行の件名（Reword で直した件名を行に出す）。</summary>
    public string Subject => MessageEdited ? Message.Replace("\r\n", "\n").Split('\n')[0] : Commit.Subject;

    /// <summary>ComboBox の SelectedItem。Move Up / Down を選んだら行を動かし、表示は今の動作に戻す。</summary>
    public RebaseActionItem MenuSelection
    {
        get => Action;
        set
        {
            if (value is null || value == Action) return;
            if (value.IsMove)
            {
                owner.Move([this], value == RebaseActionItem.MoveUp ? -1 : 1);
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => OnPropertyChanged(nameof(MenuSelection)));
                return;
            }
            owner.ApplyAction([this], value);
        }
    }

    /// <summary>"5 Jan 2026 16:33"（Interactive Rebase の日付の書式）。</summary>
    public string DateText => Commit.AuthorTime.ToLocalTime().ToString("d MMM yyyy HH:mm", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Interactive Rebase のウィンドウ。並びは新しい順（上が HEAD）。
/// Rebase を押すと計画を一時ファイルに書き、GitKnife.RI.exe を sequence.editor / core.editor にして git rebase -i を起動する。
/// </summary>
public sealed partial class InteractiveRebaseViewModel : DialogViewModel, IRevisionDetailsHost
{
    private readonly RepositoryViewModel _repo;
    public override string Title => "Interactive Rebase";
    public override string Subtitle => "";
    public override string SubmitText => "Rebase";
    public override bool ShowHeader => false;
    public override double DialogWidth => 1020;

    public CommitInfo Base { get; }
    public string Branch => _repo.References?.HeadBranch ?? "HEAD";
    public ObservableCollection<RebaseStepViewModel> Steps { get; } = [];
    public IReadOnlyList<RebaseActionItem> Actions => RebaseActionItem.Menu;
    [ObservableProperty] private RebaseStepViewModel? _selectedStep;
    /// <summary>選んでいる行（複数選択。View が SelectionChanged で入れる）。</summary>
    public IReadOnlyList<RebaseStepViewModel> SelectedSteps { get; set; } = [];

    // ---- 下半分のコミット詳細（Commit / Changes。File Tree は押せない）----
    public RepositoryViewModel Repository => _repo;
    public RevisionDetailsViewModel Details { get; private set; } = null!;
    [ObservableProperty] private DetailsMode _detailsMode = DetailsMode.Commit;
    public CommitRow? SelectedCommit => SelectedStep is { } s ? _repo.Commits.FirstOrDefault(c => c.Commit.Sha == s.Commit.Sha) : null;
    /// <summary>PARENTS のリンク（ダイアログの中なので一覧は動かさない）。</summary>
    public void NavigateToCommit(string sha) { }

    partial void OnSelectedStepChanged(RebaseStepViewModel? value)
    {
        OnPropertyChanged(nameof(SelectedCommit));
        if (SelectedCommit is { } row) _ = Details.LoadAsync(row);
    }

    // ---- Reword の編集（Reword を選ぶか R を押すと、行の下に件名・Description・Cancel / OK の小さな窓）----
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsRewordOpen))] private RebaseStepViewModel? _rewordStep;

    partial void OnRewordStepChanged(RebaseStepViewModel? oldValue, RebaseStepViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsRewording = false;
        if (newValue is not null) newValue.IsRewording = true;
    }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RewordSubjectLength))] private string _rewordSubject = "";
    [ObservableProperty] private string _rewordDescription = "";
    public bool IsRewordOpen => RewordStep is not null;
    /// <summary>件名の残りの文字数（CommitSubjectLowLimit − 長さ。"xxx" なら 47）。</summary>
    public int RewordSubjectLength => _repo.Settings.CommitSubjectLowLimit - RewordSubject.Length;

    private void BeginReword(RebaseStepViewModel step)
    {
        var text = (step.MessageEdited ? step.Message : DefaultMessage(step)).Replace("\r\n", "\n");
        var nl = text.IndexOf('\n');
        RewordSubject = nl < 0 ? text : text[..nl];
        RewordDescription = nl < 0 ? "" : text[(nl + 1)..].TrimStart('\n');
        SelectedStep = step;
        RewordStep = step;
    }

    [RelayCommand]
    private void OkReword()
    {
        if (RewordStep is not { } s) return;
        s.Message = RewordDescription.Trim().Length > 0 ? $"{RewordSubject}\n\n{RewordDescription.TrimEnd()}" : RewordSubject;
        s.MessageEdited = true;
        RewordStep = null;
    }

    [RelayCommand]
    private void CancelReword() => RewordStep = null;

    [ObservableProperty] private bool _updateRefs;
    [ObservableProperty] private bool _backup;
    /// <summary>開いたときの並びと動作（Cancel の確認を出すかどうか）。</summary>
    private string _initialState = "";

    public InteractiveRebaseViewModel(RepositoryViewModel repo, CommitInfo @base, string? initialAction = null, string? target = null)
    {
        _repo = repo;
        Details = new RevisionDetailsViewModel(repo, this);
        Base = @base;
        UpdateRefs = repo.Settings.InteractiveRebase_UpdateRefs;
        Backup = repo.Settings.InteractiveRebase_CreateBackup;
        _ = LoadAsync(initialAction, target);
    }

    private async Task LoadAsync(string? initialAction, string? target)
    {
        var commits = await _repo.ReadCommitsSinceAsync(Base.Sha);
        foreach (var c in commits)
        {
            var action = c.Sha == target && initialAction is not null ? RebaseActionItem.Get(initialAction) : RebaseActionItem.All[0];
            Steps.Add(new RebaseStepViewModel(this, c, action));
        }
        SelectedStep = Steps.FirstOrDefault(s => s.Commit.Sha == target) ?? Steps.FirstOrDefault();
        Recompute();
        _initialState = State();
    }

    private string State() => string.Join(",", Steps.Select(s => $"{s.Action.Action}:{s.Commit.Sha}:{s.MessageEdited}"));

    /// <summary>何か変えてあれば "Do you really want to cancel Interactive Rebase?"（変えていなければそのまま閉じる）。</summary>
    public override bool ConfirmCancel()
        => State() == _initialState || Alerts.Confirm("Do you really want to cancel Interactive Rebase?", "All your changes will be discarded.", "Yes", 550, "No");

    /// <summary>行の動作を変える（ドロップダウン、P / E / R / S / F / D。選んだ行すべて）。1 行に Reword を選んだらメッセージの窓を出す。</summary>
    public void ApplyAction(IReadOnlyList<RebaseStepViewModel> steps, RebaseActionItem action)
    {
        foreach (var s in steps)
        {
            s.AutoReword = false;
            s.Action = action;
        }
        Recompute();
        if (action.Action == "reword" && steps.Count == 1) BeginReword(steps[0]);
    }

    /// <summary>
    /// squash の鎖を数え直す。上（新しい）から見て、Squash / Fixup の行は下の最初の残る行（Pick など）に入る。間の Drop は線が通るだけ。
    /// 鎖に Squash が 1 つでもあれば入る先を Reword にし、無くなれば（自動で Reword にした行だけ）Pick に戻す。
    /// </summary>
    private void Recompute()
    {
        var withSquash = new HashSet<RebaseStepViewModel>();
        var chainHasSquash = false;
        var prevDown = false;
        foreach (var s in Steps)
        {
            var kind = s.IsMeld ? RebaseGraphKind.Source : !prevDown ? RebaseGraphKind.None : s.IsDrop ? RebaseGraphKind.Through : RebaseGraphKind.Target;
            s.GraphTop = prevDown && kind != RebaseGraphKind.None;
            s.GraphKind = kind;
            if (!prevDown) chainHasSquash = false;
            if (s.Action.Action == "squash") chainHasSquash = true;
            if (kind == RebaseGraphKind.Target && chainHasSquash) withSquash.Add(s);
            prevDown = kind is RebaseGraphKind.Source or RebaseGraphKind.Through;
        }
        foreach (var s in Steps)
        {
            if (withSquash.Contains(s) && s.Action.Action == "pick") { s.Action = RebaseActionItem.Get("reword"); s.AutoReword = true; }
            else if (!withSquash.Contains(s) && s.AutoReword) { s.Action = RebaseActionItem.Get("pick"); s.AutoReword = false; }
        }
        Validate();
    }

    /// <summary>この行に入る squash / fixup の行（todo の順＝下から上）。</summary>
    private List<RebaseStepViewModel> ChainInto(RebaseStepViewModel target)
    {
        var chain = new List<RebaseStepViewModel>();
        for (var i = Steps.IndexOf(target) - 1; i >= 0 && Steps[i].GraphKind is RebaseGraphKind.Source or RebaseGraphKind.Through; i--)
            if (Steps[i].IsMeld) chain.Add(Steps[i]);
        return chain;
    }

    /// <summary>Reword の窓に最初に出すメッセージ：自分のメッセージに、入ってくる Squash の行のメッセージを足す。</summary>
    private string DefaultMessage(RebaseStepViewModel step)
        => string.Join("\n\n", ChainInto(step).Where(s => s.Action.Action == "squash").Select(s => s.OriginalMessage).Prepend(step.OriginalMessage));

    /// <summary>一番古い（下の）残るコミットを squash / fixup にはできない（親が無い）。</summary>
    private void Validate()
    {
        var oldest = Steps.LastOrDefault(s => !s.IsDrop);
        var bad = oldest is { IsMeld: true };
        StatusMessage = Steps.Count == 0 ? "Nothing to rebase" : bad ? "The oldest commit can't be squashed into its parent" : null;
        StatusIsWarning = bad;
        RaiseCanSubmit();
    }

    public override bool CanSubmit => Steps.Count > 0 && !StatusIsWarning;

    /// <summary>行をドラッグで別の位置へ。</summary>
    public void MoveTo(RebaseStepViewModel step, int index)
    {
        var i = Steps.IndexOf(step);
        if (i < 0 || index < 0 || index >= Steps.Count || i == index) return;
        Steps.Move(i, index);
        SelectedStep = step;
        Recompute();
    }

    /// <summary>選んだ行を上下に動かす（Ctrl+Up / Ctrl+Down、ドロップダウンの Move Up / Move Down。上ほど新しい＝後で当てる）。端の行があれば動かさない。</summary>
    public void Move(IReadOnlyList<RebaseStepViewModel> steps, int delta)
    {
        var indexes = steps.Select(Steps.IndexOf).Where(i => i >= 0).OrderBy(i => delta < 0 ? i : -i).ToList();
        if (indexes.Count == 0 || indexes.Any(i => i + delta < 0 || i + delta >= Steps.Count)) return;
        foreach (var i in indexes) Steps.Move(i, i + delta);
        Recompute();
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        _repo.Settings.InteractiveRebase_UpdateRefs = UpdateRefs;
        _repo.Settings.InteractiveRebase_CreateBackup = Backup;
        // SSH の接続先は計画とメッセージを向こうの /tmp に置き、RI の代わりにシェルのスクリプトを使う（T1305）
        var name = "rebase-" + Guid.NewGuid().ToString("N")[..8];
        var ssh = _repo.Git.Host as Core.Git.SshGitHost;
        var dir = ssh is not null ? ssh.ToLocalPath("/tmp/gitknife-" + name) : Path.Combine(Path.GetTempPath(), "GitKnife", name);
        var messages = Path.Combine(dir, "messages");
        Core.Git.HostDirectory.CreateDirectory(messages);
        var planFile = Path.Combine(dir, "plan.txt");
        if (ssh is not null) await Core.Git.InteractiveRebase.WriteRemoteScriptsAsync(dir, ct);

        // Reword の行に Squash が入るときは、その行は pick にして、鎖の最後（git がまとめたメッセージのエディタを開くところ）でメッセージを差し替える
        var actions = Steps.ToDictionary(s => s, s => s.Action.Action);
        var messageFiles = new List<(string Sha, string Message)>();
        foreach (var s in Steps.Where(s => s.IsReword))
        {
            var message = s.MessageEdited ? s.Message : DefaultMessage(s);
            var chain = ChainInto(s);
            if (chain.Any(c => c.Action.Action == "squash"))
            {
                actions[s] = "pick";
                messageFiles.Add((chain[^1].Commit.Sha, message));
            }
            else messageFiles.Add((s.Commit.Sha, message));
        }

        // todo は古い順なので、画面（新しい順）を逆にする
        var plan = Steps.Reverse().Select(s => new RebasePlanStep(actions[s], s.Commit.Sha)).ToList();
        if (!UpdateRefs) plan.Add(InteractiveRebase.NoUpdateRefs);
        await Core.Git.HostFile.WriteAllTextAsync(planFile, InteractiveRebase.FormatPlan(plan), ct);
        foreach (var (sha, message) in messageFiles)
            await Core.Git.HostFile.WriteAllTextAsync(Path.Combine(messages, sha + ".txt"), message.Replace("\r\n", "\n").TrimEnd() + "\n", new UTF8Encoding(false), ct);

        // "Backup current state with a temporary branch"（ブランチ名は推定）
        if (Backup) await _repo.RunGitAsync(["branch", $"backup/{Branch}-{DateTime.Now:yyyyMMdd-HHmmss}", "HEAD"], ct: ct);

        var ri = Path.Combine(AppContext.BaseDirectory, "GitKnife.RI.exe");
        await RunMayConflictAsync(_repo, () => _repo.RunGitAsync(InteractiveRebase.Arguments(ri, planFile, messages, Base.Sha, _repo.Git.Host), Progress, ct,
            InteractiveRebase.Environment(ri, planFile, messages, _repo.Git.Host)));
    }
}

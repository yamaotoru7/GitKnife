using CommunityToolkit.Mvvm.ComponentModel;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Git 操作ダイアログの共通部分（題名・サブタイトル・本文・フッター）。
/// 本文は各ダイアログの View、実行は <see cref="SubmitAsync"/>。成功したら閉じ、git が失敗したら Git Error を出す。
/// </summary>
public abstract partial class DialogViewModel : ObservableObject
{
    public abstract string Title { get; }
    public abstract string Subtitle { get; }
    /// <summary>SubmitButton の文字（動作名。"Fetch"、"Pull" など）。</summary>
    public abstract string SubmitText { get; }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmitNow))] private bool _isBusy;
    /// <summary>フッターの StatusMessageTextBlock（検証メッセージや git の進捗）。</summary>
    [ObservableProperty] private string? _statusMessage;
    /// <summary>フッターのアイコン：警告は黄色い三角、成功（事前判定で競合が無い）は緑のチェックの丸。</summary>
    [ObservableProperty] private bool _statusIsWarning;
    [ObservableProperty] private bool _statusIsSuccess;

    /// <summary>入力が有効か（無効なら SubmitButton を押せない）。</summary>
    public virtual bool CanSubmit => true;
    public bool CanSubmitNow => CanSubmit && !IsBusy;
    protected void RaiseCanSubmit()
    {
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(CanSubmitNow));
    }

    /// <summary>git の stderr の進捗行をフッターに出す。</summary>
    protected void Progress(string line) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => StatusMessage = line);

    public abstract Task SubmitAsync(CancellationToken ct);

    /// <summary>SubmitButton を出すか（Git Error / Merge Conflict は Close だけ）。</summary>
    public virtual bool ShowSubmit => true;
    public virtual string CancelText => "Cancel";
    /// <summary>Cancel（Esc）で閉じてよいか。確認を出すダイアログ（Interactive Rebase）が上書きする。</summary>
    public virtual bool ConfirmCancel() => true;
    public virtual double DialogWidth => 490;
    /// <summary>高さ。NaN なら中身に合わせる。</summary>
    public virtual double DialogHeight => double.NaN;
    /// <summary>右下のつまみで大きさを変えられるか（Workspaces）。本文は残りの高さいっぱいに広がる。</summary>
    public virtual bool CanResize => false;
    /// <summary>左のアイコンと題名・サブタイトルを出すか（Interactive Rebase は本文に自分の見出しを持つ）。</summary>
    public virtual bool ShowHeader => true;
    /// <summary>下のボタンの帯を出すか（Benchmark は右上の × だけ）。</summary>
    public virtual bool ShowFooter => true;
    /// <summary>アイコンの右下に黄色い警告の三角を重ねるか（Git Error、Shared のカスタムコマンドの確認）。</summary>
    public virtual bool ShowWarningBadge => false;

    /// <summary>Activity Manager にジョブを残すリポジトリ（RepositoryActions.Run が入れる。null なら残さない）。</summary>
    public RepositoryViewModel? ActivityRepository { get; set; }
    /// <summary>Activity Manager のジョブ名（例："Fetch all"）。</summary>
    public virtual string ActivityName => Title;
    /// <summary>成功したときの一言（例："Already up to date"）。既定は出力の最後の行。</summary>
    public virtual string ActivitySummary(string output)
    {
        var last = output.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.StartsWith("$ ", StringComparison.Ordinal));
        return last ?? "";
    }

    /// <summary>
    /// 競合で止まりうる操作（merge、stash apply、pull、rebase）を実行する。git が失敗しても競合が残っていれば <see cref="GitConflictException"/> にする
    /// （ダイアログは閉じて "Merge Conflict" を出す）。
    /// </summary>
    protected static async Task RunMayConflictAsync(RepositoryViewModel repo, Func<Task> action)
    {
        try { await action(); }
        catch (Core.Git.GitException ex)
        {
            if (await repo.HasConflictsAsync()) throw new GitConflictException(ex.Result);
            throw;
        }
    }
}

/// <summary>git を呼ぶ前に分かった失敗。Git Error にこの文だけを出す（Git Flow の "Error: Working tree contains changes"）。</summary>
public sealed class GitErrorMessageException(string message) : Exception(message);

/// <summary>git が競合で止まった（失敗ではなく、続きは Local Changes と NotificationBar で行う）。</summary>
public sealed class GitConflictException(Core.Git.GitResult result) : Exception("conflict")
{
    public Core.Git.GitResult Result { get; } = result;
}

/// <summary>
/// "Go To Line"（Blame の Ctrl+G。250×120、"Line number:" の LineNumberTextBox、Go / Cancel）。
/// 見出しのアイコンと説明は無い。Go を押したら <see cref="Line"/> の行へ動く（動かすのは呼んだ側）。
/// </summary>
public sealed partial class GoToLineDialogViewModel : DialogViewModel
{
    public override string Title => "Go To Line";
    public override string Subtitle => "";
    public override string SubmitText => "Go";
    public override bool ShowHeader => false;
    public override double DialogWidth => 250;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _lineNumber = "";
    public int Line => int.TryParse(LineNumber.Trim(), out var n) ? n : 0;
    public override bool CanSubmit => Line > 0;
    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>確認だけのダイアログ（"Are you sure you want to delete submodule sublib?" など。本文なし、アイコン・題名・説明・Submit / Cancel）。</summary>
public sealed class ConfirmDialogViewModel(string title, string subtitle, string submitText, double width = 590, string cancelText = "Cancel") : DialogViewModel
{
    public override string Title => title;
    public override string Subtitle => subtitle;
    public override string SubmitText => submitText;
    public override double DialogWidth => width;
    public override string CancelText => cancelText;
    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>git の出力を見せるだけのダイアログ（"Git Error" と "Merge Conflict"。MessageEditor と Close）。</summary>
public sealed class GitOutputDialogViewModel(string title, string subtitle, string output) : DialogViewModel
{
    public override string Title => title;
    public override string Subtitle => subtitle;
    public override string SubmitText => "Submit";
    public override bool ShowSubmit => false;
    public override string CancelText => "Close";
    public override double DialogWidth => 740;
    /// <summary>Git Error だけ警告の三角を付ける（Merge Conflict には付かない）。</summary>
    public override bool ShowWarningBadge => title == "Git Error";
    public string Output => output;
    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git.Model;

namespace GitKnife.ViewModels;

/// <summary>NotificationBar の文の一部（IsLink ならリンクの色。ブランチ名）。</summary>
public sealed record NotificationPart(string Text, bool IsLink = false);

/// <summary>NotificationBar のボタン（Button1〜3）でする操作。</summary>
public enum NotificationAction { None, Resolve, Continue, Skip, Commit, BisectBad, BisectGood, BisectSkip }

/// <summary>
/// 進行中の操作の帯。
/// merge の文言は（"Merging branch 'x' into 'main'. Fix 1 conflict and then continue."）。ほかの操作の文言とボタンは推定。
/// </summary>
public sealed partial class NotificationBarViewModel : ObservableObject
{
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private IReadOnlyList<NotificationPart> _parts = [];
    [ObservableProperty] private string? _button1;
    [ObservableProperty] private string? _button2;
    [ObservableProperty] private string? _button3;
    [ObservableProperty] private bool _button2Enabled = true;
    [ObservableProperty] private bool _showProgress;
    [ObservableProperty] private double _progress;
    public NotificationAction Action1 { get; private set; }
    public NotificationAction Action2 { get; private set; }
    public NotificationAction Action3 { get; private set; }
    public OperationState? State { get; private set; }

    public void Update(OperationState? state, int conflicts, string? headSha = null)
    {
        State = state;
        IsVisible = state is { Operation: not RepositoryOperation.None };
        if (!IsVisible || state is null) return;
        var fix = conflicts switch
        {
            0 => "All conflicts are resolved. Commit to continue.",
            1 => "Fix 1 conflict and then continue.",
            var n => $"Fix {n} conflicts and then continue.",
        };
        ShowProgress = false;
        Button2Enabled = true;
        (Button1, Action1, Button2, Action2) = (null, NotificationAction.None, null, NotificationAction.None);
        (Button3, Action3) = (null, NotificationAction.None);
        switch (state.Operation)
        {
            case RepositoryOperation.Unmerged:
                // "Working directory contains 2 unmerged files"、Resolve と Abort
                Parts = [new(conflicts == 1 ? "Working directory contains 1 unmerged file" : $"Working directory contains {conflicts} unmerged files")];
                (Button1, Action1) = ("Resolve", NotificationAction.Resolve);
                break;
            case RepositoryOperation.Merge:
                Parts = [new("Merging branch '"), new(state.Other, true), new("' into '"), new(state.Target, true), new($"'. {fix}")];
                // merge の帯には Abort だけを出す
                break;
            case RepositoryOperation.Rebase or RepositoryOperation.RebaseInteractive or RepositoryOperation.ApplyMailbox:
                // Edit で止まったとき："Rebasing 'main' → '4f248f5' (rebased 1/2 commits)"、進捗の棒は 1/2、続けて " Amending '3b121ca'."。
                // 競合で止まったときの続きの文は推定（ほかの帯と同じ fix の文）
                Parts = [new("Rebasing '"), new(state.Target, true), new("' → '"), new(state.Other, true),
                    new(state.Total > 0 ? $"' (rebased {Math.Max(0, state.Step - 1)}/{state.Total} commits)" : "'"),
                    new(state.Amending is { } amending ? $" Amending '{amending}'." : $" {fix}")];
                ShowProgress = state.Total > 0;
                // 今止まっているコミットは済みに数えない（git の msgnum − 1）
                Progress = state.Total > 0 ? 100.0 * Math.Max(0, state.Step - 1) / state.Total : 0;
                (Button1, Action1, Button2, Action2) = ("Continue", NotificationAction.Continue, "Skip", NotificationAction.Skip);
                break;
            case RepositoryOperation.CherryPick:
                Parts = [new("Cherry-picking commit "), new(state.Other, true), new($" into '{state.Target}'. {fix}")];
                (Button1, Action1) = ("Continue", NotificationAction.Continue);
                break;
            case RepositoryOperation.Revert:
                Parts = [new("Reverting commit "), new(state.Other, true), new($" on '{state.Target}'. {fix}")];
                (Button1, Action1) = ("Continue", NotificationAction.Continue);
                break;
            case RepositoryOperation.Bisect:
                // "Bisecting, started from 'main'. Mark current commit as good or bad and checkout another one"、Bad / Good / Abort。
                // 始めたときのコミットのまま、または今のコミットに bad を付けた後は Good を押せない（始めた直後から Good が無効）
                // good と bad の両方に印があれば git が選んだコミットを訊く："Is '7450f74' good or bad?"、Bad / Good / Skip
                if (state.BisectReady && headSha is { Length: >= 7 })
                {
                    Parts = [new("Bisecting, started from '"), new(state.Other, true), new("'. Is '"), new(headSha[..7], true), new("' good or bad?")];
                    (Button1, Action1, Button2, Action2) = ("Bad", NotificationAction.BisectBad, "Good", NotificationAction.BisectGood);
                    (Button3, Action3) = ("Skip", NotificationAction.BisectSkip);
                    break;
                }
                Parts = [new("Bisecting, started from '"), new(state.Other, true), new("'. Mark current commit as good or bad and checkout another one")];
                (Button1, Action1, Button2, Action2) = ("Bad", NotificationAction.BisectBad, "Good", NotificationAction.BisectGood);
                Button2Enabled = state.Target != headSha;
                break;
        }
    }

    /// <summary>Abort の確認の文言（merge 。ほかは同じ形で推定）。</summary>
    public (string title, string message, string submit, string[] args) AbortInfo => State?.Operation switch
    {
        RepositoryOperation.Merge => ("Do you want to abort the merge?", "All uncommitted changes will be lost.", "Abort merge", ["reset", "--merge"]),
        // 競合したファイルだけがあるとき（stash の適用など）。Abort は merge の中止と同じ形
        RepositoryOperation.Unmerged => ("Do you want to abort?", "Changes in the unmerged files will be lost.", "Abort", ["reset", "--merge"]),
        RepositoryOperation.CherryPick => ("Do you want to abort the cherry-pick?", "All uncommitted changes will be lost.", "Abort cherry-pick", ["cherry-pick", "--abort"]),
        RepositoryOperation.Revert => ("Do you want to abort the revert?", "All uncommitted changes will be lost.", "Abort revert", ["revert", "--abort"]),
        RepositoryOperation.Bisect => ("", "", "Abort bisect", ["bisect", "reset"]),
        RepositoryOperation.ApplyMailbox => ("Do you want to abort applying patches?", "All uncommitted changes will be lost.", "Abort", ["am", "--abort"]),
        _ => ("Do you want to abort the rebase?", "All uncommitted changes will be lost.", "Abort rebase", ["rebase", "--abort"]),
    };

    /// <summary>Abort に確認を出すか（bisect は確認なしで `bisect reset`）。</summary>
    public bool AbortNeedsConfirmation => State?.Operation != RepositoryOperation.Bisect;

    /// <summary>Continue / Skip の git コマンド（エディタは開かない。メッセージはそのまま）。</summary>
    public string[]? CommandFor(NotificationAction action) => (State?.Operation, action) switch
    {
        (RepositoryOperation.Bisect, NotificationAction.BisectBad) => ["bisect", "bad"],
        (RepositoryOperation.Bisect, NotificationAction.BisectGood) => ["bisect", "good"],
        (RepositoryOperation.Bisect, NotificationAction.BisectSkip) => ["bisect", "skip"],
        (RepositoryOperation.Rebase or RepositoryOperation.RebaseInteractive, NotificationAction.Continue) => ["-c", "core.editor=true", "rebase", "--continue"],
        (RepositoryOperation.Rebase or RepositoryOperation.RebaseInteractive, NotificationAction.Skip) => ["rebase", "--skip"],
        (RepositoryOperation.ApplyMailbox, NotificationAction.Continue) => ["am", "--continue"],
        (RepositoryOperation.ApplyMailbox, NotificationAction.Skip) => ["am", "--skip"],
        (RepositoryOperation.CherryPick, NotificationAction.Continue) => ["-c", "core.editor=true", "cherry-pick", "--continue"],
        (RepositoryOperation.Revert, NotificationAction.Continue) => ["-c", "core.editor=true", "revert", "--continue"],
        _ => null,
    };
}

namespace GitKnife.Core.Git.Model;

/// <summary>コミット 1 つ（コミット一覧の 1 行）。</summary>
public sealed record CommitInfo(
    string Sha,
    IReadOnlyList<string> Parents,
    string AuthorName,
    string AuthorEmail,
    DateTimeOffset AuthorTime,
    string CommitterName,
    string CommitterEmail,
    DateTimeOffset CommitTime,
    string Subject,
    string Body)
{
    /// <summary>説明（本文）がある。一覧で "↩" を出す。</summary>
    public bool HasDescription => Body.Length > 0;
    /// <summary>コミットした人か日時が作者と違う（rebase、cherry-pick、amend など）。詳細に COMMITTER を並べる。</summary>
    public bool HasDistinctCommitter => CommitterName != AuthorName || !string.Equals(CommitterEmail, AuthorEmail, StringComparison.OrdinalIgnoreCase) || CommitTime != AuthorTime;
    /// <summary>コミット一覧の SHA（7 桁。"b467ce1"）。</summary>
    public string ShortSha => Sha[..Math.Min(7, Sha.Length)];
    /// <summary>コミット詳細のヘッダの SHA（9 桁。"db8122265"）。</summary>
    public string HeaderSha => Sha[..Math.Min(9, Sha.Length)];
    public bool IsMerge => Parents.Count > 1;
    /// <summary>stash のコミット（親を 1 つに縮めて一覧に差し込んだもの）。</summary>
    public bool IsStash { get; init; }
    /// <summary>縮める前の本当の親（コミット詳細の PARENTS。stash は base と index の 2 つ）。</summary>
    public IReadOnlyList<string>? AllParents { get; init; }
    public IReadOnlyList<string> DisplayParents => AllParents ?? Parents;
    /// <summary>たたんだマージ（一覧では最初の親だけにつなぎ、取り込んだ側のコミットを隠す。MergeCollapse）。</summary>
    public bool IsCollapsedMerge { get; init; }
}

public enum ReferenceKind { LocalBranch, RemoteBranch, Tag, Stash, Other }

/// <summary>参照（サイドバーとコミット一覧のラベル）。</summary>
public sealed record ReferenceInfo(string CanonicalName, string TargetSha, ReferenceKind Kind)
{
    /// <summary>refs/heads/feature/login → feature/login、refs/remotes/origin/main → origin/main、refs/tags/v1 → v1。</summary>
    public string ShortName => Kind switch
    {
        ReferenceKind.LocalBranch => CanonicalName["refs/heads/".Length..],
        ReferenceKind.RemoteBranch => CanonicalName["refs/remotes/".Length..],
        ReferenceKind.Tag => CanonicalName["refs/tags/".Length..],
        _ => CanonicalName,
    };
    /// <summary>リモートブランチのリモート名（origin/main → origin）。</summary>
    public string? RemoteName => Kind == ReferenceKind.RemoteBranch ? ShortName.Split('/', 2)[0] : null;
    public bool IsHead { get; init; }
    /// <summary>upstream の完全名（refs/remotes/origin/main）。</summary>
    public string? Upstream { get; init; }
    public int Ahead { get; init; }
    public int Behind { get; init; }
    /// <summary>upstream が設定されているがリモートに無い（[gone]）。</summary>
    public bool UpstreamGone { get; init; }
    /// <summary>annotated tag か。</summary>
    public bool IsAnnotated { get; init; }
    /// <summary>タグのメッセージ・ブランチの先端コミットの件名。</summary>
    public string Subject { get; init; } = "";
    /// <summary>このブランチがチェックアウトされている worktree のパス（他の worktree なら worktree アイコン）。</summary>
    public string? WorktreePath { get; init; }
}

public sealed record StashInfo(int Index, string Sha, string Message, string BaseSha, DateTimeOffset Time)
{
    public string RefName => $"stash@{{{Index}}}";
}

public sealed record RemoteInfo(string Name, string FetchUrl, string? PushUrl);

/// <summary>進行中の操作（NotificationBar）。</summary>
/// <summary>Unmerged：merge などの印が無いのに競合したファイルがある（stash の適用・autostash を戻したときの競合。"Working directory contains 2 unmerged files"）。</summary>
public enum RepositoryOperation { None, Merge, Rebase, RebaseInteractive, ApplyMailbox, CherryPick, Revert, Bisect, Unmerged }

/// <summary>参照・stash・リモート・HEAD・進行中操作をまとめたもの。</summary>
public sealed class RepositoryReferences
{
    public required IReadOnlyList<ReferenceInfo> References { get; init; }
    public required IReadOnlyList<StashInfo> Stashes { get; init; }
    public required IReadOnlyList<RemoteInfo> Remotes { get; init; }
    /// <summary>現在のブランチの短い名前。detached なら null。</summary>
    public string? HeadBranch { get; init; }
    /// <summary>HEAD のコミット。空のリポジトリなら null。</summary>
    public string? HeadSha { get; init; }
    public RepositoryOperation Operation { get; init; }
    /// <summary>Git Flow の設定。初期化していなければ null。</summary>
    public GitFlowConfig? GitFlow { get; init; }

    public IEnumerable<ReferenceInfo> LocalBranches => References.Where(r => r.Kind == ReferenceKind.LocalBranch);
    public IEnumerable<ReferenceInfo> RemoteBranches => References.Where(r => r.Kind == ReferenceKind.RemoteBranch);
    public IEnumerable<ReferenceInfo> Tags => References.Where(r => r.Kind == ReferenceKind.Tag);
    public ReferenceInfo? Head => References.FirstOrDefault(r => r.IsHead);
}

/// <summary>
/// Git Flow の設定（.git/config の gitflow.*）。初期化済みかは設定のキー
/// （gitflow.branch.master と gitflow.branch.develop）で決める。git-flow があるかどうかは見ない。
/// </summary>
public sealed record GitFlowConfig(string Master, string Develop, string FeaturePrefix, string ReleasePrefix, string HotfixPrefix, string VersionTagPrefix)
{
    /// <summary>`config --get-regexp` の行（"key value"。値が空なら "key" だけ）から作る。gitflow.* 以外の行は無視する。</summary>
    public static GitFlowConfig? Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines)
        {
            var sp = line.IndexOf(' ');
            var key = sp < 0 ? line.Trim() : line[..sp];
            if (key.StartsWith("gitflow.", StringComparison.OrdinalIgnoreCase)) values[key] = sp < 0 ? "" : line[(sp + 1)..].Trim();
        }
        if (!values.TryGetValue("gitflow.branch.master", out var master) || !values.TryGetValue("gitflow.branch.develop", out var develop)) return null;
        string Prefix(string name, string fallback) => values.TryGetValue("gitflow.prefix." + name, out var v) ? v : fallback;
        return new GitFlowConfig(master, develop, Prefix("feature", "feature/"), Prefix("release", "release/"), Prefix("hotfix", "hotfix/"), Prefix("versiontag", ""));
    }

    /// <summary>ブランチが feature / release / hotfix のどれか（Finish の対象）。名前は接頭辞を外したもの。</summary>
    public (GitFlowKind Kind, string Name)? Classify(string branch)
    {
        foreach (var (kind, prefix) in new[] { (GitFlowKind.Feature, FeaturePrefix), (GitFlowKind.Release, ReleasePrefix), (GitFlowKind.Hotfix, HotfixPrefix) })
            if (prefix.Length > 0 && branch.StartsWith(prefix, StringComparison.Ordinal) && branch.Length > prefix.Length) return (kind, branch[prefix.Length..]);
        return null;
    }

    public string PrefixOf(GitFlowKind kind) => kind switch { GitFlowKind.Feature => FeaturePrefix, GitFlowKind.Release => ReleasePrefix, _ => HotfixPrefix };
}

public enum GitFlowKind { Feature, Release, Hotfix }

/// <summary>ファイルの状態の 1 文字（porcelain v2 の X / Y）。</summary>
public enum FileChange { Unmodified, Modified, TypeChanged, Added, Deleted, Renamed, Copied, Untracked, Ignored, Conflicted }

/// <summary>status の 1 エントリ（Local Changes の 1 行）。</summary>
public sealed record StatusEntry(string Path, FileChange Index, FileChange WorkTree)
{
    public string? OriginalPath { get; init; }
    public bool IsConflicted => Index == FileChange.Conflicted || WorkTree == FileChange.Conflicted;
    public bool IsUntracked => WorkTree == FileChange.Untracked;
    /// <summary>Staged リストに出るか。</summary>
    public bool HasStagedChange => !IsConflicted && Index is not (FileChange.Unmodified or FileChange.Untracked or FileChange.Ignored);
    /// <summary>Unstaged リストに出るか（競合と untracked も含む）。</summary>
    public bool HasUnstagedChange => IsConflicted || WorkTree is not (FileChange.Unmodified or FileChange.Ignored);
    /// <summary>submodule か。</summary>
    public bool IsSubmodule { get; init; }
}

public sealed class RepositoryStatus
{
    public required IReadOnlyList<StatusEntry> Entries { get; init; }
    public IEnumerable<StatusEntry> Staged => Entries.Where(e => e.HasStagedChange);
    public IEnumerable<StatusEntry> Unstaged => Entries.Where(e => e.HasUnstagedChange);
    public int ChangeCount => Entries.Count(e => e.HasStagedChange || e.HasUnstagedChange);
}

/// <summary>
/// 進行中の操作の詳細。Other は merge の相手・rebase の onto・cherry-pick / revert のコミット、Target は今のブランチ（rebase は書き換え中のブランチ）。
/// Step / Total は rebase の進み具合（1 始まり）。
/// </summary>
/// <param name="Amending">rebase が Edit で止まっているときのコミット（rebase-merge/amend の 7 桁）。それ以外は null。</param>
/// <param name="BisectReady">bisect で good と bad の両方に印がある（git が次のコミットを選んで待っている）。</param>
public sealed record OperationState(RepositoryOperation Operation, string Other, string Target, int Step = 0, int Total = 0, string? Amending = null, bool BisectReady = false);

/// <summary>競合の片側で、そのファイルを変えたコミット（競合の案内のカードに並べる）。</summary>
public sealed record ConflictCommit(string Sha, string AuthorName, string AuthorEmail, DateTimeOffset Time, string Subject)
{
    /// <summary>カードの "940122"（6 桁）。</summary>
    public string ShortSha => Sha[..Math.Min(6, Sha.Length)];
}

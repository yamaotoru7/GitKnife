using GitKnife.Core.Git.Model;
using GitKnife.Core.Graph;

namespace GitKnife.ViewModels;

public enum RefLabelKind { LocalBranch, CurrentBranch, RemoteBranch, Tag, Stash, Head }

/// <summary>今のブランチと upstream の差（ラベルの前の点。Push = 青、Pull = 灰色）。</summary>
public enum SyncMark { None, Push, Pull }

/// <summary>コミット一覧のラベル（ブランチ・タグ・stash）。IsSynced：upstream も同じコミットにある（リモートのラベルを省いて 1 つにまとめる）。</summary>
public sealed record RefLabel(string Text, RefLabelKind Kind, bool IsSynced = false)
{
    /// <summary>IsSynced のときの upstream の短い名前（"origin/bugfix/greeting"。コミット詳細の REFS では別に出す）。</summary>
    public string? SyncedUpstream { get; init; }
}

/// <summary>コミット一覧の 1 行。描画は RevisionListView が直接行う。</summary>
public sealed class CommitRow
{
    public required CommitInfo Commit { get; init; }
    public required GraphRow Graph { get; init; }
    public required IReadOnlyList<RefLabel> Labels { get; init; }
    /// <summary>HEAD のコミット（件名を太字に）。</summary>
    public bool IsHead { get; init; }
    /// <summary>HEAD から辿れる（辿れないコミットは件名を灰色にする）。</summary>
    public bool IsReachableFromHead { get; init; }
    /// <summary>どの参照（ブランチ・タグ・stash・HEAD）からもたどれない（Show Lost Commits で出る reflog だけのコミット）。右クリックに Merge を出さない。</summary>
    public bool IsLost { get; init; }
    /// <summary>push するコミット（今のブランチにあって upstream に無い）か、pull するコミット（upstream にあって今のブランチに無い）。</summary>
    public SyncMark Sync { get; init; }
    public bool HasSyncMark => Sync != SyncMark.None;
    public bool HasLabels => Labels.Count > 0;
    /// <summary>
    /// コミット詳細の REFS（stash のラベルは出さない）。一覧で 1 つにまとめたブランチと upstream は、
    /// 詳細では分けて出す（REFS "bugfix/greeting" "origin/bugfix/greeting"）。
    /// </summary>
    public IReadOnlyList<RefLabel> DetailLabels => Labels
        .Where(l => l.Kind != RefLabelKind.Stash)
        .SelectMany(l => l.SyncedUpstream is { } up ? [l, new RefLabel(up, RefLabelKind.RemoteBranch)] : new[] { l })
        .ToList();
    public bool HasDetailLabels => DetailLabels.Count > 0;
    public string DateText => Commit.AuthorTime.ToLocalTime().ToString("d M yyyy HH:mm");   // 表示 "5 1 2026 17:10"
    /// <summary>縦レイアウトの日付（時刻なし "5 1 2026"）。</summary>
    public string DateOnlyText => Commit.AuthorTime.ToLocalTime().ToString("d M yyyy");

    public static IReadOnlyList<CommitRow> Build(IReadOnlyList<CommitInfo> commits, IReadOnlyList<GraphRow> graph, RepositoryReferences refs, bool hideTags = false, IReadOnlyList<string>? hiddenReferences = null)
    {
        // 隠したブランチ・リモートブランチ（目の印）は一覧のラベルに出さない。upstream を隠したローカルブランチは雲を付けない
        // 詳細の REFS には upstream を残す
        bool Hidden(string canonical) => hiddenReferences is { Count: > 0 } h && Core.Git.ReferenceFilter.Matches(canonical, h);
        var bySha = new Dictionary<string, List<RefLabel>>();
        void Add(string sha, RefLabel l) { if (!bySha.TryGetValue(sha, out var list)) bySha[sha] = list = []; list.Add(l); }
        // upstream と同じコミットにあるローカルブランチは、リモートのラベルを省いて 1 つにまとめる（"☁ bugfix/greeting"）
        var bySha2 = refs.References.ToDictionary(r => r.CanonicalName, r => r.TargetSha);
        var mergedRemotes = new HashSet<string>();
        var shortNames = refs.References.ToDictionary(r => r.CanonicalName, r => r.ShortName);
        foreach (var r in refs.LocalBranches)
            if (r.Upstream is { } up && bySha2.TryGetValue(up, out var upSha) && upSha == r.TargetSha) mergedRemotes.Add(up);
        foreach (var r in refs.References)
        {
            switch (r.Kind)
            {
                case ReferenceKind.LocalBranch when !Hidden(r.CanonicalName):
                    var synced = r.Upstream is { } u && mergedRemotes.Contains(u);
                    Add(r.TargetSha, new RefLabel(r.ShortName, r.IsHead ? RefLabelKind.CurrentBranch : RefLabelKind.LocalBranch, synced && !Hidden(r.Upstream!))
                        { SyncedUpstream = synced ? shortNames.GetValueOrDefault(r.Upstream!) : null });
                    break;
                case ReferenceKind.RemoteBranch when !Hidden(r.CanonicalName):
                    if (!mergedRemotes.Contains(r.CanonicalName)) Add(r.TargetSha, new RefLabel(r.ShortName, RefLabelKind.RemoteBranch));
                    break;
                case ReferenceKind.Tag when !hideTags: Add(r.TargetSha, new RefLabel(r.ShortName, RefLabelKind.Tag)); break;
            }
        }
        foreach (var s in refs.Stashes) Add(s.Sha, new RefLabel(s.RefName, RefLabelKind.Stash));
        if (refs.HeadBranch is null && refs.HeadSha is not null) Add(refs.HeadSha, new RefLabel("HEAD", RefLabelKind.Head));

        // HEAD から親を辿れるコミットと、いずれかの参照から辿れるコミット
        var parents = new Dictionary<string, IReadOnlyList<string>>();
        foreach (var c in commits) parents.TryAdd(c.Sha, c.DisplayParents);
        HashSet<string> Reachable(IEnumerable<string> tips)
        {
            var seen = new HashSet<string>();
            var stack = new Stack<string>(tips);
            while (stack.Count > 0)
            {
                var sha = stack.Pop();
                if (!seen.Add(sha) || !parents.TryGetValue(sha, out var ps)) continue;
                foreach (var p in ps) stack.Push(p);
            }
            return seen;
        }
        var reachable = refs.HeadSha is not null ? Reachable([refs.HeadSha]) : [];
        // push / pull するコミット：HEAD と upstream の片方からだけ辿れるもの
        HashSet<string> toPush = [], toPull = [];
        if (refs.Head is { Upstream: { } upName } && refs.HeadSha is { } headSha && refs.References.FirstOrDefault(r => r.CanonicalName == upName) is { } upstreamRef)
        {
            var fromUp = Reachable([upstreamRef.TargetSha]);
            toPush = reachable.Where(s => !fromUp.Contains(s)).ToHashSet();
            toPull = fromUp.Where(s => !reachable.Contains(s)).ToHashSet();
        }
        var fromRefs = Reachable(refs.References.Select(r => r.TargetSha).Concat(refs.Stashes.Select(s => s.Sha)).Concat(refs.HeadSha is { } h ? [h] : []));

        var rows = new CommitRow[commits.Count];
        for (var i = 0; i < commits.Count; i++)
        {
            var c = commits[i];
            var labels = bySha.TryGetValue(c.Sha, out var l)
                ? l.OrderBy(x => x.Kind switch { RefLabelKind.Stash => 0, RefLabelKind.Head => 1, RefLabelKind.CurrentBranch => 2, RefLabelKind.LocalBranch => 3, RefLabelKind.RemoteBranch => 4, _ => 5 }).ToList()
                : [];
            rows[i] = new CommitRow { Commit = c, Graph = graph[i], Labels = labels, IsHead = c.Sha == refs.HeadSha, IsReachableFromHead = reachable.Contains(c.Sha) || c.IsStash, IsLost = !fromRefs.Contains(c.Sha),
                Sync = toPush.Contains(c.Sha) && !c.IsStash ? SyncMark.Push : toPull.Contains(c.Sha) ? SyncMark.Pull : SyncMark.None };
        }
        return rows;
    }
}

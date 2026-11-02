using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Graph;

/// <summary>
/// マージの折りたたみ。たたんだマージは最初の親だけにつなぎ、取り込んだ側のコミット（とそのラベル）を隠す。
/// たたむかどうかは worktree-config.toml の collapse_all_mode と collapse_toggled_shas で決まる：
/// collapse_all_mode が false なら toggled のマージだけたたむ。true ならすべてたたみ、toggled のマージだけ開く。
/// Collapse All Merges も一覧を読んだあとでたたむ（`--first-parent` だと、まだマージされていないブランチの先も出てしまう）。
/// </summary>
public static class MergeCollapse
{
    public static bool IsCollapsed(string sha, bool allMode, IReadOnlySet<string> toggled) => allMode ^ toggled.Contains(sha);

    /// <summary>
    /// 表示順のコミットから、隠すコミットを除き、たたんだマージの親を最初の親だけにしたものを返す（順序はそのまま）。
    /// 子の無いコミット（ブランチの先、stash など）から親をたどり、たたんだマージでは最初の親だけを追う。たどり着かなかったコミットを隠す。
    /// </summary>
    public static IReadOnlyList<CommitInfo> Apply(IReadOnlyList<CommitInfo> commits, Func<string, bool> isCollapsed)
    {
        var bySha = new Dictionary<string, CommitInfo>();
        foreach (var c in commits) bySha.TryAdd(c.Sha, c);
        bool Collapses(CommitInfo c) => c.Parents.Count > 1 && !c.IsStash && isCollapsed(c.Sha);
        if (!commits.Any(Collapses)) return commits;

        var hasChild = new HashSet<string>();
        foreach (var c in commits)
            foreach (var p in c.Parents) hasChild.Add(p);

        var visible = new HashSet<string>();
        var stack = new Stack<string>(commits.Where(c => !hasChild.Contains(c.Sha)).Select(c => c.Sha));
        while (stack.Count > 0)
        {
            var sha = stack.Pop();
            if (!bySha.TryGetValue(sha, out var c) || !visible.Add(sha)) continue;
            var parents = Collapses(c) ? c.Parents.Take(1) : c.Parents;
            foreach (var p in parents) stack.Push(p);
        }

        var result = new List<CommitInfo>(visible.Count);
        var added = new HashSet<string>();
        foreach (var c in commits)
        {
            if (!visible.Contains(c.Sha) || !added.Add(c.Sha)) continue;
            result.Add(Collapses(c) ? c with { Parents = [c.Parents[0]], AllParents = c.AllParents ?? c.Parents, IsCollapsedMerge = true } : c);
        }
        return result;
    }
}

using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Git;

/// <summary>
/// サイドバーの Filter / Hide（worktree-config.toml の filter_references / hidden_references）。
/// 値は完全名で、フォルダは末尾が "/"（"refs/heads/feature/"）。フォルダのボタンはリモートの同じフォルダも入れる（"refs/remotes/origin/feature/"）。
/// - Filter があればそれに当てはまる参照から辿れるコミットだけ（HEAD も stash も出さない）。
/// - Hide は当てはまる参照を辿り始めから外す（そこからしか辿れないコミットが消える。stash は残る）。
/// </summary>
public static class ReferenceFilter
{
    /// <summary>
    /// View > Filter by Active Branch は "HEAD" を入れる。今のブランチとその upstream に置き換える（帯は "'main', 'origin/main'"）。
    /// detached のときは "HEAD" のまま。
    /// </summary>
    public static IReadOnlyList<string> Expand(RepositoryReferences refs, IReadOnlyList<string> patterns)
    {
        if (!patterns.Contains("HEAD") || refs.HeadBranch is not { } head) return patterns;
        var list = new List<string>();
        foreach (var p in patterns)
        {
            if (p != "HEAD") { if (!list.Contains(p)) list.Add(p); continue; }
            var canonical = "refs/heads/" + head;
            if (!list.Contains(canonical)) list.Add(canonical);
            if (refs.References.FirstOrDefault(r => r.CanonicalName == canonical)?.Upstream is { } up && !list.Contains(up)) list.Add(up);
        }
        return list;
    }

    public static bool Matches(string canonicalName, IEnumerable<string> patterns)
        => patterns.Any(p => p.EndsWith('/') ? canonicalName.StartsWith(p, StringComparison.Ordinal) : canonicalName == p);

    /// <summary>コミット一覧の辿り始め（null なら既定の --branches --remotes --tags HEAD）。</summary>
    public static IReadOnlyList<string>? StartPoints(RepositoryReferences refs, IReadOnlyList<string> filter, IReadOnlyList<string> hidden)
    {
        filter = Expand(refs, filter);
        if (filter.Count > 0)
        {
            var only = refs.References.Where(r => Matches(r.CanonicalName, filter) && !Matches(r.CanonicalName, hidden)).Select(r => r.CanonicalName).ToList();
            if (filter.Contains("HEAD") && refs.HeadSha is not null) only.Add("HEAD");   // detached の Filter by Active Branch
            return only;
        }
        if (hidden.Count == 0) return null;
        var list = refs.References
            .Where(r => r.Kind is ReferenceKind.LocalBranch or ReferenceKind.RemoteBranch or ReferenceKind.Tag && !Matches(r.CanonicalName, hidden))
            .Select(r => r.CanonicalName).ToList();
        var headBranch = refs.HeadBranch is { } h ? "refs/heads/" + h : null;
        if (refs.HeadSha is not null && (headBranch is null || !Matches(headBranch, hidden))) list.Add("HEAD");
        return list;
    }

    /// <summary>サイドバーの行（ブランチかフォルダ）から作るパターン。ローカルのフォルダと各リモートの同じフォルダ。</summary>
    public static IReadOnlyList<string> PatternsFor(string canonicalOrFolder, bool isFolder, IEnumerable<string> remotes)
    {
        if (!isFolder) return [canonicalOrFolder];
        var folder = canonicalOrFolder.TrimEnd('/') + "/";
        if (!folder.StartsWith("refs/heads/", StringComparison.Ordinal)) return [folder];
        var rest = folder["refs/heads/".Length..];
        return [folder, .. remotes.Select(r => $"refs/remotes/{r}/{rest}")];
    }

    /// <summary>帯に出す名前（"refs/heads/feature/" → "'feature/*'"、"refs/remotes/origin/feature/" → "'origin/feature/*'"）。</summary>
    public static string Display(string pattern)
    {
        var s = pattern.StartsWith("refs/heads/", StringComparison.Ordinal) ? pattern["refs/heads/".Length..]
            : pattern.StartsWith("refs/remotes/", StringComparison.Ordinal) ? pattern["refs/remotes/".Length..]
            : pattern.StartsWith("refs/tags/", StringComparison.Ordinal) ? pattern["refs/tags/".Length..] : pattern;
        return "'" + (s.EndsWith('/') ? s + "*" : s) + "'";
    }
}

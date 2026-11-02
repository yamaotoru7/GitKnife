using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Git;

/// <summary>Search タブの検索の種類（config.toml の recent_revision_search_queries の type と同じ値）。</summary>
public enum RevisionSearchType { Message, Author, DiffPath, DiffContent }

/// <summary>検索の範囲（scope："repository" / "currentBranch"）。</summary>
public enum RevisionSearchScope { Repository, CurrentBranch }

/// <summary>最近の検索の 1 つ（`[[recent_revision_search_queries]]` の type / scope / search_string）。</summary>
public sealed record RevisionSearchQuery(RevisionSearchType Type, RevisionSearchScope Scope, string SearchString)
{
    public static string TypeKey(RevisionSearchType t) => t switch
    {
        RevisionSearchType.Author => "author",
        RevisionSearchType.DiffPath => "diffPath",
        RevisionSearchType.DiffContent => "diffContent",
        _ => "message",
    };

    public static RevisionSearchType ParseType(string? s) => s switch
    {
        "author" => RevisionSearchType.Author,
        "diffPath" => RevisionSearchType.DiffPath,
        "diffContent" => RevisionSearchType.DiffContent,
        _ => RevisionSearchType.Message,
    };

    public static string ScopeKey(RevisionSearchScope s) => s == RevisionSearchScope.CurrentBranch ? "currentBranch" : "repository";
    public static RevisionSearchScope ParseScope(string? s) => s == "currentBranch" ? RevisionSearchScope.CurrentBranch : RevisionSearchScope.Repository;
}

/// <summary>検索結果の 1 コミット（Diff Content / Path のときは一致したファイルも）。</summary>
public sealed record RevisionSearchResult(string Sha, string AuthorName, string AuthorEmail, DateTimeOffset AuthorTime, string Subject, IReadOnlyList<RevisionSearchFile> Files);

public sealed record RevisionSearchFile(string Path, FileChange Change);

/// <summary>
/// サイドバーの Search タブ。`git log` の引数：
/// Commit Message は `--grep=q --regexp-ignore-case`、Author は `--author=q --regexp-ignore-case`、Path は `--name-status -- :(icase)q`、
/// Diff Content は `--name-status -G q`。範囲が Repository なら `HEAD --branches --remotes --tags` を付ける（Current Branch は何も付けない＝HEAD）。
/// </summary>
public static class RevisionSearch
{
    private const string Sep = "\u0001";

    public static string[] Args(RevisionSearchQuery q)
    {
        var args = new List<string> { "log", "--no-show-signature", $"--pretty=format:%H{Sep}%aN{Sep}%aE{Sep}%at{Sep}%s" };
        if (q.Scope == RevisionSearchScope.Repository) args.AddRange(["HEAD", "--branches", "--remotes", "--tags"]);
        switch (q.Type)
        {
            case RevisionSearchType.Message: args.AddRange([$"--grep={q.SearchString}", "--regexp-ignore-case", "--"]); break;
            case RevisionSearchType.Author: args.AddRange([$"--author={q.SearchString}", "--regexp-ignore-case", "--"]); break;
            case RevisionSearchType.DiffPath: args.AddRange(["--name-status", "--", $":(icase){q.SearchString}"]); break;
            case RevisionSearchType.DiffContent: args.AddRange(["--name-status", "-G", q.SearchString, "--"]); break;
        }
        return [.. args];
    }

    /// <summary>`git log` の出力を読む。コミットの行（区切り文字を含む）の後に --name-status の "M\tpath" が続く。</summary>
    public static IReadOnlyList<RevisionSearchResult> Parse(string output)
    {
        var results = new List<RevisionSearchResult>();
        List<RevisionSearchFile>? files = null;
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.Contains(Sep, StringComparison.Ordinal))
            {
                var f = line.Split(Sep);
                if (f.Length < 5) continue;
                files = [];
                var time = long.TryParse(f[3], out var t) ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime() : DateTimeOffset.MinValue;
                results.Add(new RevisionSearchResult(f[0], f[1], f[2], time, f[4], files));
                continue;
            }
            var tab = line.IndexOf('\t');
            if (files is null || tab <= 0) continue;
            var path = line[(tab + 1)..];
            var tab2 = path.LastIndexOf('\t');   // R100\told\tnew は新しい方
            if (tab2 >= 0) path = path[(tab2 + 1)..];
            files.Add(new RevisionSearchFile(path, line[0] switch
            {
                'A' => FileChange.Added,
                'D' => FileChange.Deleted,
                'R' => FileChange.Renamed,
                'C' => FileChange.Copied,
                'T' => FileChange.TypeChanged,
                _ => FileChange.Modified,
            }));
        }
        return results;
    }

    public static async Task<IReadOnlyList<RevisionSearchResult>> RunAsync(GitProcess git, string workTree, RevisionSearchQuery q, CancellationToken ct = default)
    {
        var r = await git.RunAsync(new GitRequest(workTree, Args(q)), ct).ConfigureAwait(false);
        return r.Succeeded ? Parse(r.StandardOutput) : [];
    }
}

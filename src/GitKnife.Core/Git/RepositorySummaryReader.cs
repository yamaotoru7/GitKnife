namespace GitKnife.Core.Git;

/// <summary>Repository Manager の Summary の 1 行（ブランチ・タグ・コミット）。</summary>
public sealed record SummaryItem(string Name, string Subject, string Author, string Email, DateTimeOffset Date);

/// <summary>Repository Manager の右側（RepositoryDetailsUserControl）に出す要約。</summary>
public sealed class RepositorySummary
{
    public int UncommittedFiles { get; init; }
    public int Commits { get; init; }
    public DateTimeOffset? InitialCommit { get; init; }
    public DateTimeOffset? LastCommit { get; init; }
    public IReadOnlyList<string> Remotes { get; init; } = [];
    /// <summary>リモートの fetch の URL（Issues / Pull Requests のリンクを作る）。</summary>
    public IReadOnlyDictionary<string, string> RemoteUrls { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<SummaryItem> Branches { get; init; } = [];
    public IReadOnlyList<SummaryItem> Tags { get; init; } = [];
    public IReadOnlyList<SummaryItem> RecentCommits { get; init; } = [];
    public string? ReadmeName { get; init; }
    public string? ReadmeText { get; init; }
}

/// <summary>
/// Repository Manager の要約を読む。数字の意味：
/// Uncommitted Files は untracked を除いた変更（basic で 2）、Commits は `rev-list --all --count`（stash のコミットも数える。12）、
/// Branches はリモートブランチを新しい順（origin/HEAD、origin/main、origin/bugfix/greeting）、Tags も新しい順、Commits は全体の新しい順 5 件（stash も出る）。
/// </summary>
public sealed class RepositorySummaryReader(GitProcess git, string workingDirectory)
{
    private const int MaxItems = 5;

    private async Task<string> Run(string[] args, CancellationToken ct)
    {
        var r = await git.RunAsync(new GitRequest(workingDirectory, args), ct).ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput : "";
    }

    public async Task<RepositorySummary> ReadAsync(CancellationToken ct = default)
    {
        var status = Run(["--no-optional-locks", "status", "--porcelain", "--untracked-files=no"], ct);
        var count = Run(["rev-list", "--all", "--count"], ct);
        var first = Run(["log", "--all", "--max-parents=0", "--format=%ct"], ct);
        var last = Run(["log", "--all", "-1", "--format=%ct"], ct);
        var remotes = Run(["remote", "-v"], ct);
        const string RefFormat = "--format=%(refname)%00%(subject)%00%(if)%(taggername)%(then)%(taggername)%(else)%(authorname)%(end)%00%(if)%(taggeremail)%(then)%(taggeremail)%(else)%(authoremail)%(end)%00%(creatordate:unix)";
        var branches = Run(["for-each-ref", "--sort=-creatordate", $"--count={MaxItems}", RefFormat, "refs/remotes"], ct);
        var tags = Run(["for-each-ref", "--sort=-creatordate", $"--count={MaxItems}", RefFormat, "refs/tags"], ct);
        var commits = Run(["log", "--all", "--date-order", $"-{MaxItems}", "--format=%s%x00%aN%x00%aE%x00%ct"], ct);
        await Task.WhenAll(status, count, first, last, remotes, branches, tags, commits).ConfigureAwait(false);

        var readme = FindReadme();
        return new RepositorySummary
        {
            UncommittedFiles = Lines(status.Result).Count,
            Commits = int.TryParse(count.Result.Trim(), out var n) ? n : 0,
            InitialCommit = Lines(first.Result).Select(ParseTime).Where(t => t is not null).Min(),
            LastCommit = ParseTime(last.Result.Trim()),
            Remotes = ParseRemotes(remotes.Result).Keys.ToList(),
            RemoteUrls = ParseRemotes(remotes.Result),
            Branches = ParseRefs(branches.Result, "refs/remotes/"),
            Tags = ParseRefs(tags.Result, "refs/tags/"),
            RecentCommits = Lines(commits.Result).Select(l => l.Split('\0')).Where(p => p.Length >= 4)
                .Select(p => new SummaryItem(p[0], p[0], p[1], p[2], ParseTime(p[3]) ?? default)).ToList(),
            ReadmeName = readme is null ? null : Path.GetFileName(readme),
            ReadmeText = readme is null ? null : await HostFile.ReadAllTextAsync(readme, ct).ConfigureAwait(false),
        };
    }

    /// <summary>`remote -v` の出力（"origin\t&lt;url&gt; (fetch)"）から、名前と fetch の URL を出てきた順に。</summary>
    public static IReadOnlyDictionary<string, string> ParseRemotes(string output)
    {
        var result = new Dictionary<string, string>();
        foreach (var line in Lines(output))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0) continue;
            var url = line[(tab + 1)..];
            var kind = url.LastIndexOf(" (", StringComparison.Ordinal);
            if (kind >= 0) url = url[..kind];
            result.TryAdd(line[..tab], url);
        }
        return result;
    }

    public static IReadOnlyList<SummaryItem> ParseRefs(string output, string prefix)
        => Lines(output).Select(l => l.Split('\0')).Where(p => p.Length >= 5)
            .Select(p => new SummaryItem(p[0].StartsWith(prefix, StringComparison.Ordinal) ? p[0][prefix.Length..] : p[0], p[1], p[2], p[3].Trim('<', '>'), ParseTime(p[4]) ?? default))
            .ToList();

    private string? FindReadme()
    {
        try
        {
            return HostDirectory.EnumerateFiles(workingDirectory)
                .Where(f => Path.GetFileName(f).StartsWith("README", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => Path.GetExtension(f).Equals(".md", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .FirstOrDefault();
        }
        catch (IOException) { return null; }
    }

    private static List<string> Lines(string text) => text.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static DateTimeOffset? ParseTime(string s) => long.TryParse(s, out var t) ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime() : null;

    /// <summary>"9 months ago" のような相対時刻（Repository Manager の表示）。</summary>
    public static string Relative(DateTimeOffset? time, DateTimeOffset now)
    {
        if (time is not { } t) return "";
        var d = now - t;
        if (d.TotalMinutes < 1) return "just now";
        if (d.TotalHours < 1) return Unit((int)d.TotalMinutes, "minute");
        if (d.TotalDays < 1) return Unit((int)d.TotalHours, "hour");
        if (d.TotalDays < 30) return Unit((int)d.TotalDays, "day");
        if (d.TotalDays < 365) return Unit((int)(d.TotalDays / 30), "month");
        return Unit((int)(d.TotalDays / 365), "year");
    }

    private static string Unit(int n, string unit) => n == 1 ? $"1 {unit} ago" : $"{n} {unit}s ago";
}

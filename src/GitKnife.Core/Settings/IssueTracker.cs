using System.Text;
using System.Text.RegularExpressions;

namespace GitKnife.Core.Settings;

/// <summary>Issue Tracker のルール 1 つ（Repository Settings > Issue Tracker）。</summary>
public sealed record IssueTrackerRule(string Name, string Regex, string Url, bool Shared)
{
    /// <summary>正規表現として読めるか（RegexStatusTextBlock の valid / invalid。空は invalid）。</summary>
    public bool IsValid
    {
        get
        {
            if (Regex.Length == 0) return false;
            try { _ = new Regex(Regex); return true; }
            catch (ArgumentException) { return false; }
        }
    }

    /// <summary>ファイルに書くのは名前があり、正規表現が読めて、URL があるものだけ（空の新しいルールは書かない）。</summary>
    public bool IsComplete => Name.Trim().Length > 0 && IsValid && Url.Trim().Length > 0;
}

/// <summary>メッセージの中の一致（リンクにする範囲と URL）。</summary>
public sealed record IssueLink(int Start, int Length, string Url);

/// <summary>
/// Issue Tracker のルールのファイル。Local は `.git/issuetracker`、Shared は作業ツリーの `.issuetracker`（RuleLocationTextBlock の文字のとおり）。
/// git config と同じ書式で、次のように書く（改行は CRLF、末尾も CRLF、文字列の中の \ は \\）：
/// <code>
/// # Integration with Issue Tracker
/// #
/// # (note that '\' need to be escaped).
///
/// [issuetracker "Sample GitHub Rule"]
///   regex = "#(\\d+)"
///   url = "https://github.com/username/repository/issues/$1"
/// </code>
/// </summary>
public static partial class IssueTracker
{
    public const string LocalFileName = "issuetracker";
    public const string SharedFileName = ".issuetracker";

    /// <summary>+ のメニューのサンプル。</summary>
    public static readonly IssueTrackerRule SampleGitHub = new("Sample GitHub Rule", @"#(\d+)", "https://github.com/username/repository/issues/$1", false);
    public static readonly IssueTrackerRule SampleJira = new("Sample Jira Rule", @"PROJ-(\d+)", "https://jira.yourcompany.com/browse/PROJ-$1", false);
    public static readonly IssueTrackerRule SampleJiraMultiproject = new("Sample Jira Multiproject Rule", @"(PRJ1|PRJ2)-(\d+)", "https://jira.yourcompany.com/browse/$1-$2", false);
    /// <summary>Add New Rule の既定の名前。</summary>
    public const string NewRuleName = "Issue Tracker Rule";
    /// <summary>Test Message の初めの文。</summary>
    public const string SampleMessage = "Fix documentation of the release process (#12337)";

    public static string LocalPath(string gitDirectory) => Path.Combine(gitDirectory, LocalFileName);
    public static string SharedPath(string worktree) => Path.Combine(worktree, SharedFileName);

    /// <summary>Local と Shared のルール（Local が先。どちらもファイルの順）。</summary>
    public static IReadOnlyList<IssueTrackerRule> Load(string gitDirectory, string worktree)
        => [.. LoadFile(LocalPath(gitDirectory), shared: false), .. LoadFile(SharedPath(worktree), shared: true)];

    public static IReadOnlyList<IssueTrackerRule> LoadFile(string path, bool shared)
    {
        try { return Git.HostFile.Exists(path) ? Parse(Git.HostFile.ReadAllText(path), shared) : []; }
        catch (IOException) { return []; }
    }

    public static IReadOnlyList<IssueTrackerRule> Parse(string text, bool shared)
    {
        var rules = new List<IssueTrackerRule>();
        string? name = null, regex = null, url = null;
        void Flush()
        {
            if (name is not null) rules.Add(new IssueTrackerRule(name, regex ?? "", url ?? "", shared));
            name = regex = url = null;
        }
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            if (Section().Match(line) is { Success: true } s) { Flush(); name = Unescape(s.Groups[1].Value); continue; }
            if (name is null || KeyValue().Match(line) is not { Success: true } kv) continue;
            var value = kv.Groups[2].Value.Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = Unescape(value[1..^1]);
            switch (kv.Groups[1].Value.ToLowerInvariant())
            {
                case "regex": regex = value; break;
                case "url": url = value; break;
            }
        }
        Flush();
        return rules;
    }

    /// <summary>ファイルに書く形の文字列（完成したルールだけ）。</summary>
    public static string Serialize(IEnumerable<IssueTrackerRule> rules)
    {
        var sb = new StringBuilder();
        sb.Append("# Integration with Issue Tracker\r\n#\r\n# (note that '\\' need to be escaped).\r\n");
        foreach (var r in rules.Where(r => r.IsComplete))
        {
            sb.Append("\r\n[issuetracker \"").Append(Escape(r.Name)).Append("\"]\r\n");
            sb.Append("  regex = \"").Append(Escape(r.Regex)).Append("\"\r\n");
            sb.Append("  url = \"").Append(Escape(r.Url)).Append("\"\r\n");
        }
        return sb.ToString();
    }

    /// <summary>Local / Shared のファイルを書く。書くルールが無ければファイルを消す。</summary>
    public static void Save(string gitDirectory, string worktree, IEnumerable<IssueTrackerRule> rules)
    {
        var all = rules.ToList();
        Write(LocalPath(gitDirectory), all.Where(r => !r.Shared));
        Write(SharedPath(worktree), all.Where(r => r.Shared));
    }

    private static void Write(string path, IEnumerable<IssueTrackerRule> rules)
    {
        var list = rules.Where(r => r.IsComplete).ToList();
        if (list.Count == 0)
        {
            if (Git.HostFile.Exists(path)) Git.HostFile.Delete(path);
            return;
        }
        var text = Serialize(list);
        if (Git.HostFile.Exists(path) && Git.HostFile.ReadAllText(path) == text) return;
        Git.HostFile.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>メッセージの中の一致を URL にする（$1、$2 … をグループで置き換える）。重なる一致は先に見つけた方。</summary>
    public static IReadOnlyList<IssueLink> FindLinks(string message, IEnumerable<IssueTrackerRule> rules)
    {
        var links = new List<IssueLink>();
        foreach (var rule in rules.Where(r => r.IsValid && r.Url.Length > 0))
        {
            foreach (Match m in new Regex(rule.Regex).Matches(message))
            {
                if (m.Length == 0 || links.Any(l => m.Index < l.Start + l.Length && l.Start < m.Index + m.Length)) continue;
                var url = Placeholder().Replace(rule.Url, p => int.Parse(p.Groups[1].Value) is var g && g < m.Groups.Count ? m.Groups[g].Value : p.Value);
                links.Add(new IssueLink(m.Index, m.Length, url));
            }
        }
        return links.OrderBy(l => l.Start).ToList();
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Unescape(string s)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '\\' && i + 1 < s.Length) { sb.Append(s[++i] switch { 'n' => '\n', 't' => '\t', var c => c }); continue; }
            sb.Append(s[i]);
        }
        return sb.ToString();
    }

    [GeneratedRegex(@"^\[issuetracker\s+""(.*)""\]$", RegexOptions.IgnoreCase)]
    private static partial Regex Section();

    [GeneratedRegex(@"^([A-Za-z]+)\s*=\s*(.*)$")]
    private static partial Regex KeyValue();

    [GeneratedRegex(@"\$(\d+)")]
    private static partial Regex Placeholder();
}

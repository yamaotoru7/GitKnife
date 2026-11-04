using System.Text;

namespace GitKnife.Core.Reporting;

/// <summary>添付する画像（Ctrl+Shift+F12 のときの画面）。</summary>
public sealed record BugReportImage(string FileName, byte[] Png);

/// <summary>
/// バグレポート（Ctrl+Shift+F12。2026-10-28 ユーザーの依頼）。画面のキャプチャ、説明、環境、ログの末尾。
/// 送る前に必ず %LocalAppData%\GitKnife\bug-reports\&lt;日時&gt;\ に保存する（送れなくても残る。Claude が後で読める）。
/// </summary>
public sealed class BugReport
{
    public const int LogTailLines = 200;

    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public List<BugReportImage> Images { get; } = [];
    /// <summary>環境（版、OS、git、開いているリポジトリなど）。表にする。</summary>
    public List<KeyValuePair<string, string>> Environment { get; } = [];
    public string? LogTail { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    /// <summary>
    /// New issue の本文の先頭に入れる目印（表示されない HTML コメント）。担当が付いていなくても、GitKnife のバグ報告で作った Issue を見分けられるように。
    /// </summary>
    public const string IssueMarker = "<!-- gitknife-bug-report -->";

    public static string DefaultDirectory => Path.Combine(ProductInfo.AppDataDirectory, "bug-reports");

    /// <summary>
    /// Issue の本文（Markdown）。imageUrls が null なら保存用（画像はファイル名で参照）、空なら画像を載せない（添付する前の本文）。
    /// </summary>
    public string ToMarkdown(IReadOnlyList<string>? imageUrls = null) => Body(imageUrls, LogTail, null);

    /// <summary>本文。log はログのうち載せる部分、logSummary は折りたたみの見出し（null なら "Log (last N lines)"）。</summary>
    private string Body(IReadOnlyList<string>? imageUrls, string? log, string? logSummary)
    {
        var sb = new StringBuilder();
        if (Description.Trim().Length > 0) sb.AppendLine(Description.Trim()).AppendLine();
        for (var i = 0; i < Images.Count && (imageUrls is null || i < imageUrls.Count); i++)
            sb.AppendLine($"![{Images[i].FileName}]({imageUrls?[i] ?? Images[i].FileName})").AppendLine();
        if (Environment.Count > 0)
        {
            sb.AppendLine("| | |").AppendLine("|---|---|");
            foreach (var (k, v) in Environment) sb.AppendLine($"| {Escape(k)} | {Escape(v)} |");
            sb.AppendLine();
        }
        if (log is { Length: > 0 })
        {
            sb.AppendLine($"<details><summary>{logSummary ?? $"Log (last {LogTailLines} lines)"}</summary>").AppendLine();
            var fence = log.Contains("```") ? "~~~~" : "```";
            sb.AppendLine(fence).AppendLine(log.TrimEnd()).AppendLine(fence).AppendLine().AppendLine("</details>");
        }
        else if (logSummary is not null) sb.AppendLine(logSummary);
        // 改行は LF にそろえる（AppendLine は Windows で CRLF）
        return sb.ToString().Replace("\r\n", "\n").TrimEnd() + "\n";
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");

    /// <summary>フォルダに保存する（report.md、画像、log.txt）。保存したフォルダを返す。</summary>
    public string Save(string? baseDirectory = null)
    {
        var root = baseDirectory ?? DefaultDirectory;
        var dir = Path.Combine(root, CreatedAt.ToString("yyyyMMdd-HHmmss"));
        for (var n = 2; Directory.Exists(dir); n++) dir = Path.Combine(root, $"{CreatedAt:yyyyMMdd-HHmmss}-{n}");
        Directory.CreateDirectory(dir);
        foreach (var img in Images) File.WriteAllBytes(Path.Combine(dir, img.FileName), img.Png);
        File.WriteAllText(Path.Combine(dir, "report.md"), $"# {Title}\n\n" + ToMarkdown(), new UTF8Encoding(false));
        if (LogTail is { Length: > 0 }) File.WriteAllText(Path.Combine(dir, LogFileName), LogTail.TrimEnd() + "\n", new UTF8Encoding(false));
        return dir;
    }

    /// <summary>ログの末尾 n 行（読めなければ null。書き込み中のファイルも読めるように共有で開く）。</summary>
    public static string? ReadLogTail(string path, int lines = LogTailLines)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var all = reader.ReadToEnd().Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            return string.Join("\n", all.Skip(Math.Max(0, all.Length - lines)));
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>保存したフォルダに書くログ全体のファイル名（GitHub も Forgejo も .txt は Issue に添付できる）。</summary>
    public const string LogFileName = "log.txt";

    /// <summary>GitHub の New issue の URL に載せる長さの上限（ブラウザとサーバーが受け付ける長さより短めに）。</summary>
    public const int MaxGitHubUrlLength = 7500;

    /// <summary>
    /// New issue の画面の URL（題名と本文を入れておく。GitHub と Forgejo は同じ形）。画像は保存したフォルダから貼ってもらう。
    /// GitLab は /-/issues/new で、題名と本文は issue[title] と issue[description]（/issues/new は /-/ 付きへリダイレクトされ、
    /// そのとき長いクエリが Location に入ってサーバーの前段が 502 を返すことがある）。
    /// 長すぎるときは、ログを新しい側から入るだけ載せ、残りは保存したフォルダの log.txt を貼ってもらう。
    /// </summary>
    /// <param name="kind">サーバーの種類（<see cref="ServerProbe"/>）。Unknown ならホスト名で推測する。</param>
    public string NewIssueUrl(string serverUrl, string repository, ServerKind kind = ServerKind.Unknown, int maxLength = MaxGitHubUrlLength)
    {
        var gitLab = kind.OrGuess(serverUrl) == ServerKind.GitLab;
        var baseUrl = $"{serverUrl.Trim().TrimEnd('/')}/{repository.Trim().Trim('/')}/{(gitLab ? "-/" : "")}issues/new";
        var (titleKey, bodyKey) = gitLab ? ("issue%5Btitle%5D", "issue%5Bdescription%5D") : ("title", "body");
        var note = IssueMarker + "\n" + (Images.Count > 0 ? $"<!-- Drag the screenshots ({string.Join(", ", Images.Select(i => i.FileName))}) from the saved report folder here. -->\n" : "") + "\n";
        string Make(string body) => $"{baseUrl}?{titleKey}={Uri.EscapeDataString(Title)}&{bodyKey}={Uri.EscapeDataString(note + body)}";
        var url = Make(ToMarkdown([]));
        if (url.Length <= maxLength || LogTail is not { Length: > 0 }) return url;

        // 載せる行数を二分探索で決める（新しい行ほど大事なので末尾から）
        var lines = LogTail.TrimEnd().Split('\n');
        string With(int n) => Make(Body([], string.Join("\n", lines[^n..]),
            n > 0 ? $"Log (last {n} of {lines.Length} lines; drag {LogFileName} from the saved report folder for all)"
                  : $"(Drag {LogFileName} from the saved report folder for the log.)"));
        int lo = 0, hi = lines.Length - 1;
        while (lo < hi)
        {
            var mid = (lo + hi + 1) / 2;
            if (With(mid).Length <= maxLength) lo = mid; else hi = mid - 1;
        }
        return With(lo);
    }
}

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
    /// <summary>作った Issue の担当（Report a Bug の "Assign to …"。#8）。保存する report.md には入れない。</summary>
    public List<string> Assignees { get; } = [];
    public DateTime CreatedAt { get; init; } = DateTime.Now;

    public static string DefaultDirectory => Path.Combine(ProductInfo.AppDataDirectory, "bug-reports");

    /// <summary>
    /// Issue の本文（Markdown）。imageUrls が null なら保存用（画像はファイル名で参照）、空なら画像を載せない（添付する前の本文）。
    /// </summary>
    public string ToMarkdown(IReadOnlyList<string>? imageUrls = null)
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
        if (LogTail is { Length: > 0 })
        {
            sb.AppendLine($"<details><summary>Log (last {LogTailLines} lines)</summary>").AppendLine();
            var fence = LogTail.Contains("```") ? "~~~~" : "```";
            sb.AppendLine(fence).AppendLine(LogTail.TrimEnd()).AppendLine(fence).AppendLine().AppendLine("</details>");
        }
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

    /// <summary>
    /// Forgejo に送る：Issue を作り、画像を添付し、本文に画像を埋める。作った Issue を返す。
    /// </summary>
    public async Task<CreatedIssue> SendAsync(ForgejoIssueClient client, CancellationToken ct = default)
    {
        var issue = await client.CreateIssueAsync(Title, ToMarkdown([]), Assignees, ct);
        if (Images.Count == 0) return issue;
        var urls = new List<string>();
        foreach (var img in Images) urls.Add(await client.UploadAttachmentAsync(issue.Number, img.FileName, img.Png, "image/png", ct));
        await client.UpdateBodyAsync(issue.Number, ToMarkdown(urls), ct);
        return issue;
    }

    /// <summary>GitHub の New issue の URL に載せる長さの上限（ブラウザとサーバーが受け付ける長さより短めに）。</summary>
    public const int MaxGitHubUrlLength = 7500;

    /// <summary>
    /// GitHub の New issue の画面の URL（題名と本文を入れておく）。GitHub はトークンなしでは Issue を作れず、画像も API で添付できないので、
    /// ブラウザで開いて、保存したフォルダの画像を貼ってもらう。長すぎるときはログを本文から外す（保存したフォルダの report.md にある）。
    /// </summary>
    public string GitHubNewIssueUrl(string serverUrl, string repository)
    {
        var baseUrl = $"{serverUrl.Trim().TrimEnd('/')}/{repository.Trim().Trim('/')}/issues/new";
        var note = Images.Count > 0 ? $"<!-- Drag the screenshots ({string.Join(", ", Images.Select(i => i.FileName))}) from the saved report folder here. -->\n\n" : "";
        string Make(string body) => $"{baseUrl}?title={Uri.EscapeDataString(Title)}&body={Uri.EscapeDataString(note + body)}";
        var url = Make(ToMarkdown([]));
        if (url.Length <= MaxGitHubUrlLength || LogTail is null) return url;
        var log = LogTail;
        LogTail = null;
        try { return Make(ToMarkdown([]) + "\n(The log is in report.md in the saved report folder.)\n"); }
        finally { LogTail = log; }
    }
}

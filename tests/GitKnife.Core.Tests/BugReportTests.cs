using GitKnife.Core.Reporting;

namespace GitKnife.Core.Tests;

public class BugReportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GitKnife.Tests", Guid.NewGuid().ToString("N"));

    public BugReportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static BugReport Sample()
    {
        var r = new BugReport { Title = "Tab color is lost", Description = "Steps:\n1. ...", CreatedAt = new DateTime(2026, 10, 28, 12, 34, 56) };
        r.Images.Add(new BugReportImage("main.png", [1, 2, 3]));
        r.Environment.Add(new("GitKnife", "0.1.0"));
        r.Environment.Add(new("Repository", "D:\\a|b"));
        r.LogTail = "line1\nline2";
        return r;
    }

    [Fact]
    public void MarkdownHasDescriptionImagesEnvironmentAndLog()
    {
        var md = Sample().ToMarkdown(["https://x/attachments/1"]);
        Assert.StartsWith("Steps:\n1. ...\n", md);
        Assert.Contains("![main.png](https://x/attachments/1)", md);
        Assert.Contains("| Repository | D:\\a\\|b |", md);
        Assert.Contains("```\nline1\nline2\n```", md);
        // 添付する前の本文には画像を載せない、保存用はファイル名で参照
        Assert.DoesNotContain("![", Sample().ToMarkdown([]));
        Assert.Contains("![main.png](main.png)", Sample().ToMarkdown());
    }

    /// <summary>New issue の画面に題名と本文を入れて開く。長すぎればログを新しい側から入るだけ載せる。</summary>
    [Fact]
    public void NewIssueUrlCarriesTitleAndBody()
    {
        var url = Sample().NewIssueUrl("https://github.com/", "owner/gitknife");
        Assert.StartsWith("https://github.com/owner/gitknife/issues/new?title=Tab%20color%20is%20lost&body=", url);
        var body = Uri.UnescapeDataString(url[(url.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        Assert.StartsWith(BugReport.IssueMarker + "\n", body);   // 担当が無くても見分ける目印
        Assert.Contains("main.png", body);   // 貼ってもらう画像の名前
        Assert.DoesNotContain("![", body);
        Assert.Contains("line1\nline2", body);

        var big = Sample();
        big.LogTail = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"line {i:000} " + new string('x', 60)));
        var cut = big.NewIssueUrl("https://git.example.com", "owner/gitknife", 3000);
        Assert.True(cut.Length <= 3000);
        var cutBody = Uri.UnescapeDataString(cut);
        Assert.Contains("line 200 ", cutBody);          // 新しい行は残る
        Assert.DoesNotContain("line 001 ", cutBody);    // 古い行は落とす
        Assert.Contains("of 200 lines; drag log.txt", cutBody);

        // ログの 1 行も入らない長さでも、log.txt の案内は出る
        var none = Uri.UnescapeDataString(big.NewIssueUrl("https://github.com", "owner/gitknife", 100));
        Assert.Contains("(Drag log.txt from the saved report folder for the log.)", none);
        Assert.DoesNotContain("line 200 ", none);
    }

    [Fact]
    public void SavesToTimestampedFolder()
    {
        var dir = Sample().Save(_dir);
        Assert.Equal("20261028-123456", Path.GetFileName(dir));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(dir, "main.png")));
        Assert.StartsWith("# Tab color is lost\n", File.ReadAllText(Path.Combine(dir, "report.md")));
        Assert.Equal("line1\nline2\n", File.ReadAllText(Path.Combine(dir, "log.txt")));
        // 同じ秒にもう 1 つ
        Assert.Equal("20261028-123456-2", Path.GetFileName(Sample().Save(_dir)));
    }

    [Fact]
    public void ReadsLogTail()
    {
        var path = Path.Combine(_dir, "a.log");
        File.WriteAllText(path, string.Join("\r\n", Enumerable.Range(1, 10)) + "\r\n");
        Assert.Equal("8\n9\n10", BugReport.ReadLogTail(path, 3));
        Assert.Null(BugReport.ReadLogTail(Path.Combine(_dir, "missing.log")));
    }
}

using System.Net;
using System.Text;
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

    /// <summary>GitHub は New issue の画面に題名と本文を入れて開く。長すぎればログを外す。</summary>
    [Fact]
    public void GitHubNewIssueUrlCarriesTitleAndBody()
    {
        var url = Sample().GitHubNewIssueUrl("https://github.com/", "owner/gitknife");
        Assert.StartsWith("https://github.com/owner/gitknife/issues/new?title=Tab%20color%20is%20lost&body=", url);
        var body = Uri.UnescapeDataString(url[(url.IndexOf("&body=", StringComparison.Ordinal) + 6)..]);
        Assert.Contains("main.png", body);   // 貼ってもらう画像の名前
        Assert.DoesNotContain("![", body);
        Assert.Contains("line1\nline2", body);

        var big = Sample();
        big.LogTail = new string('x', BugReport.MaxGitHubUrlLength);
        var cut = big.GitHubNewIssueUrl("https://github.com", "owner/gitknife");
        Assert.True(cut.Length <= BugReport.MaxGitHubUrlLength);
        Assert.Contains("report.md", Uri.UnescapeDataString(cut));
        Assert.NotNull(big.LogTail);
    }

    [Fact]
    public void SavesToTimestampedFolder()
    {
        var dir = Sample().Save(_dir);
        Assert.Equal("20261028-123456", Path.GetFileName(dir));
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(dir, "main.png")));
        Assert.StartsWith("# Tab color is lost\n", File.ReadAllText(Path.Combine(dir, "report.md")));
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

    [Fact]
    public async Task SendsIssueAttachmentsAndBodyToForgejo()
    {
        var handler = new RecordingHandler();
        using var client = new ForgejoIssueClient("https://forge.example/sub/", "owner/gitknife", "tok", handler);
        var issue = await Sample().SendAsync(client);

        Assert.Equal(7, issue.Number);
        Assert.Equal("https://forge.example/sub/owner/gitknife/issues/7", issue.HtmlUrl);
        Assert.Equal([
            "POST https://forge.example/sub/api/v1/repos/owner/gitknife/issues",
            "POST https://forge.example/sub/api/v1/repos/owner/gitknife/issues/7/assets?name=main.png",
            "PATCH https://forge.example/sub/api/v1/repos/owner/gitknife/issues/7",
        ], handler.Requests.Select(r => r.Line));
        Assert.All(handler.Requests, r => Assert.Equal("token tok", r.Auth));
        Assert.Contains("\"title\":\"Tab color is lost\"", handler.Requests[0].Body);
        Assert.DoesNotContain("assignees", handler.Requests[0].Body);
        Assert.Contains("name=attachment", handler.Requests[1].Body);
        Assert.Contains("![main.png](https://forge.example/attachments/abc)", handler.Requests[2].Body.Replace("\\u0021", "!"));
    }

    [Fact]
    public async Task AssignsTheIssueWhenAsked()
    {
        var handler = new RecordingHandler();
        using var client = new ForgejoIssueClient("https://forge.example", "owner/gitknife", "tok", handler);
        var report = Sample();
        report.Assignees.Add("ai-assistant");
        await report.SendAsync(client);
        Assert.Contains("\"assignees\":[\"ai-assistant\"]", handler.Requests[0].Body);
        Assert.DoesNotContain("ai-assistant", report.ToMarkdown());
    }

    [Fact]
    public async Task ReportsServerError()
    {
        var handler = new RecordingHandler { Fail = true };
        using var client = new ForgejoIssueClient("https://forge.example", "owner/gitknife", "bad", handler);
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => Sample().SendAsync(client));
        Assert.Contains("401", ex.Message);
        Assert.Throws<ArgumentException>(() => new ForgejoIssueClient("https://forge.example", "gitknife", "t"));
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public bool Fail { get; init; }
        public List<(string Line, string? Auth, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct);
            Requests.Add(($"{request.Method} {request.RequestUri}", request.Headers.Authorization?.ToString(), body));
            if (Fail) return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("{\"message\":\"token is required\"}") };
            var json = request.RequestUri!.AbsolutePath.EndsWith("/assets")
                ? """{ "id": 1, "name": "main.png", "browser_download_url": "https://forge.example/attachments/abc" }"""
                : """{ "number": 7, "html_url": "https://forge.example/sub/owner/gitknife/issues/7" }""";
            return new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}

using System.Net;
using System.Text;
using GitKnife.Core.Settings;

namespace GitKnife.Core.Tests;

public class ServerProbeTests
{
    /// <summary>GitLab：/api/v4/version がサインインしていなければ 401 の JSON（X-Gitlab-Meta ヘッダー付き）。</summary>
    [Fact]
    public async Task DetectsGitLab()
    {
        var server = new FakeServer(url => url == "http://gitlab.corp/api/v4/version"
            ? Json(HttpStatusCode.Unauthorized, """{"message":"401 Unauthorized"}""", gitLabMeta: true)
            : Html(HttpStatusCode.Found));
        Assert.Equal(ServerKind.GitLab, await ServerProbe.DetectAsync("http://gitlab.corp/", server));
        // リポジトリの URL にグループが入っていても、API はサーバーの根で見る
        Assert.Equal(ServerKind.GitLab, await ServerProbe.DetectAsync("http://gitlab.corp/group/", server));
    }

    /// <summary>Forgejo：/api/v4 は 404、/api/v1/version はサインインが要るサーバーでも 403 の JSON。</summary>
    [Fact]
    public async Task DetectsForgejo()
    {
        var server = new FakeServer(url => url == "https://git.example.com/api/v1/version"
            ? Json(HttpStatusCode.Forbidden, """{"message":"Only signed in user is allowed to call APIs."}""")
            : new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("Not found.") });
        Assert.Equal(ServerKind.Forgejo, await ServerProbe.DetectAsync("https://git.example.com/", server));
        Assert.Equal(["https://git.example.com/api/v4/version", "https://git.example.com/api/v1/version"], server.Requests);
    }

    /// <summary>github.com は問い合わせない。どちらでもなければ Unknown、通信できなければ null（覚えない）。</summary>
    [Fact]
    public async Task GitHubUnknownAndUnreachable()
    {
        var none = new FakeServer(_ => throw new HttpRequestException("no route"));
        Assert.Equal(ServerKind.GitHub, await ServerProbe.DetectAsync("https://github.com/", none));
        Assert.Empty(none.Requests);
        Assert.Null(await ServerProbe.DetectAsync("https://git.example.com/", none));
        Assert.Equal(ServerKind.Unknown, await ServerProbe.DetectAsync("https://git.example.com/", new FakeServer(_ => Html(HttpStatusCode.NotFound))));
    }

    [Fact]
    public void GuessesFromHostName()
    {
        Assert.Equal(ServerKind.GitHub, ServerProbe.Guess("https://github.com/"));
        Assert.Equal(ServerKind.GitLab, ServerProbe.Guess("http://gitlab.corp.example/"));
        Assert.Equal(ServerKind.Forgejo, ServerProbe.Guess("https://git.example.com/"));
        Assert.Equal(ServerKind.GitLab, ServerKind.GitLab.OrGuess("https://git.example.com/"));
        Assert.Equal(ServerKind.Forgejo, ServerKind.Unknown.OrGuess("https://git.example.com/"));
    }

    /// <summary>覚えた種類はサーバーの URL（末尾の / と大文字小文字を問わない）で引く。</summary>
    [Fact]
    public void SettingsRememberKindPerServer()
    {
        var s = new AppSettings();
        Assert.Null(s.GetServerKind("http://gitlab.corp/"));
        s.SetServerKind("http://GitLab.corp/", ServerKind.GitLab);
        Assert.Equal(ServerKind.GitLab, s.GetServerKind("http://gitlab.corp"));
        Assert.Null(s.GetServerKind("https://git.example.com/"));
    }

    /// <summary>判定した種類で New issue の URL の形を決める（ホスト名に gitlab が入らない GitLab でも）。</summary>
    [Fact]
    public void NewIssueUrlFollowsDetectedKind()
    {
        var report = new Reporting.BugReport { Title = "t" };
        Assert.StartsWith("https://code.corp/team/app/-/issues/new?issue%5Btitle%5D=t&", report.NewIssueUrl("https://code.corp/", "team/app", ServerKind.GitLab));
        Assert.StartsWith("https://code.corp/team/app/issues/new?title=t&", report.NewIssueUrl("https://code.corp/", "team/app"));
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body, bool gitLabMeta = false)
    {
        var r = new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        if (gitLabMeta) r.Headers.Add("X-Gitlab-Meta", """{"version":"1"}""");
        return r;
    }

    private static HttpResponseMessage Html(HttpStatusCode status) =>
        new(status) { Content = new StringContent("<html></html>", Encoding.UTF8, "text/html") };

    private sealed class FakeServer(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            return Task.FromResult(respond(url));
        }
    }
}

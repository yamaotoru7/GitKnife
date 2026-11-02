using System.Net;
using System.Security.Cryptography;
using System.Text;
using GitKnife.Core.Update;

namespace GitKnife.Core.Tests;

public class UpdateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GitKnife.Tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Theory]
    [InlineData("v0.1.1", "0.1.1")]
    [InlineData("0.2", "0.2.0")]
    [InlineData("v1.0.0-beta.2", "1.0.0")]
    public void ParsesTags(string tag, string expected) => Assert.Equal(Version.Parse(expected), UpdateVersion.Parse(tag));

    [Fact]
    public void ComparesWithCurrentVersion()
    {
        static ReleaseInfo R(string tag) => new(tag, "", "", "", []);
        Assert.True(UpdateVersion.IsNewer(R("v0.1.1"), "0.1.0"));
        Assert.False(UpdateVersion.IsNewer(R("v0.1.0"), "0.1.0"));
        Assert.False(UpdateVersion.IsNewer(R("nightly"), "0.1.0"));
    }

    [Fact]
    public void OneNewReleaseKeepsItsNotesAsTheyAre()
    {
        var latest = Notes("v0.1.7", "GitKnife 0.1.7。\n\n- b");
        Assert.Equal(latest.Body, UpdateVersion.CombineNotes([latest, Notes("v0.1.6", "a")], latest, "0.1.6"));
    }

    [Fact]
    public void SkippedReleasesAreCombinedNewestFirst()
    {
        var latest = Notes("v0.1.7", "GitKnife 0.1.7。\n\n- c");
        ReleaseInfo[] releases = [Notes("v0.1.5", "GitKnife 0.1.5。\r\n\r\n- a"), Notes("v0.1.4", "old"), Notes("v0.1.6", "GitKnife 0.1.6。"), Notes("v0.1.8", "ahead of latest"), Notes("nightly", "no version")];
        Assert.Equal("## 0.1.7\n\n- c\n\n## 0.1.6\n\n(No release notes)\n\n## 0.1.5\n\n- a", UpdateVersion.CombineNotes(releases, latest, "0.1.4"));
    }

    private static ReleaseInfo Notes(string tag, string body) => new(tag, "", body, "", []);

    /// <summary>今の版が出たページで止める。下書きとプレリリースは入れない。</summary>
    [Fact]
    public async Task ReadsReleasesUntilCurrentVersion()
    {
        var server = new FakeForgejo();
        var store = new MemoryStore { Tokens = new OAuthTokens("access-1", "r", DateTimeOffset.UtcNow.AddHours(1)) };
        using var client = new UpdateClient("https://forge.example/", "owner/gitknife", "client-1", "", store, server);
        var releases = await client.GetReleasesSinceAsync("0.1.8");

        Assert.Equal(["v0.2.0", "v0.1.9"], releases.Select(r => r.TagName));
        Assert.Equal(["?limit=50&per_page=50&page=1", "?limit=50&per_page=50&page=2"], server.ReleasePages);
    }

    /// <summary>GitHub は api.github.com をサインインせずに読む（トークンを付けない）。</summary>
    [Fact]
    public async Task GitHubReadsReleasesWithoutSignIn()
    {
        var server = new FakeGitHub();
        using var client = new UpdateClient("https://github.com/", "owner/gitknife", "client-1", "", new MemoryStore(), server);
        var latest = await client.GetLatestReleaseAsync(openBrowser: null);

        Assert.Equal(new Version(0, 2, 0), latest!.Version);
        Assert.Equal(["https://api.github.com/repos/owner/gitknife/releases/latest"], server.Requests);
        Assert.Null(server.LastAuthorization);
    }

    /// <summary>Client ID が空の Forgejo もサインインせずに読む。</summary>
    [Fact]
    public async Task ForgejoWithoutClientIdDoesNotSignIn()
    {
        var server = new FakeGitHub();
        using var client = new UpdateClient("https://forge.example/", "owner/gitknife", "", "", new MemoryStore(), server);
        await client.GetLatestReleaseAsync(openBrowser: null);
        Assert.Equal(["https://forge.example/api/v1/repos/owner/gitknife/releases/latest"], server.Requests);
        Assert.Null(server.LastAuthorization);
    }

    /// <summary>どの URL にも同じリリースを返す（認証を求めない）。</summary>
    private sealed class FakeGitHub : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];
        public string? LastAuthorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            LastAuthorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"tag_name":"v0.2.0","name":"0.2.0","body":"notes","assets":[]}""", Encoding.UTF8, "application/json"),
            });
        }
    }

    /// <summary>ブラウザの代わりに戻り先へ GET し、認可コードが PKCE 付きでトークンに替わること。</summary>
    [Fact]
    public async Task BrowserSignInExchangesCodeWithPkce()
    {
        var server = new FakeForgejo();
        using var http = new HttpClient(server);
        var oauth = new ForgejoOAuth(http, "https://forge.example/", "client-1", "read:repository");
        var tokens = await oauth.AuthorizeInBrowserAsync(url => _ = FakeBrowser.FollowAsync(url, "the-code"));

        Assert.Equal("access-1", tokens.AccessToken);
        Assert.Equal("refresh-1", tokens.RefreshToken);
        var form = server.TokenForms.Single();
        Assert.Equal("authorization_code", form["grant_type"]);
        Assert.Equal("the-code", form["code"]);
        Assert.Equal("client-1", form["client_id"]);
        Assert.StartsWith("http://127.0.0.1:", form["redirect_uri"]);
        // code_challenge = BASE64URL(SHA256(code_verifier))
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"]))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(challenge, FakeBrowser.LastQuery["code_challenge"]);
        Assert.Equal("read:repository", FakeBrowser.LastQuery["scope"]);
    }

    [Fact]
    public async Task RefusedSignInThrows()
    {
        using var http = new HttpClient(new FakeForgejo());
        var oauth = new ForgejoOAuth(http, "https://forge.example/", "client-1");
        var ex = await Assert.ThrowsAsync<OAuthException>(() => oauth.AuthorizeInBrowserAsync(url => _ = FakeBrowser.FollowAsync(url, null, error: "access_denied")));
        Assert.Contains("access_denied", ex.Message);
    }

    /// <summary>期限切れのトークンは refresh token で取り直し、Bearer で最新のリリースを読む。</summary>
    [Fact]
    public async Task LatestReleaseRefreshesExpiredToken()
    {
        var server = new FakeForgejo();
        var store = new MemoryStore { Tokens = new OAuthTokens("old", "refresh-0", DateTimeOffset.UtcNow.AddMinutes(-5)) };
        using var client = new UpdateClient("https://forge.example/", "owner/gitknife", "client-1", "", store, server);
        var latest = await client.GetLatestReleaseAsync(openBrowser: null);

        Assert.NotNull(latest);
        Assert.Equal(new Version(0, 2, 0), latest!.Version);
        Assert.Equal("GitKnife-0.2.0-setup.exe", latest.Installer!.Name);
        Assert.Equal("refresh_token", server.TokenForms.Single()["grant_type"]);
        Assert.Equal("access-1", store.Tokens!.AccessToken);
        Assert.Equal("Bearer access-1", server.LastAuthorization);
    }

    [Fact]
    public async Task WithoutTokenAndBrowserSignInIsRequired()
    {
        using var client = new UpdateClient("https://forge.example/", "owner/gitknife", "client-1", "", new MemoryStore(), new FakeForgejo());
        await Assert.ThrowsAsync<SignInRequiredException>(() => client.GetLatestReleaseAsync(openBrowser: null));
    }

    [Fact]
    public async Task DownloadChecksSha256()
    {
        var server = new FakeForgejo();
        var store = new MemoryStore { Tokens = new OAuthTokens("access-1", "r", DateTimeOffset.UtcNow.AddHours(1)) };
        using var client = new UpdateClient("https://forge.example/", "owner/gitknife", "client-1", "", store, server);
        var latest = (await client.GetLatestReleaseAsync(null))!;
        var path = await client.DownloadAsync(latest, latest.Installer!, _dir, null);
        Assert.Equal(FakeForgejo.Installer, File.ReadAllBytes(path));

        server.CorruptChecksum = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadAsync(latest, latest.Installer!, _dir, null));
        Assert.False(File.Exists(path + ".part"));
    }

    private sealed class MemoryStore : ITokenStore
    {
        public OAuthTokens? Tokens { get; set; }
        public OAuthTokens? Load() => Tokens;
        public void Save(OAuthTokens? tokens) => Tokens = tokens;
    }

    /// <summary>許可画面を開いた「ふり」：URL から戻り先と state を読み、戻り先に GET する。</summary>
    private static class FakeBrowser
    {
        public static Dictionary<string, string> LastQuery { get; private set; } = [];

        public static async Task FollowAsync(string authorizeUrl, string? code, string? error = null)
        {
            var query = new Uri(authorizeUrl).Query.TrimStart('?').Split('&')
                .Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
            LastQuery = query;
            using var http = new HttpClient();
            // 関係ない要求（favicon）は無視されること
            (await http.GetAsync(query["redirect_uri"] + "favicon.ico")).Dispose();
            var back = error is null ? $"?code={code}&state={query["state"]}" : $"?error={error}&state={query["state"]}";
            (await http.GetAsync(query["redirect_uri"] + back)).Dispose();
        }
    }

    private sealed class FakeForgejo : HttpMessageHandler
    {
        public static readonly byte[] Installer = Encoding.ASCII.GetBytes("pretend this is an installer");
        public List<Dictionary<string, string>> TokenForms { get; } = [];
        public string? LastAuthorization { get; private set; }
        public bool CorruptChecksum { get; set; }
        public List<string> ReleasePages { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/login/oauth/access_token")
            {
                var body = await request.Content!.ReadAsStringAsync(ct);
                TokenForms.Add(body.Split('&').Select(p => p.Split('=', 2)).ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1].Replace('+', ' '))));
                return Json("""{"access_token":"access-1","refresh_token":"refresh-1","token_type":"bearer","expires_in":3600}""");
            }
            LastAuthorization = request.Headers.Authorization?.ToString();
            if (LastAuthorization != "Bearer access-1") return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            var hash = Convert.ToHexStringLower(SHA256.HashData(Installer));
            if (CorruptChecksum) hash = new string('0', 64);
            if (path == "/api/v1/repos/owner/gitknife/releases")
            {
                ReleasePages.Add(request.RequestUri.Query);
                return Json(request.RequestUri.Query.EndsWith("page=1", StringComparison.Ordinal)
                    ? """[{"tag_name":"v0.3.0","draft":true},{"tag_name":"v0.2.1","prerelease":true},{"tag_name":"v0.2.0"}]"""
                    : """[{"tag_name":"v0.1.9"},{"tag_name":"v0.1.8"},{"tag_name":"v0.1.7"}]""");
            }
            return path switch
            {
                "/api/v1/repos/owner/gitknife/releases/latest" => Json($$"""
                    {"tag_name":"v0.2.0","name":"0.2.0","body":"notes","html_url":"https://forge.example/r","assets":[
                      {"name":"GitKnife-0.2.0-setup.exe","size":{{Installer.Length}},"browser_download_url":"https://forge.example/dl/setup.exe"},
                      {"name":"GitKnife-0.2.0-setup.exe.sha256","size":90,"browser_download_url":"https://forge.example/dl/setup.exe.sha256"}]}
                    """),
                "/dl/setup.exe" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Installer) },
                "/dl/setup.exe.sha256" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent($"{hash}  GitKnife-0.2.0-setup.exe") },
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
        }

        private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}

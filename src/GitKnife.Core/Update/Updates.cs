using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using System.Net.Http.Json;
using GitKnife.Core.Logging;

namespace GitKnife.Core.Update;

public sealed record ReleaseAsset(string Name, long Size, string DownloadUrl);

/// <summary>Forgejo か GitHub のリリース 1 件。タグ "v0.1.1" を版として読む。</summary>
public sealed record ReleaseInfo(string TagName, string Name, string Body, string HtmlUrl, IReadOnlyList<ReleaseAsset> Assets)
{
    public Version? Version => UpdateVersion.Parse(TagName);

    /// <summary>インストーラ（GitKnife-x.y.z-setup.exe）。</summary>
    public ReleaseAsset? Installer => Assets.FirstOrDefault(a => a.Name.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase));

    /// <summary>"&lt;インストーラ&gt;.sha256"（build-installer.ps1 が作る。無いリリースもある）。</summary>
    public ReleaseAsset? ChecksumFor(ReleaseAsset asset) => Assets.FirstOrDefault(a => a.Name.Equals(asset.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
}

public static class UpdateVersion
{
    /// <summary>"v0.1.1"、"0.1.1"、"0.2.0-beta" → 0.1.1 / 0.2.0。読めなければ null。</summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim().TrimStart('v', 'V');
        var cut = s.IndexOfAny(['-', '+', ' ']);
        if (cut >= 0) s = s[..cut];
        if (!Version.TryParse(s, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    public static bool IsNewer(ReleaseInfo release, string currentVersion) =>
        release.Version is { } r && Parse(currentVersion) is { } c && r > c;

    /// <summary>
    /// 今の版から latest までのリリースノートを新しい順につなぐ。飛ばす版が無ければ latest の本文そのまま。
    /// 複数なら版ごとに "## 0.1.7" の見出しを付け、本文の 1 行目の "GitKnife 0.1.7。" は見出しと重なるので除く。
    /// </summary>
    public static string CombineNotes(IEnumerable<ReleaseInfo> releases, ReleaseInfo latest, string currentVersion)
    {
        var skipped = releases.Append(latest)
            .Where(r => IsNewer(r, currentVersion) && r.Version <= latest.Version)
            .DistinctBy(r => r.Version)
            .OrderByDescending(r => r.Version)
            .ToList();
        if (skipped.Count <= 1) return latest.Body;
        return string.Join("\n\n", skipped.Select(r => $"## {r.Version}\n\n{BodyWithoutTitle(r)}"));
    }

    private static string BodyWithoutTitle(ReleaseInfo release)
    {
        var body = release.Body.Trim();
        var title = $"{ProductInfo.Name} {release.Version}。";
        if (body == title) return "(No release notes)";
        if (body.StartsWith(title + "\n", StringComparison.Ordinal) || body.StartsWith(title + "\r\n", StringComparison.Ordinal)) body = body[title.Length..].Trim();
        return body.Length > 0 ? body : "(No release notes)";
    }
}

/// <summary>サインインが要るが、対話できない（起動時の自動の確認）。</summary>
public sealed class SignInRequiredException() : Exception("Sign-in to the update server is required.");

/// <summary>トークンの置き場所（アプリは Windows の資格情報マネージャー）。</summary>
public interface ITokenStore
{
    OAuthTokens? Load();
    void Save(OAuthTokens? tokens);
}

/// <summary>
/// 更新の確認とダウンロード（Forgejo か GitHub のリリースにある Inno Setup のインストーラ）。
/// clientId があれば OAuth2 のトークンを付けて呼ぶ（サインインした人にしか API を見せない Forgejo 用）。
/// 期限切れなら refresh token で取り直し、それもだめなら（対話できるときだけ）ブラウザでサインインしてもらう。
/// clientId が空か GitHub なら、サインインせずに読む。
/// </summary>
public sealed class UpdateClient : IDisposable
{
    /// <summary>リリースの一覧を読むページ数の上限（page を無視するサーバーでも止まるように）。</summary>
    private const int MaxReleasePages = 20;

    private readonly HttpClient _http;
    private readonly string _repoApi;
    private readonly ForgejoOAuth? _oauth;
    private readonly ITokenStore _store;

    public UpdateClient(string serverUrl, string repository, string clientId, string scope, ITokenStore store, HttpMessageHandler? handler = null)
    {
        var parts = repository.Trim().Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0)) throw new ArgumentException($"Repository must be 'owner/repo': '{repository}'");
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = Timeout.InfiniteTimeSpan;   // ダウンロードが長い。個々の呼び出しは ct で切る
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ProductInfo.Name, ProductInfo.Version));
        var repo = $"{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}";
        if (Distribution.IsGitHub(serverUrl))
        {
            _repoApi = $"https://api.github.com/repos/{repo}";
            _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }
        else
        {
            _repoApi = $"{serverUrl.Trim().TrimEnd('/')}/api/v1/repos/{repo}";
            if (!string.IsNullOrWhiteSpace(clientId)) _oauth = new ForgejoOAuth(_http, serverUrl, clientId, scope);
        }
        _store = store;
    }

    /// <summary>最新のリリース（無ければ null）。</summary>
    /// <param name="openBrowser">null なら対話しない（サインインが要れば SignInRequiredException）。</param>
    public async Task<ReleaseInfo?> GetLatestReleaseAsync(Action<string>? openBrowser, CancellationToken ct = default)
    {
        using var r = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{_repoApi}/releases/latest"), openBrowser, ct).ConfigureAwait(false);
        if (r.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(r, "Reading the latest release", ct).ConfigureAwait(false);
        var dto = await r.Content.ReadFromJsonAsync<ReleaseDto>(ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Empty response");
        return ToReleaseInfo(dto);
    }

    /// <summary>今の版より新しい公開済みのリリース（下書きとプレリリースは除く）。サーバーは新しい順に返すので、今の版以下が出たページで止める。</summary>
    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesSinceAsync(string currentVersion, CancellationToken ct = default)
    {
        var found = new List<ReleaseInfo>();
        for (var page = 1; page <= MaxReleasePages; page++)
        {
            using var r = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, $"{_repoApi}/releases?limit=50&per_page=50&page={page}"), null, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(r, "Reading the releases", ct).ConfigureAwait(false);
            var items = await r.Content.ReadFromJsonAsync<List<ReleaseDto>>(ct).ConfigureAwait(false) ?? [];
            if (items.Count == 0) break;
            var releases = items.Where(d => !d.Draft && !d.Prerelease).Select(ToReleaseInfo).ToList();
            found.AddRange(releases.Where(x => UpdateVersion.IsNewer(x, currentVersion)));
            if (releases.Any(x => x.Version is not null && !UpdateVersion.IsNewer(x, currentVersion))) break;
        }
        return found;
    }

    private static ReleaseInfo ToReleaseInfo(ReleaseDto dto) =>
        new(dto.TagName ?? "", dto.Name ?? "", dto.Body ?? "", dto.HtmlUrl ?? "",
            (dto.Assets ?? []).Select(a => new ReleaseAsset(a.Name ?? "", a.Size, a.BrowserDownloadUrl ?? "")).ToList());

    /// <summary>
    /// インストーラを落として、大きさと（あれば）.sha256 を照合する。途中のファイルは .part に書き、照合できてから名前を替える。
    /// </summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, string directory, IProgress<double>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Path.GetFileName(asset.Name));
        var part = path + ".part";
        string? expectedHash = null;
        if (release.ChecksumFor(asset) is { } sum)
        {
            using var hr = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, sum.DownloadUrl), null, ct).ConfigureAwait(false);
            await EnsureSuccessAsync(hr, $"Downloading {sum.Name}", ct).ConfigureAwait(false);
            expectedHash = (await hr.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim().Split([' ', '\t', '\r', '\n'], 2)[0].ToLowerInvariant();
        }

        using (var r = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl), null, ct, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
        {
            await EnsureSuccessAsync(r, $"Downloading {asset.Name}", ct).ConfigureAwait(false);
            var total = r.Content.Headers.ContentLength ?? asset.Size;
            await using var src = await r.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = File.Create(part);
            var buffer = new byte[81920];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        var size = new FileInfo(part).Length;
        if (asset.Size > 0 && size != asset.Size) { File.Delete(part); throw new InvalidDataException($"Downloaded {size} bytes, expected {asset.Size}."); }
        if (expectedHash is not null)
        {
            string actual;
            await using (var f = File.OpenRead(part)) actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(f, ct).ConfigureAwait(false));
            if (actual != expectedHash) { File.Delete(part); throw new InvalidDataException($"SHA-256 mismatch: {actual} (expected {expectedHash})."); }
        }
        File.Move(part, path, overwrite: true);
        Log.Info($"Update downloaded: {path} ({size} bytes{(expectedHash is null ? ", no checksum" : ", SHA-256 ok")})");
        return path;
    }

    /// <summary>トークンを付けて送る。401 なら取り直して 1 回だけ送り直す。サインインしないサーバーならそのまま送る。</summary>
    private async Task<HttpResponseMessage> SendAuthorizedAsync(Func<HttpRequestMessage> make, Action<string>? openBrowser, CancellationToken ct, HttpCompletionOption option = HttpCompletionOption.ResponseContentRead)
    {
        if (_oauth is null)
        {
            using var request = make();
            return await _http.SendAsync(request, option, ct).ConfigureAwait(false);
        }
        var tokens = await GetTokensAsync(openBrowser, forceRenew: false, ct).ConfigureAwait(false);
        var r = await SendAsync(make, tokens, option, ct).ConfigureAwait(false);
        if (r.StatusCode != HttpStatusCode.Unauthorized) return r;
        r.Dispose();
        Log.Info("Update server returned 401; renewing the token");
        tokens = await GetTokensAsync(openBrowser, forceRenew: true, ct).ConfigureAwait(false);
        return await SendAsync(make, tokens, option, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(Func<HttpRequestMessage> make, OAuthTokens tokens, HttpCompletionOption option, CancellationToken ct)
    {
        using var request = make();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return await _http.SendAsync(request, option, ct).ConfigureAwait(false);
    }

    private async Task<OAuthTokens> GetTokensAsync(Action<string>? openBrowser, bool forceRenew, CancellationToken ct)
    {
        if (_oauth is null) throw new InvalidOperationException("This update server does not use sign-in.");
        var tokens = _store.Load();
        if (tokens is not null && !forceRenew && !tokens.IsExpired(DateTimeOffset.UtcNow)) return tokens;
        if (tokens?.RefreshToken is { Length: > 0 } refresh)
        {
            try
            {
                tokens = await _oauth.RefreshAsync(refresh, ct).ConfigureAwait(false);
                _store.Save(tokens);
                return tokens;
            }
            catch (OAuthException ex) { Log.Info($"Refreshing the update token failed: {ex.Message}"); }
        }
        if (openBrowser is null) throw new SignInRequiredException();
        Log.Info("Opening the browser to sign in to the update server");
        tokens = await _oauth.AuthorizeInBrowserAsync(openBrowser, ct).ConfigureAwait(false);
        _store.Save(tokens);
        return tokens;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage r, string what, CancellationToken ct)
    {
        if (r.IsSuccessStatusCode) return;
        var text = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (text.Length > 300) text = text[..300];
        throw new HttpRequestException($"{what} failed: {(int)r.StatusCode} {r.ReasonPhrase} {text}".Trim(), null, r.StatusCode);
    }

    public void Dispose() => _http.Dispose();

    private sealed class ReleaseDto
    {
        [JsonPropertyName("tag_name")] public string? TagName { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("body")] public string? Body { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        [JsonPropertyName("draft")] public bool Draft { get; set; }
        [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
        [JsonPropertyName("assets")] public List<AssetDto>? Assets { get; set; }
    }

    private sealed class AssetDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

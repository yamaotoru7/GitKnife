using System.Net.Http.Headers;
using GitKnife.Core.Logging;

namespace GitKnife.Core;

/// <summary>更新とバグ報告の送り先のサーバーの種類（API と New issue の URL の形が違う）。</summary>
public enum ServerKind
{
    Unknown,
    GitHub,
    GitLab,
    Forgejo,
}

/// <summary>
/// 送り先のサーバーの種類を決める。github.com はホスト名で決め、それ以外はサインインせずに API を 1〜2 回呼んで見分ける：
/// - GitLab：/api/v4/version が JSON を返す（サインインしていなければ 401）か、応答に X-Gitlab-Meta ヘッダーがある。
/// - Forgejo（Gitea）：/api/v1/version が JSON を返す（サインインが要るサーバーでも 401／403 の JSON）。
/// どちらでもなければ Unknown。通信できなければ null（覚えずに次の機会にもう一度）。
/// </summary>
public static class ServerProbe
{
    /// <summary>判定に使える情報が無いときの推測（ホスト名だけ）。github.com → GitHub、ホスト名に gitlab → GitLab、それ以外 → Forgejo。</summary>
    public static ServerKind Guess(string serverUrl) =>
        Distribution.IsGitHub(serverUrl) ? ServerKind.GitHub
        : Distribution.IsGitLab(serverUrl) ? ServerKind.GitLab
        : ServerKind.Forgejo;

    /// <summary>判定した種類か、判定できなかった（Unknown）ときの推測。</summary>
    public static ServerKind OrGuess(this ServerKind kind, string serverUrl) => kind == ServerKind.Unknown ? Guess(serverUrl) : kind;

    /// <summary>"http://host/sub/" → "http://host/"（GitLab の API はサーバーの根にある。リポジトリの URL のグループの部分は外す）。</summary>
    public static string Root(string serverUrl) =>
        Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Authority) + "/" : serverUrl.Trim().TrimEnd('/') + "/";

    public static async Task<ServerKind?> DetectAsync(string serverUrl, HttpMessageHandler? handler = null, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (Distribution.IsGitHub(serverUrl)) return ServerKind.GitHub;
        if (!Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out _)) return null;
        using var http = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
        http.Timeout = timeout ?? TimeSpan.FromSeconds(5);
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ProductInfo.Name, ProductInfo.Version));
        var reached = false;

        async Task<HttpResponseMessage?> Get(string url)
        {
            try
            {
                var r = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                reached = true;
                return r;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
            {
                Log.Info($"Server type check: {url} failed: {ex.Message}");
                return null;
            }
        }

        static bool IsJson(HttpResponseMessage r) => r.Content.Headers.ContentType?.MediaType?.Contains("json", StringComparison.OrdinalIgnoreCase) == true;

        using (var r = await Get(Root(serverUrl) + "api/v4/version").ConfigureAwait(false))
        {
            if (r is not null && (r.Headers.Contains("X-Gitlab-Meta") || IsJson(r) && (int)r.StatusCode is 200 or 401))
                return ServerKind.GitLab;
        }
        using (var r = await Get(serverUrl.Trim().TrimEnd('/') + "/api/v1/version").ConfigureAwait(false))
        {
            if (r is not null && IsJson(r) && (int)r.StatusCode is 200 or 401 or 403)
                return ServerKind.Forgejo;
        }
        return reached ? ServerKind.Unknown : null;
    }
}

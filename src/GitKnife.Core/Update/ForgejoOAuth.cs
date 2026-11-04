using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GitKnife.Core.Logging;

namespace GitKnife.Core.Update;

/// <summary>Forgejo が出したトークン。ExpiresAt を過ぎたら RefreshToken で取り直す。</summary>
public sealed record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt - TimeSpan.FromMinutes(1);
}

/// <summary>
/// Forgejo（Gitea 互換）の OAuth2。ブラウザでログインと許可をしてもらい、ループバックで認可コードを受け取る（RFC 8252、PKCE。2026-10-29 ユーザー判断）。
/// アプリは秘密の鍵を持たない（Forgejo のアプリ登録で Confidential client をオフ）。戻り先は "http://127.0.0.1/" で登録し、ポートは毎回空いているものを使う
/// （Forgejo は公開クライアントのループバックの戻り先のポートを無視して照合する）。
/// GitLab も同じ流れで、入口と出口のパスだけが違う（<see cref="ForGitLab"/>。GitLab は /oauth/authorize と /oauth/token）。
/// </summary>
public sealed class ForgejoOAuth(HttpClient http, string serverUrl, string clientId, string scope = "",
    string authorizePath = "login/oauth/authorize", string tokenPath = "login/oauth/access_token")
{
    /// <summary>GitLab の OAuth2（アプリ登録は Confidential をオフ、Redirect URI は http://127.0.0.1/、スコープは read_api）。</summary>
    public static ForgejoOAuth ForGitLab(HttpClient http, string serverUrl, string clientId, string scope) =>
        new(http, ServerProbe.Root(serverUrl), clientId, scope, "oauth/authorize", "oauth/token");

    private readonly string _base = serverUrl.Trim().TrimEnd('/') + "/";

    /// <summary>許可を待つ長さ（ブラウザでログインする時間を含む）。</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>ブラウザで許可画面を開き、戻ってきた認可コードをトークンに替える。</summary>
    /// <param name="openBrowser">許可画面の URL を既定のブラウザで開く。</param>
    public async Task<OAuthTokens> AuthorizeInBrowserAsync(Action<string> openBrowser, CancellationToken ct = default)
    {
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var redirect = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/";
            var query = new List<string>
            {
                "client_id=" + Uri.EscapeDataString(clientId),
                "redirect_uri=" + Uri.EscapeDataString(redirect),
                "response_type=code",
                "code_challenge=" + challenge,
                "code_challenge_method=S256",
                "state=" + state,
            };
            if (scope.Trim().Length > 0) query.Add("scope=" + Uri.EscapeDataString(scope.Trim()));
            openBrowser($"{_base}{authorizePath}?{string.Join('&', query)}");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            Log.Info($"Waiting for the browser to come back to {redirect} (up to {Timeout.TotalMinutes:0} minutes)");
            string code;
            try { code = await WaitForCodeAsync(listener, state, timeout.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // 許可画面で Forgejo がエラーを出すと（登録した戻り先と合わないなど）、ブラウザは戻ってこない
                throw new OAuthException($"Timed out waiting for sign-in in the browser ({Timeout.TotalMinutes:0} minutes). If the browser showed an error, check the OAuth2 application on the server (Confidential client must be off, redirect URI http://127.0.0.1/).");
            }
            return await RequestTokenAsync(new()
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirect,
                ["client_id"] = clientId,
                ["code_verifier"] = verifier,
            }, ct).ConfigureAwait(false);
        }
        finally { listener.Stop(); }
    }

    public Task<OAuthTokens> RefreshAsync(string refreshToken, CancellationToken ct = default) =>
        RequestTokenAsync(new()
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
        }, ct);

    /// <summary>ブラウザが戻り先に来るのを待つ。favicon など関係ない要求には 404 を返して待ち続ける。</summary>
    private static async Task<string> WaitForCodeAsync(TcpListener listener, string state, CancellationToken ct)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            await using var stream = client.GetStream();
            var target = await ReadRequestTargetAsync(stream, ct).ConfigureAwait(false);
            var q = ParseQuery(target);
            if (q.TryGetValue("error", out var error))
            {
                q.TryGetValue("error_description", out var description);
                await RespondAsync(stream, 400, "GitKnife could not sign in", $"{error}: {description}", ct).ConfigureAwait(false);
                throw new OAuthException($"Sign-in was refused: {error}{(string.IsNullOrEmpty(description) ? "" : " (" + description + ")")}");
            }
            if (!q.TryGetValue("code", out var code))
            {
                await RespondAsync(stream, 404, "Not found", "", ct).ConfigureAwait(false);
                continue;
            }
            if (!q.TryGetValue("state", out var s) || s != state)
            {
                await RespondAsync(stream, 400, "GitKnife could not sign in", "The response did not match the request.", ct).ConfigureAwait(false);
                throw new OAuthException("Sign-in response did not match the request (state).");
            }
            await RespondAsync(stream, 200, "GitKnife is signed in", "You can close this tab and return to GitKnife.", ct).ConfigureAwait(false);
            return code;
        }
    }

    /// <summary>"GET /?code=...&amp;state=... HTTP/1.1" の 2 つ目。ヘッダーの終わりまで読む。</summary>
    private static async Task<string> ReadRequestTargetAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
            if (Encoding.ASCII.GetString(buffer, 0, total).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
        }
        var firstLine = Encoding.ASCII.GetString(buffer, 0, total).Split("\r\n")[0];
        var parts = firstLine.Split(' ');
        return parts.Length >= 2 ? parts[1] : "";
    }

    private static Dictionary<string, string> ParseQuery(string target)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = target.IndexOf('?');
        if (i < 0) return result;
        foreach (var pair in target[(i + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((eq < 0 ? pair : pair[..eq]).Replace('+', ' '));
            var value = eq < 0 ? "" : Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' '));
            result[key] = value;
        }
        return result;
    }

    private static async Task RespondAsync(NetworkStream stream, int status, string title, string message, CancellationToken ct)
    {
        var html = $"<!doctype html><meta charset=\"utf-8\"><title>{WebUtility.HtmlEncode(title)}</title>"
            + "<body style=\"font-family:Segoe UI,sans-serif;margin:48px\">"
            + $"<h2>{WebUtility.HtmlEncode(title)}</h2><p>{WebUtility.HtmlEncode(message)}</p></body>";
        var body = Encoding.UTF8.GetBytes(html);
        var reason = status switch { 200 => "OK", 400 => "Bad Request", _ => "Not Found" };
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    private async Task<OAuthTokens> RequestTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var content = new FormUrlEncodedContent(form);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_base}{tokenPath}") { Content = content };
        request.Headers.Accept.ParseAdd("application/json");
        using var r = await http.SendAsync(request, ct).ConfigureAwait(false);
        var text = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!r.IsSuccessStatusCode)
            throw new OAuthException($"Token request failed: {(int)r.StatusCode} {r.ReasonPhrase} {(text.Length > 300 ? text[..300] : text)}".Trim());
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var access = root.TryGetProperty("access_token", out var a) ? a.GetString() : null;
        if (string.IsNullOrEmpty(access)) throw new OAuthException("Token response has no access_token.");
        var refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        var expiresIn = root.TryGetProperty("expires_in", out var e) && e.TryGetInt32(out var sec) ? sec : 3600;
        return new OAuthTokens(access, refresh, DateTimeOffset.UtcNow.AddSeconds(expiresIn));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

public sealed class OAuthException(string message) : Exception(message);

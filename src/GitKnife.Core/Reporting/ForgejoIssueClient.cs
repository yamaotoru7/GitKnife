using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace GitKnife.Core.Reporting;

/// <summary>作った Issue。</summary>
public sealed record CreatedIssue(long Number, string HtmlUrl);

/// <summary>
/// Forgejo（Gitea 互換）の Issue API。バグレポートの送り先。
/// 1. POST /api/v1/repos/{owner}/{repo}/issues で Issue を作る
/// 2. POST .../issues/{index}/assets で画像を添付する（multipart の "attachment"）
/// 3. PATCH .../issues/{index} で本文に添付の URL を埋める
/// 認証は個人のアクセストークン（"Authorization: token ..."。権限 write:issue）。
/// </summary>
public sealed class ForgejoIssueClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _repoApi;

    /// <param name="serverUrl">Web の URL（https://host/ など。サブパスも可）。</param>
    /// <param name="repository">"owner/repo"。</param>
    public ForgejoIssueClient(string serverUrl, string repository, string token, HttpMessageHandler? handler = null)
    {
        var parts = repository.Trim().Trim('/').Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0)) throw new ArgumentException($"Repository must be 'owner/repo': '{repository}'");
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.BaseAddress = new Uri(serverUrl.Trim().TrimEnd('/') + "/");
        _http.Timeout = TimeSpan.FromSeconds(60);
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token.Trim());
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _repoApi = $"api/v1/repos/{Uri.EscapeDataString(parts[0])}/{Uri.EscapeDataString(parts[1])}";
    }

    /// <summary>assignees は担当にするユーザー名（空なら担当なし）。</summary>
    public async Task<CreatedIssue> CreateIssueAsync(string title, string body, IReadOnlyList<string>? assignees = null, CancellationToken ct = default)
    {
        object payload = assignees is { Count: > 0 } ? new { title, body, assignees } : new { title, body };
        using var r = await _http.PostAsJsonAsync($"{_repoApi}/issues", payload, ct);
        await EnsureSuccessAsync(r, "Creating issue", ct);
        var issue = await r.Content.ReadFromJsonAsync<IssueResponse>(ct) ?? throw new InvalidOperationException("Empty response");
        return new CreatedIssue(issue.Number, issue.HtmlUrl ?? "");
    }

    /// <summary>添付して、ブラウザで開ける URL を返す。</summary>
    public async Task<string> UploadAttachmentAsync(long number, string fileName, byte[] content, string mediaType, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(file, "attachment", fileName);
        using var r = await _http.PostAsync($"{_repoApi}/issues/{number}/assets?name={Uri.EscapeDataString(fileName)}", form, ct);
        await EnsureSuccessAsync(r, $"Uploading {fileName}", ct);
        var a = await r.Content.ReadFromJsonAsync<AttachmentResponse>(ct) ?? throw new InvalidOperationException("Empty response");
        return a.BrowserDownloadUrl ?? "";
    }

    public async Task UpdateBodyAsync(long number, string body, CancellationToken ct = default)
    {
        using var r = await _http.PatchAsJsonAsync($"{_repoApi}/issues/{number}", new { body }, ct);
        await EnsureSuccessAsync(r, "Updating issue", ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage r, string what, CancellationToken ct)
    {
        if (r.IsSuccessStatusCode) return;
        var text = await r.Content.ReadAsStringAsync(ct);
        if (text.Length > 500) text = text[..500];
        throw new HttpRequestException($"{what} failed: {(int)r.StatusCode} {r.ReasonPhrase}\n{text}".Trim(), null, r.StatusCode);
    }

    public void Dispose() => _http.Dispose();

    private sealed class IssueResponse
    {
        [JsonPropertyName("number")] public long Number { get; set; }
        [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    }

    private sealed class AttachmentResponse
    {
        [JsonPropertyName("browser_download_url")] public string? BrowserDownloadUrl { get; set; }
    }
}

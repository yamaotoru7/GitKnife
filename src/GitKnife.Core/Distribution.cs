using System.Reflection;

namespace GitKnife.Core;

/// <summary>
/// 配布先ごとの値（ビルド時に Directory.Build.props が AssemblyMetadata に入れる）。
/// 更新の確認とバグ報告の送り先の既定値になる。
/// </summary>
public static class Distribution
{
    /// <summary>Web の URL（"https://github.com/" など）。</summary>
    public static string ServerUrl { get; } = Get("GkServer") is { Length: > 0 } s ? s : "https://github.com/";

    /// <summary>"owner/repo"。</summary>
    public static string Repository { get; } = Get("GkRepository") ?? "";

    /// <summary>更新の API にサインインが要るサーバーの OAuth2 アプリの Client ID（空ならサインインしない）。</summary>
    public static string OAuthClientId { get; } = Get("GkOAuthClientId") ?? "";

    /// <summary>バグ報告の Issue の担当（空なら "Assign to …" を出さない）。</summary>
    public static string BugReportAssignee { get; } = Get("GkBugReportAssignee") ?? "";

    /// <summary>github.com（Issue と Releases の扱いが Forgejo と違う）。</summary>
    public static bool IsGitHub(string serverUrl) =>
        Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var u) && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    private static string? Get(string key) =>
        typeof(Distribution).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value?.Trim();
}

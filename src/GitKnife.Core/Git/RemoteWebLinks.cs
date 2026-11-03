using System.Text.RegularExpressions;

namespace GitKnife.Core.Git;

/// <summary>
/// Repository Manager の Remotes の "Issues" / "Pull Requests" のリンク先。
/// よく使うホスティング（GitHub、GitLab、Bitbucket、Azure DevOps）の決まった形で作る。作れないときは null。
/// </summary>
public static partial class RemoteWebLinks
{
    public static (string? Issues, string? PullRequests) For(string remoteUrl)
    {
        if (WebBase(remoteUrl) is not { } web) return (null, null);
        var host = new Uri(web).Host.ToLowerInvariant();
        if (host == "github.com" || host.EndsWith(".github.com", StringComparison.Ordinal)) return ($"{web}/issues", $"{web}/pulls");
        if (host == "bitbucket.org") return ($"{web}/issues", $"{web}/pull-requests");
        if (host == "dev.azure.com" || host.EndsWith(".visualstudio.com", StringComparison.Ordinal))
        {
            // https://dev.azure.com/org/project/_git/repo → 作業項目はプロジェクト、PR はリポジトリ
            var i = web.IndexOf("/_git/", StringComparison.Ordinal);
            return i < 0 ? (null, null) : ($"{web[..i]}/_workitems", $"{web}/pullrequests");
        }
        // GitLab（gitlab.com と自前のサーバー）は /-/ の下
        return ($"{web}/-/issues", $"{web}/-/merge_requests");
    }

    /// <summary>
    /// Add Remote / Clone の Repository Url の右の ▾（NetworkProtocolDropDownButton）の 2 つ：HTTPS の形と SSH の形
    /// （"https://github.com/owner/repo.git" と "git@github.com:owner/repo.git"）。作れなければ null。
    /// </summary>
    public static (string Https, string Ssh)? ProtocolForms(string remoteUrl)
    {
        if (WebBase(remoteUrl) is not { } web || web.Contains("dev.azure.com", StringComparison.OrdinalIgnoreCase)) return null;
        var u = new Uri(web);
        var path = u.AbsolutePath.TrimStart('/');
        return ($"https://{u.Host}/{path}.git", $"git@{u.Host}:{path}.git");
    }

    /// <summary>ボタンの文字（NetworkProtocolTextBlock）。SSH の形なら "SSH"、それ以外（ローカルのパスも）は "HTTPS"。</summary>
    public static string ProtocolName(string remoteUrl)
    {
        var url = remoteUrl.Trim();
        if (url.Contains("://", StringComparison.Ordinal)) return url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase) ? "SSH" : "HTTPS";
        return ScpLike().IsMatch(url) ? "SSH" : "HTTPS";
    }

    /// <summary>リモートの URL を https の Web の URL にする（.git を外す）。ローカルのパスや file:// は null。</summary>
    public static string? WebBase(string remoteUrl)
    {
        var url = remoteUrl.Trim();
        string? web = null;
        if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
            web = $"https://{u.Host}{(u.IsDefaultPort ? "" : ":" + u.Port)}{u.AbsolutePath}";
        }
        else if (url.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return null;
            web = $"https://{u.Host}{u.AbsolutePath}";
        }
        else if (ScpLike().Match(url) is { Success: true } m)
        {
            // git@github.com:owner/repo.git
            web = $"https://{m.Groups["host"].Value}/{m.Groups["path"].Value.TrimStart('/')}";
        }
        if (web is null) return null;
        web = web.TrimEnd('/');
        if (web.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) web = web[..^4];
        // Azure DevOps の ssh（ssh.dev.azure.com:v3/org/project/repo）は Web の形が違う
        var azure = AzureSsh().Match(web);
        if (azure.Success) web = $"https://dev.azure.com/{azure.Groups["org"].Value}/{azure.Groups["project"].Value}/_git/{azure.Groups["repo"].Value}";
        return web;
    }

    /// <summary>
    /// クリップボードの文字がリモートの URL に見えるか（Add Remote で Repository Url に先に入れる。#14）。
    /// 1 行で空白を含まず、https / http / ssh / git の URL でパスがあるもの、または user@host:path の形。
    /// </summary>
    public static bool LooksLikeRemoteUrl(string? text)
    {
        var url = text?.Trim() ?? "";
        if (url.Length == 0 || url.Any(char.IsWhiteSpace)) return false;
        if (url.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Host.Length == 0) return false;
            if (u.Scheme is not ("https" or "http" or "ssh" or "git")) return false;
            return u.AbsolutePath.Trim('/').Length > 0;
        }
        return url.Contains('@') && ScpLike().Match(url) is { Success: true } m && m.Groups["path"].Value.Trim('/').Length > 0;
    }

    // user@host:path（Windows のドライブ C:\... と区別するため、host は 2 文字以上）
    [GeneratedRegex(@"^(?:[^@/\s]+@)?(?<host>[A-Za-z0-9.-]{2,}):(?<path>[^\\].*)$")]
    private static partial Regex ScpLike();

    [GeneratedRegex(@"^https://ssh\.dev\.azure\.com/v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+)$")]
    private static partial Regex AzureSsh();
}

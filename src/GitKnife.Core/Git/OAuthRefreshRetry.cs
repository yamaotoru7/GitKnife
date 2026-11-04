using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace GitKnife.Core.Git;

/// <summary>
/// Git Credential Manager の OAuth（generic など）で、期限の切れたアクセストークンのせいで http(s) のリモートの認証が失敗したかを判定する。
/// GCM は保存してあるアクセストークンを期限を見ずに渡し、サーバーに拒否されると git が credential reject でそれを消す。
/// 次の git では GCM が refresh_token から新しいアクセストークンを取るので通る。そのため GitKnife は、
/// 「Authentication failed for 'http(s)://…'」で失敗し、資格情報マネージャーにそのホストの refresh_token が残っているときだけ、同じ git を 1 回やり直す。
/// refresh_token が無い（ユーザー名とパスワードを保存している、など）ときはやり直さない（同じものを聞き直したり、ロックの回数を増やしたりしない）。
/// </summary>
public static partial class OAuthRefreshRetry
{
    [GeneratedRegex(@"Authentication failed for '(?<url>https?://[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex AuthFailedForHttp();

    /// <summary>stderr が http(s) のリモートでの認証失敗なら、そのリモートの "scheme://authority"（ユーザー名などは除く）。そうでなければ null。</summary>
    public static string? FindFailedOrigin(string stderr)
    {
        var m = AuthFailedForHttp().Match(stderr);
        if (!m.Success || !Uri.TryCreate(m.Groups["url"].Value.Trim(), UriKind.Absolute, out var uri)) return null;
        return $"{uri.Scheme}://{uri.Authority}";
    }

    /// <summary>GCM が refresh_token を置く資格情報の名前の先頭（"http://host:port" → "git:http://refresh_token.host:port"）。</summary>
    public static string RefreshTokenTarget(string origin)
    {
        var uri = new Uri(origin);
        return $"git:{uri.Scheme}://refresh_token.{uri.Authority}";
    }

    /// <summary>この失敗を 1 回やり直してよいか（Windows で、そのホストの refresh_token が資格情報マネージャーにあるとき）。</summary>
    public static bool ShouldRetry(string stderr, out string? origin)
    {
        origin = FindFailedOrigin(stderr);
        if (origin is null || !OperatingSystem.IsWindows()) return false;
        try { return HasCredential(RefreshTokenTarget(origin)); }
        catch { return false; }
    }

    /// <summary>名前が target そのもの、または target に続けてパスが付いたもの（credential.useHttpPath のとき）があるか。中身は読まない。</summary>
    private static bool HasCredential(string target)
    {
        foreach (var filter in new[] { target, target + "/*" })
        {
            if (!CredEnumerate(filter, 0, out var count, out var list)) continue;
            try { if (count > 0) return true; }
            finally { CredFree(list); }
        }
        return false;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredEnumerate(string? filter, int flags, out int count, out IntPtr credentials);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
}

namespace GitKnife.Core;

/// <summary>
/// 配布先（更新の確認とバグ報告の送り先の既定値）。インストーラはどこに置いても同じもので、配布先はダウンロードした場所で決まる：
/// インストーラが自分の Zone.Identifier（ブラウザが書くダウンロード元。HostUrl と ReferrerUrl）を
/// GitKnife.exe の隣の install-source.txt に写し、ここで "&lt;サーバー&gt;/&lt;owner&gt;/&lt;repo&gt;/releases/…" の形を読む。
/// 読めなければ（ファイルが無い、URL がリリースの形でない）GitHub の既定。
/// </summary>
public static class Distribution
{
    public const string DefaultServerUrl = "https://github.com/";
    public const string DefaultRepository = "yamaotoru7/GitKnife";
    public const string InstallSourceFileName = "install-source.txt";

    private static readonly (string ServerUrl, string Repository)? Source = ReadInstallSource(Path.Combine(AppContext.BaseDirectory, InstallSourceFileName));

    /// <summary>インストーラがダウンロード元を書き残していて、それが読めた。</summary>
    public static bool FromInstallSource => Source is not null;

    /// <summary>Web の URL（"https://github.com/" など。末尾は /）。</summary>
    public static string ServerUrl => Source?.ServerUrl ?? DefaultServerUrl;

    /// <summary>"owner/repo"。</summary>
    public static string Repository => Source?.Repository ?? DefaultRepository;

    /// <summary>github.com（Releases の API が Forgejo と違う）。</summary>
    public static bool IsGitHub(string serverUrl) =>
        Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var u) && u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// GitLab（New issue の URL とクエリの名前が GitHub・Forgejo と違う）。自前のサーバーはホスト名で見分けるしかないので、
    /// ホスト名に "gitlab" が入っていれば GitLab とみなす（gitlab.com、gitlab.example.co.jp など）。
    /// </summary>
    public static bool IsGitLab(string serverUrl) =>
        Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var u) && u.Host.Contains("gitlab", StringComparison.OrdinalIgnoreCase);

    private static (string, string)? ReadInstallSource(string path)
    {
        try { return File.Exists(path) ? ParseZoneIdentifier(File.ReadAllText(path)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>
    /// Zone.Identifier の中身（"HostUrl=…"、"ReferrerUrl=…" の行）から配布先を読む。
    /// GitHub は HostUrl がファイルの置き場（release-assets.githubusercontent.com）で、ReferrerUrl がリリースのページ。
    /// Forgejo は HostUrl が /owner/repo/releases/download/… か /attachments/…、ReferrerUrl がリリースのページ。どちらかがリリースの形なら使う。
    /// </summary>
    public static (string ServerUrl, string Repository)? ParseZoneIdentifier(string text)
    {
        var urls = text.Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase) || l.StartsWith("ReferrerUrl=", StringComparison.OrdinalIgnoreCase))
            .Select(l => l[(l.IndexOf('=') + 1)..].Trim());
        foreach (var url in urls)
            if (ParseReleaseUrl(url) is { } r) return r;
        return null;
    }

    /// <summary>
    /// ユーザーが入れたリポジトリの URL（"https://github.com/owner/repo"。末尾の /、.git、/releases…、/issues… は許す）
    /// → ("https://github.com/", "owner/repo")。読めなければ null。
    /// </summary>
    public static (string ServerUrl, string Repository)? ParseRepositoryUrl(string url)
    {
        url = url.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http")) return null;
        var segs = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (segs.Count < 2) return null;
        var cut = segs.FindIndex(2, s => s is "releases" or "issues" or "-" or "src" or "tree");
        if (cut >= 2) segs = segs[..cut];
        if (segs.Count < 2) return null;
        var repo = segs[^1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segs[^1][..^4] : segs[^1];
        if (repo.Length == 0) return null;
        var prefix = string.Concat(segs[..^2].Select(s => s + "/"));
        return ($"{u.GetLeftPart(UriPartial.Authority)}/{prefix}", $"{Uri.UnescapeDataString(segs[^2])}/{Uri.UnescapeDataString(repo)}");
    }

    /// <summary>("https://github.com/", "owner/repo") → "https://github.com/owner/repo"（空なら空）。</summary>
    public static string RepositoryUrl(string serverUrl, string repository) =>
        serverUrl.Trim().Length == 0 || repository.Trim().Length == 0 ? "" : $"{serverUrl.Trim().TrimEnd('/')}/{repository.Trim().Trim('/')}";

    /// <summary>
    /// "https://host[/sub]/owner/repo/releases[/…]" → ("https://host[/sub]/", "owner/repo")。
    /// GitLab のリリースのページ（"…/owner/repo/-/releases/…"）と、添付のパッケージの URL
    /// （"https://host/api/v4/projects/group%2Fowner%2Frepo/packages/…"。グループは ServerUrl に入れる）も読む。
    /// </summary>
    public static (string ServerUrl, string Repository)? ParseReleaseUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http")) return null;
        // GitLab の API の URL（プロジェクトのパスが 1 つの要素にエスケープされている）
        var raw = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (raw.Length >= 4 && raw[0] == "api" && raw[1] == "v4" && raw[2] == "projects")
        {
            var path = Uri.UnescapeDataString(raw[3]).Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (path.Length < 2) return null;
            return ($"{u.GetLeftPart(UriPartial.Authority)}/{string.Concat(path[..^2].Select(s => s + "/"))}", $"{path[^2]}/{path[^1]}");
        }
        var segs = u.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var i = Array.FindLastIndex(segs, s => s.Equals("releases", StringComparison.OrdinalIgnoreCase));
        if (i >= 1 && segs[i - 1] == "-") segs = [.. segs[..(i - 1)], .. segs[i..]];
        i = Array.FindLastIndex(segs, s => s.Equals("releases", StringComparison.OrdinalIgnoreCase));
        if (i < 2) return null;
        var prefix = string.Concat(segs[..(i - 2)].Select(s => s + "/"));
        var server = $"{u.GetLeftPart(UriPartial.Authority)}/{prefix}";
        return (server, $"{Uri.UnescapeDataString(segs[i - 2])}/{Uri.UnescapeDataString(segs[i - 1])}");
    }
}

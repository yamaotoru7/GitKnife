using System.Text.RegularExpressions;
using GitKnife.Core.Git;

namespace GitKnife.Core.Reporting;

/// <summary>
/// バグレポートに載せる文字（環境の表、ログ）から、人や PC が分かる部分を伏せる（Issue #21）。
/// 伏せるもの：開いているリポジトリのパス（Windows の形、/ 区切り、/e/… の形、WSL と SSH の接続先のパス）、
/// ユーザーのフォルダ（%LOCALAPPDATA% などに置き換え、\Users\&lt;名前&gt; と /home/&lt;名前&gt; の名前）、ユーザー名、PC の名前、
/// メールアドレス、URL の中のユーザー名とパスワードとパス（ホストまで残す）。
/// </summary>
public sealed partial class BugReportPrivacy
{
    public const string RepositoryToken = "<repository>";
    public const string UserToken = "<user>";
    public const string MachineToken = "<machine>";
    public const string EmailToken = "<email>";
    public const string PathToken = "<path>";

    private readonly List<(string From, string To)> _replacements = [];
    private readonly Regex? _names;

    /// <param name="repositories">開いているリポジトリのパス。先頭（今のタブ）が &lt;repository&gt;、ほかは &lt;repository 2&gt;… になる。</param>
    /// <param name="folders">置き換えるフォルダと名前（C:\Users\a\AppData\Local → %LOCALAPPDATA% など）。長い方を先に当てる。</param>
    /// <param name="names">そのまま現れたら伏せる名前（ユーザー名 → &lt;user&gt;、PC の名前 → &lt;machine&gt;）。</param>
    public BugReportPrivacy(IEnumerable<string> repositories, IEnumerable<(string Folder, string Name)> folders, IEnumerable<(string Name, string Token)> names)
    {
        var n = 0;
        foreach (var repo in repositories.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            n++;
            var token = n == 1 ? RepositoryToken : $"<repository {n}>";
            foreach (var v in PathForms(repo)) _replacements.Add((v, token));
        }
        foreach (var (folder, name) in folders.Where(f => !string.IsNullOrWhiteSpace(f.Folder)))
            foreach (var v in PathForms(folder)) _replacements.Add((v, name));
        // 長いものから（リポジトリがユーザーのフォルダの下にあってもリポジトリとして伏せる）
        _replacements.Sort((a, b) => b.From.Length.CompareTo(a.From.Length));

        var list = names.Where(x => x.Name.Trim().Length >= 2).ToList();
        if (list.Count > 0)
        {
            _nameTokens = list.ToDictionary(x => x.Name, x => x.Token, StringComparer.OrdinalIgnoreCase);
            _names = new Regex(@"(?<![\w.-])(?:" + string.Join("|", list.Select(x => Regex.Escape(x.Name)).OrderByDescending(x => x.Length)) + @")(?![\w-])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
    }

    private readonly Dictionary<string, string> _nameTokens = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>今の Windows のユーザーで作る（ユーザーのフォルダ、ユーザー名、PC の名前）。</summary>
    /// <param name="sourceFolders">Repository Manager の Source Code Folder（&lt;source folder&gt; にする）。</param>
    public static BugReportPrivacy ForCurrentUser(IEnumerable<string> repositories, IEnumerable<string>? sourceFolders = null)
    {
        (string, string) F(Environment.SpecialFolder f, string name) => (Environment.GetFolderPath(f), name);
        return new BugReportPrivacy(repositories,
            [
                .. (sourceFolders ?? []).Select(d => (d, "<source folder>")),
                F(Environment.SpecialFolder.LocalApplicationData, "%LOCALAPPDATA%"),
                F(Environment.SpecialFolder.ApplicationData, "%APPDATA%"),
                F(Environment.SpecialFolder.UserProfile, "%USERPROFILE%"),
                (Path.GetTempPath().TrimEnd('\\', '/'), "%TEMP%"),
            ],
            [(Environment.UserName, UserToken), (Environment.MachineName, MachineToken)]);
    }

    /// <summary>環境の表に出すリポジトリ（パスの代わりに接続先の種類だけ：Local、WSL、SSH）。</summary>
    public static string RepositoryKind(string path) => GitHost.FromPath(path) switch
    {
        WslGitHost => "WSL",
        SshGitHost => "SSH",
        _ => "Local",
    };

    /// <summary>
    /// 環境の表に出す git（その接続先で使う git）。Local は git.exe のパス、WSL と SSH はディストリビューションとホストの名前を出さずに種類だけ
    /// （SSH はホストの設定の git の場所も）。
    /// </summary>
    public static string GitDescription(GitHost host, string localGitPath) => host switch
    {
        WslGitHost => "git in WSL",
        SshGitHost s => $"git on SSH host ({s.GitPath})",
        _ => localGitPath,
    };

    /// <summary>文字の中の伏せる部分を置き換える。</summary>
    public string Mask(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var s = text;
        // 資格情報の診断（user=名前）に出たアカウント名は、ほかの場所（gh:host:名前 など）でも伏せる
        var accounts = AccountName().Matches(s).Select(m => m.Groups["name"].Value).Where(n => n.Length >= 2 && !n.StartsWith('<'))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length).ToList();
        foreach (var (from, to) in _replacements) s = ReplaceIgnoreCase(s, from, to);
        s = AccountName().Replace(s, m => m.Groups["key"].Value + UserToken);
        foreach (var a in accounts) s = Regex.Replace(s, @"(?<![\w.-])" + Regex.Escape(a) + @"(?![\w-])", UserToken, RegexOptions.IgnoreCase);
        s = UrlCredentials().Replace(s, m => m.Groups["scheme"].Value + UserToken + "@");
        // URL のパスにはアカウントや組織の名前が入る（https://host/owner/repo.git、git@host:owner/repo.git）。ホストまで残す
        s = UrlPath().Replace(s, m => m.Groups["keep"].Value + "/" + PathToken);
        s = ScpPath().Replace(s, m => m.Groups["keep"].Value + PathToken);
        s = Email().Replace(s, m => m.Groups["local"].Value.Equals("git", StringComparison.OrdinalIgnoreCase) ? m.Value : EmailToken);
        s = ProfileName().Replace(s, m => m.Groups["head"].Value + UserToken);
        if (_names is not null) s = _names.Replace(s, m => _nameTokens.TryGetValue(m.Value, out var t) ? t : UserToken);
        return s;
    }

    /// <summary>パスの書き方の違い（\ と /、Git Bash の /e/…、WSL と SSH の接続先のパス）。</summary>
    private static IEnumerable<string> PathForms(string path)
    {
        var p = path.Trim().TrimEnd('\\', '/');
        if (p.Length < 3) yield break;
        yield return p;
        if (p.Contains('\\')) yield return p.Replace('\\', '/');
        if (p.Contains('/')) yield return p.Replace('/', '\\');
        if (p.Length > 2 && char.IsAsciiLetter(p[0]) && p[1] == ':')
            yield return "/" + char.ToLowerInvariant(p[0]) + p[2..].Replace('\\', '/');
        if (WslPath.TryParseUnc(p, out _, out var linux) && linux.Length > 1) yield return linux.TrimEnd('/');
        if (SshPath.TryParse(p, out _, out var remote) && remote.Length > 1) yield return remote.TrimEnd('/');
    }

    private static string ReplaceIgnoreCase(string s, string from, string to)
    {
        var i = s.IndexOf(from, StringComparison.OrdinalIgnoreCase);
        if (i < 0) return s;
        var sb = new System.Text.StringBuilder();
        var start = 0;
        for (; i >= 0; i = s.IndexOf(from, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            // 名前の途中で切れるもの（C:\Users\a に対する C:\Users\a~1、D:\Work に対する D:\Work2）は置き換えない
            var end = i + from.Length;
            if (i < start || (end < s.Length && (char.IsLetterOrDigit(s[end]) || s[end] is '_' or '-' or '~' or '.' && end + 1 < s.Length && char.IsLetterOrDigit(s[end + 1])))) continue;
            sb.Append(s, start, i - start).Append(to);
            start = end;
        }
        return sb.Append(s, start, s.Length - start).ToString();
    }

    /// <summary>https://name:token@host の name:token。</summary>
    [GeneratedRegex(@"(?<scheme>\b[a-z][a-z0-9+.-]*://)[^/@\s'""<>]+@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlCredentials();

    [GeneratedRegex(@"(?<keep>\b[a-z][a-z0-9+.-]*://(?:<user>@)?[^/\s'""<>]+)/(?!<path>)[^\s'""<>)\],;]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPath();

    /// <summary>user=名前、username=名前（資格情報の診断）。</summary>
    [GeneratedRegex(@"(?<key>\buser(?:name)?=)(?<name>[^\s'""<>,;]+)", RegexOptions.IgnoreCase)]
    private static partial Regex AccountName();

    [GeneratedRegex(@"(?<keep>(?<![\w.+-])git@[\w.-]+:)(?!<path>)[^\s'""<>/][^\s'""<>]*", RegexOptions.IgnoreCase)]
    private static partial Regex ScpPath();

    [GeneratedRegex(@"(?<![\w.+-])(?<local>[\w.+-]+)@[\w-]+(?:\.[\w-]+)+", RegexOptions.CultureInvariant)]
    private static partial Regex Email();

    /// <summary>\Users\名前、/Users/名前、/home/名前 の名前（8.3 形式の短い名前も）。</summary>
    [GeneratedRegex(@"(?<head>(?:[\\/]Users|/home)[\\/])(?!<user>)[^\\/\s'""<>:|*?]+", RegexOptions.IgnoreCase)]
    private static partial Regex ProfileName();
}

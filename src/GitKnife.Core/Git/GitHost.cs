using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GitKnife.Core.Git;

/// <summary>
/// 接続先。git をどこで動かすかと、Windows のパスと接続先のパスの対応を持つ。
/// Local は Windows の git.exe。WSL のリポジトリを Windows の git で読むと実行ビットが読めず、すべて mode の変更に見える（2026-10-30 llama.cpp）ので、
/// WSL のパスはその WSL の中の git で動かす。
/// </summary>
public abstract record GitHost
{
    public static GitHost Local { get; } = new LocalGitHost();

    /// <summary>登録した SSH のホストを名前で引く（アプリが設定から入れる）。無い名前は、名前をそのまま ssh の接続先にする（~/.ssh/config の Host）。</summary>
    public static Func<string, SshGitHost?>? SshHostResolver { get; set; }

    /// <summary>
    /// パスから接続先を決める。\\wsl.localhost\&lt;distro&gt;\…、\\wsl$\…、WSL を割り当てたドライブは WSL、
    /// \\gitknife-ssh\&lt;ホスト&gt;\… は SSH、ほかは Local。
    /// </summary>
    public static GitHost FromPath(string path)
    {
        if (SshPath.TryParse(path, out var name, out _)) return SshHostResolver?.Invoke(name) ?? new SshGitHost(name, name);
        return WslPath.TryParseUnc(WslPath.ResolveMappedDrive(path), out var distro, out _) ? new WslGitHost(distro) : Local;
    }

    /// <summary>ログと表示用の名前。</summary>
    public abstract string DisplayName { get; }

    /// <summary>Windows のパス → 接続先のパス（git の作業フォルダと引数）。</summary>
    public virtual string ToHostPath(string localPath) => localPath;

    /// <summary>接続先のパス（git の出力）→ Windows のパス。</summary>
    public virtual string ToLocalPath(string hostPath) => hostPath;

    /// <summary>
    /// 利用者が接続先を選んで打ったパス → この接続先を指す Windows の形。<see cref="ToLocalPath"/> と違い、WSL の /mnt/c/… も WSL のまま
    /// （ToLocalPath は C:\… にするので Local の git になる）。Open Repository のフォルダの一覧と確認の rev-parse に使う。
    /// </summary>
    public virtual string BrowsePath(string hostPath) => hostPath;

    /// <summary>接続先のホームのフォルダ（Local は null）。接続できなければ IOException。同期なのでバックグラウンドから呼ぶ。</summary>
    public virtual string? HomeDirectory() => null;

    /// <summary>引数の中の Windows の絶対パスを接続先の形にする。リポジトリの中の相対パスはそのまま。</summary>
    public virtual string ConvertArgument(string argument) => argument;

    /// <summary>
    /// git を起動する ProcessStartInfo（ファイル名、引数、作業フォルダ、環境変数）。リダイレクトなどは GitProcess が付ける。
    /// env は GitKnife が git に渡す環境変数（GIT_TERMINAL_PROMPT など）。
    /// </summary>
    public abstract System.Diagnostics.ProcessStartInfo CreateStartInfo(GitExecutable git, GitRequest request, IReadOnlyDictionary<string, string?> env);
}

public sealed record LocalGitHost : GitHost
{
    public override string DisplayName => "Local";

    public override System.Diagnostics.ProcessStartInfo CreateStartInfo(GitExecutable git, GitRequest request, IReadOnlyDictionary<string, string?> env)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(git.Path) { WorkingDirectory = request.WorkingDirectory };
        foreach (var a in request.Arguments) psi.ArgumentList.Add(a);
        foreach (var (k, v) in env) psi.Environment[k] = v;
        return psi;
    }
}

/// <summary>WSL のディストリビューション。git は <c>wsl.exe -d &lt;distro&gt; --cd &lt;dir&gt; --exec git …</c> で起動する。</summary>
public sealed partial record WslGitHost(string Distribution) : GitHost
{
    public override string DisplayName => "WSL: " + Distribution;

    public static string WslExe => Path.Combine(Environment.SystemDirectory, "wsl.exe");

    public override System.Diagnostics.ProcessStartInfo CreateStartInfo(GitExecutable git, GitRequest request, IReadOnlyDictionary<string, string?> env)
    {
        // 作業フォルダは wsl.exe に --cd で渡す（UNC の作業フォルダでは起動しない）
        var psi = new System.Diagnostics.ProcessStartInfo(WslExe) { WorkingDirectory = Environment.SystemDirectory };
        foreach (var a in new[] { "-d", Distribution, "--cd", ToHostPath(request.WorkingDirectory), "--exec", "git" }) psi.ArgumentList.Add(a);
        foreach (var a in request.Arguments) psi.ArgumentList.Add(ConvertArgument(a));
        foreach (var (k, v) in env) psi.Environment[k] = v;
        // WSL の中へは WSLENV に名前を並べた変数だけが渡る
        var existing = psi.Environment.TryGetValue("WSLENV", out var w) && !string.IsNullOrEmpty(w) ? w + ":" : "";
        psi.Environment["WSLENV"] = existing + string.Join(':', env.Keys);
        return psi;
    }

    public override string ToHostPath(string localPath)
    {
        var p = WslPath.ResolveMappedDrive(localPath);
        if (WslPath.TryParseUnc(p, out var distro, out var linux))
            return string.Equals(distro, Distribution, StringComparison.OrdinalIgnoreCase) ? linux : localPath;
        return WslPath.FromDrivePath(p) ?? localPath;
    }

    public override string ToLocalPath(string hostPath) =>
        !hostPath.StartsWith('/') ? hostPath : WslPath.ToDrivePath(hostPath) ?? WslPath.ToUnc(Distribution, hostPath);

    public override string BrowsePath(string hostPath) => WslPath.ToUnc(Distribution, hostPath);

    public override string? HomeDirectory()
    {
        var psi = new System.Diagnostics.ProcessStartInfo(WslExe)
        {
            WorkingDirectory = Environment.SystemDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = new UTF8Encoding(false), StandardErrorEncoding = Encoding.Unicode,
        };
        foreach (var a in new[] { "-d", Distribution, "--cd", "~", "--exec", "pwd" }) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var err = p.StandardError.ReadToEndAsync();
        var home = p.StandardOutput.ReadToEnd().Trim();
        p.WaitForExit();
        return p.ExitCode == 0 ? home : throw new IOException(err.Result.Trim());
    }

    /// <summary>この PC の WSL のディストリビューション（`wsl -l -q`。起動はしない）。git の無い docker-desktop は除く。WSL が無ければ空。</summary>
    public static IReadOnlyList<string> Distributions()
    {
        if (!File.Exists(WslExe)) return [];
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(WslExe, "-l -q") { RedirectStandardOutput = true, StandardOutputEncoding = Encoding.Unicode, CreateNoWindow = true, UseShellExecute = false };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0) return [];
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(n => n.Length > 0 && !n.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        catch (System.ComponentModel.Win32Exception) { return []; }
    }

    [GeneratedRegex(@"^(?<pre>--[A-Za-z0-9-]+=)?(?<path>[A-Za-z]:[\\/].*|(?:\\\\|//)wsl(?:\.localhost|\$)[\\/].*)$", RegexOptions.Singleline)]
    private static partial Regex WindowsPathArgument();

    /// <summary>引数の中の Windows の絶対パス（そのまま、または --xxx=パス）を接続先の形にする。リポジトリの中の相対パスはそのまま。</summary>
    public override string ConvertArgument(string argument)
    {
        var m = WindowsPathArgument().Match(argument);
        return m.Success ? m.Groups["pre"].Value + ToHostPath(m.Groups["path"].Value) : argument;
    }
}

/// <summary>
/// SSH の接続先（T1301）。git は <c>ssh -T &lt;接続先&gt; "cd '&lt;dir&gt;' &amp;&amp; env K=V … git '&lt;引数&gt;' …"</c> で起動する。
/// Windows 側ではリポジトリを \\gitknife-ssh\&lt;Name&gt;\home\… の形で表す（タブやワークスペースの保存、Path の操作がそのまま使える）。
/// このパスはファイルとしては開けないので、リポジトリの中のファイルは接続先を通して読む（T1303）。
/// Destination は ssh に渡す接続先（"user@host"、~/.ssh/config の Host 名など）、GitPath は接続先の git。
/// </summary>
public sealed partial record SshGitHost(string Name, string Destination, string GitPath = "git") : GitHost
{
    public override string DisplayName => "SSH: " + Name;

    /// <summary>Windows の OpenSSH（~/.ssh/config と ssh-agent をそのまま使う）。無ければ PATH の ssh.exe。</summary>
    public static string SshExe
    {
        get
        {
            var p = Path.Combine(Environment.SystemDirectory, "OpenSSH", "ssh.exe");
            return File.Exists(p) ? p : "ssh.exe";
        }
    }

    public override string ToHostPath(string localPath) =>
        SshPath.TryParse(localPath, out var name, out var remote) && string.Equals(name, Name, StringComparison.OrdinalIgnoreCase) ? remote : localPath;

    public override string ToLocalPath(string hostPath) => hostPath.StartsWith('/') ? SshPath.ToUnc(Name, hostPath) : hostPath;

    public override string BrowsePath(string hostPath) => SshPath.ToUnc(Name, hostPath);

    public override string? HomeDirectory()
    {
        var r = Shell("pwd");
        return r.ExitCode == 0 ? r.Text.Trim() : throw new IOException(r.ErrorText.Trim());
    }

    [GeneratedRegex(@"^(?<pre>--[A-Za-z0-9-]+=)?(?<path>(?:\\\\|//)gitknife-ssh[\\/].*)$", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex SshPathArgument();

    public override string ConvertArgument(string argument)
    {
        var m = SshPathArgument().Match(argument);
        return m.Success ? m.Groups["pre"].Value + ToHostPath(m.Groups["path"].Value) : argument;
    }

    /// <summary>POSIX シェルの 1 語（'…'。中の ' は '\'' にする）。</summary>
    public static string Quote(string s) => "'" + s.Replace("'", @"'\''") + "'";

    /// <summary>接続先のシェルで実行するコマンド行。</summary>
    public string RemoteCommand(string workingDirectory, IEnumerable<string> arguments, IReadOnlyDictionary<string, string?> env)
    {
        var sb = new StringBuilder();
        sb.Append("cd ").Append(Quote(ToHostPath(workingDirectory))).Append(" && env");
        foreach (var (k, v) in env) if (v is not null) sb.Append(' ').Append(k).Append('=').Append(Quote(v));
        sb.Append(' ').Append(Quote(GitPath));
        foreach (var a in arguments) sb.Append(' ').Append(Quote(ConvertArgument(a)));
        return sb.ToString();
    }

    public override System.Diagnostics.ProcessStartInfo CreateStartInfo(GitExecutable git, GitRequest request, IReadOnlyDictionary<string, string?> env)
    {
        // パスワード・パスフレーズ・既知ホストの確認は GitKnife.AskPass が本体に聞く。AskPass が無ければ BatchMode（鍵と ssh-agent だけ）
        return SshAskPass.StartInfo(Destination, [], [RemoteCommand(request.WorkingDirectory, request.Arguments, env)]);
    }
}

/// <summary>SSH のリポジトリの Windows 側の表し方（\\gitknife-ssh\&lt;Name&gt;\home\a ⇔ ("Name", "/home/a")）。</summary>
public static partial class SshPath
{
    public const string Server = "gitknife-ssh";

    [GeneratedRegex(@"^(?:\\\\|//)gitknife-ssh[\\/](?<name>[^\\/]+)(?<rest>[\\/].*)?$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Unc();

    public static bool TryParse(string path, out string name, out string remotePath)
    {
        var m = Unc().Match(path);
        if (!m.Success) { name = ""; remotePath = ""; return false; }
        name = m.Groups["name"].Value;
        var rest = m.Groups["rest"].Value.Replace('\\', '/').TrimEnd('/');
        remotePath = rest.Length == 0 ? "/" : rest;
        return true;
    }

    public static string ToUnc(string name, string remotePath) => @"\\" + Server + @"\" + name + remotePath.TrimEnd('/').Replace('/', '\\');

    public static bool IsSshPath(string path) => Unc().IsMatch(path);
}

/// <summary>Windows のパスと WSL のパスの変換（T1302）。</summary>
public static partial class WslPath
{
    [GeneratedRegex(@"^(?:\\\\|//)(?:wsl\.localhost|wsl\$)[\\/](?<distro>[^\\/]+)(?<rest>[\\/].*)?$", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Unc();

    /// <summary>\\wsl.localhost\Ubuntu\home\a（\\wsl$\…、/ 区切りも）→ ("Ubuntu", "/home/a")。</summary>
    public static bool TryParseUnc(string path, out string distribution, out string linuxPath)
    {
        var m = Unc().Match(path);
        if (!m.Success) { distribution = ""; linuxPath = ""; return false; }
        distribution = m.Groups["distro"].Value;
        var rest = m.Groups["rest"].Value.Replace('\\', '/').TrimEnd('/');
        linuxPath = rest.Length == 0 ? "/" : rest;
        return true;
    }

    /// <summary>("Ubuntu", "/home/a") → \\wsl.localhost\Ubuntu\home\a。</summary>
    public static string ToUnc(string distribution, string linuxPath) =>
        @"\\wsl.localhost\" + distribution + linuxPath.TrimEnd('/').Replace('/', '\\');

    /// <summary>
    /// C:\a\b → /mnt/c/a/b。ドライブのパスでなければ null。
    /// 推定：WSL の automount の root は既定の /mnt/（wsl.conf の [automount] root を変えた環境は見ていない）。
    /// </summary>
    public static string? FromDrivePath(string path)
    {
        if (path.Length < 2 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || (path.Length > 2 && path[2] is not ('\\' or '/'))) return null;
        var rest = path.Length > 3 ? path[3..].Replace('\\', '/').TrimEnd('/') : "";
        return "/mnt/" + char.ToLowerInvariant(path[0]) + (rest.Length > 0 ? "/" + rest : "");
    }

    /// <summary>/mnt/c/a/b → C:\a\b。/mnt/&lt;ドライブ&gt; の下でなければ null。</summary>
    public static string? ToDrivePath(string linuxPath)
    {
        if (!linuxPath.StartsWith("/mnt/", StringComparison.Ordinal) || linuxPath.Length < 6 || !char.IsAsciiLetter(linuxPath[5])) return null;
        if (linuxPath.Length > 6 && linuxPath[6] != '/') return null;
        var rest = linuxPath.Length > 7 ? linuxPath[7..].TrimEnd('/').Replace('/', '\\') : "";
        return char.ToUpperInvariant(linuxPath[5]) + @":\" + rest;
    }

    /// <summary>ネットワークドライブに割り当てたドライブ（U: → \\wsl.localhost\Ubuntu など）なら UNC に直す。そうでなければそのまま。</summary>
    public static string ResolveMappedDrive(string path)
    {
        if (!OperatingSystem.IsWindows() || path.Length < 2 || !char.IsAsciiLetter(path[0]) || path[1] != ':') return path;
        var unc = GetDriveConnection(path[..2]);
        if (unc is null) return path;
        var rest = path.Length > 2 ? path[2..].Replace('/', '\\') : "";
        return unc.TrimEnd('\\') + (rest.StartsWith('\\') || rest.Length == 0 ? rest : "\\" + rest);
    }

    private static readonly Dictionary<string, (string? unc, DateTime at)> s_driveCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>割り当ての先（WNetGetConnection）。引くたびに数 ms かかるので 30 秒覚えておく。</summary>
    private static string? GetDriveConnection(string drive)
    {
        lock (s_driveCache)
            if (s_driveCache.TryGetValue(drive, out var c) && DateTime.UtcNow - c.at < TimeSpan.FromSeconds(30)) return c.unc;
        string? unc = null;
        try
        {
            var sb = new StringBuilder(512);
            var len = sb.Capacity;
            if (WNetGetConnection(drive, sb, ref len) == 0) unc = sb.ToString();
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        lock (s_driveCache) s_driveCache[drive] = (unc, DateTime.UtcNow);
        return unc;
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)] private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);
}

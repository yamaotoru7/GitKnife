using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using GitKnife.Core.Logging;

namespace GitKnife.Core.Git;

/// <summary>
/// 認証の失敗（"could not read Username for '...': terminal prompts disabled" など）を、後からログで判別できるようにする。
/// 失敗した git の直後に、使った git、効いている credential.* の設定とその出どころ、PATH にある git-credential-* ヘルパー、
/// 関係する環境変数、Windows 資格情報マネージャにそのホストの資格情報があるか（名前とユーザー名だけ。パスワードは読まない）を gitknife.log に書く。
/// 2026-10-28：同梱の PortableGit が helper-selector のままで、別の PC で http のリモートへの push が失敗し続けた。
/// </summary>
public static partial class CredentialDiagnostics
{
    [GeneratedRegex(@"could not read (?:Username|Password) for '|unable to get password from user|Authentication failed for '|terminal prompts disabled|HTTP Basic: Access denied|Invalid username or password", RegexOptions.IgnoreCase)]
    private static partial Regex AuthFailure();

    [GeneratedRegex(@"for '(?<url>[a-z][a-z0-9+.-]*://[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex UrlInMessage();

    [GeneratedRegex(@"^(?:Pushing to|Fetching|Cloning into) (?<url>[a-z][a-z0-9+.-]*://\S+)", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex UrlInProgress();

    public static bool LooksLikeAuthFailure(string stderr) => AuthFailure().IsMatch(stderr);

    /// <summary>stderr からリモートの URL（ユーザー名・パスワードは除く）を取る。見つからなければ null。</summary>
    public static string? FindUrl(string stderr)
    {
        var m = UrlInMessage().Match(stderr);
        if (!m.Success) m = UrlInProgress().Match(stderr);
        return m.Success ? Origin(m.Groups["url"].Value) : null;
    }

    /// <summary>"http://user:pass@host:port/path" → "http://host:port"。URL でなければ null。</summary>
    private static string? Origin(string url) =>
        url.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Authority}" : null;

    public static async Task LogAsync(GitProcess git, GitRequest request, GitResult failed)
    {
        var workingDirectory = request.WorkingDirectory;
        // Completed を購読していない別のインスタンスで呼ぶ（調べるための git を Activity Manager に出さない）
        var probe = new GitProcess(git.Git, git.Host);
        // URL は stderr → 引数の URL → 引数のリモート名（push gitserver ... のように stderr に URL が出ないことがある）の順に探す
        var url = FindUrl(failed.StandardError) ?? request.Arguments.Select(Origin).FirstOrDefault(u => u is not null);
        if (url is null)
        {
            try
            {
                var remotes = (await probe.RunAsync(new GitRequest(workingDirectory, ["remote"])).ConfigureAwait(false)).StandardOutput
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var name = request.Arguments.FirstOrDefault(a => remotes.Contains(a));
                if (name is not null)
                    url = Origin((await probe.RunAsync(new GitRequest(workingDirectory, ["remote", "get-url", name])).ConfigureAwait(false)).StandardOutput);
            }
            catch { }
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Credential diagnostics for '{failed.CommandLine}' (remote {url ?? "?"})");
        sb.AppendLine($"  git: {git.Git.Path}");

        async Task Run(string title, params string[] args)
        {
            try
            {
                var r = await probe.RunAsync(new GitRequest(workingDirectory, args)).ConfigureAwait(false);
                var text = (r.StandardOutput + r.StandardError).Trim();
                sb.AppendLine($"  {title} (exit {r.ExitCode}):");
                foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries)) sb.AppendLine("    " + line.TrimEnd('\r'));
            }
            catch (Exception ex) { sb.AppendLine($"  {title}: {ex.Message}"); }
        }

        await Run("version", "version").ConfigureAwait(false);
        await Run("exec-path", "--exec-path").ConfigureAwait(false);
        // 出どころ付きの credential.* 全部（helper が system / global / local のどこで決まっているか、helperselector.selected の残りも見える）
        await Run("credential config", "config", "--show-scope", "--show-origin", "--get-regexp", @"^credential\.").ConfigureAwait(false);
        // その URL に効く値（credential.<url>.* を含めて git が解決した結果）
        if (url is not null) await Run($"credential config for {url}", "config", "--get-urlmatch", "credential", url).ConfigureAwait(false);

        sb.AppendLine("  environment:");
        foreach (var name in new[] { "GIT_ASKPASS", "SSH_ASKPASS", "GIT_TERMINAL_PROMPT", "GCM_INTERACTIVE", "GCM_GUI_PROMPT", "GCM_ALLOW_UNSAFE_REMOTES", "GCM_CREDENTIAL_STORE", "GIT_CONFIG_GLOBAL", "GIT_CONFIG_SYSTEM", "GIT_CONFIG_NOSYSTEM", "HOME" })
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (v is not null) sb.AppendLine($"    {name}={v}");
        }
        // GIT_CONFIG_PARAMETERS / GIT_CONFIG_KEY_n は値にトークンが入りうるので、あるかどうかだけ
        foreach (var name in new[] { "GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT" })
            if (Environment.GetEnvironmentVariable(name) is not null) sb.AppendLine($"    {name} is set (value not logged)");

        // helper-selector が選択肢に出すのは PATH にある git-credential-*.exe（同じ名前が 2 つ出るのは git が 2 つ PATH にあるとき）
        sb.AppendLine("  git-credential-* on PATH:");
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var d = dir.Trim('"');
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.EnumerateFiles(d, "git-credential-*.exe")) sb.AppendLine("    " + f);
            }
            catch { }
        }

        if (url is not null && OperatingSystem.IsWindows()) AppendStoredCredentials(sb, url);
        Log.Warning(sb.ToString().TrimEnd());
    }

    /// <summary>Windows 資格情報マネージャで、ホスト名を含む項目の名前・ユーザー名・更新日時（パスワードは読まない）。</summary>
    private static void AppendStoredCredentials(StringBuilder sb, string url)
    {
        var host = new Uri(url).Host;
        sb.AppendLine($"  Windows Credential Manager entries containing '{host}':");
        if (!CredEnumerate(null, 0, out var count, out var list))
        {
            sb.AppendLine($"    (CredEnumerate failed: {Marshal.GetLastWin32Error()})");
            return;
        }
        try
        {
            var found = 0;
            for (var i = 0; i < count; i++)
            {
                var c = Marshal.PtrToStructure<CREDENTIAL>(Marshal.ReadIntPtr(list, i * IntPtr.Size));
                var target = c.TargetName ?? "";
                if (!target.Contains(host, StringComparison.OrdinalIgnoreCase)) continue;
                var written = DateTime.FromFileTime(c.LastWritten);
                sb.AppendLine($"    {target}  user={c.UserName ?? ""}  type={c.Type}  written={written:yyyy-MM-dd HH:mm}  hasSecret={c.CredentialBlobSize > 0}");
                found++;
            }
            if (found == 0) sb.AppendLine("    (none)");
        }
        finally { CredFree(list); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string? TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool CredEnumerate(string? filter, int flags, out int count, out IntPtr credentials);
    [DllImport("advapi32.dll")] private static extern void CredFree(IntPtr buffer);
}

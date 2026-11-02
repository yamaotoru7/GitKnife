using System.Diagnostics;

namespace GitKnife.Core.Git;

/// <summary>
/// 使う git.exe の場所。設定 GitInstancePath が null なら、同梱の git（将来）→ PATH の git → Git for Windows の既定の場所の順に探す。
/// </summary>
public sealed class GitExecutable
{
    public string Path { get; }

    public GitExecutable(string path) => Path = path;

    public static GitExecutable Resolve(string? configuredPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredPath) && File.Exists(configuredPath)) return new GitExecutable(configuredPath);
        var bundled = System.IO.Path.Combine(AppContext.BaseDirectory, "git", "cmd", "git.exe");
        if (File.Exists(bundled)) return new GitExecutable(bundled);
        var fromPath = FindOnPath("git.exe");
        if (fromPath is not null) return new GitExecutable(fromPath);
        foreach (var candidate in new[]
        {
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"),
        })
            if (File.Exists(candidate)) return new GitExecutable(candidate);
        throw new FileNotFoundException("git.exe が見つからない。Preferences > Git で指定するか Git for Windows を入れる。");
    }

    /// <summary>
    /// 同じ Git for Windows の bash.exe（カスタムコマンドの Bash）。
    /// git.exe は `cmd\`、`bin\`、`mingw64\bin\` のどれかにあるので、上へたどって `bin\bash.exe` を探す。見つからなければ PATH の bash.exe。
    /// </summary>
    public string? FindBash()
    {
        for (var dir = System.IO.Path.GetDirectoryName(Path); dir is not null; dir = System.IO.Path.GetDirectoryName(dir))
            foreach (var rel in new[] { @"bin\bash.exe", @"usr\bin\bash.exe" })
            {
                var p = System.IO.Path.Combine(dir, rel);
                if (File.Exists(p)) return p;
            }
        return FindOnPath("bash.exe");
    }

    private static string? FindOnPath(string file)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { var p = System.IO.Path.Combine(dir.Trim('"'), file); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }

    /// <summary>`git version` の出力（"git version 2.50.1.windows.1" → "2.50.1.windows.1"）。</summary>
    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var r = await new GitProcess(this).RunAsync(new GitRequest(Environment.CurrentDirectory, ["version"]), ct).ConfigureAwait(false);
        var line = r.StandardOutput.Trim();
        return line.StartsWith("git version ", StringComparison.Ordinal) ? line["git version ".Length..] : line;
    }
}

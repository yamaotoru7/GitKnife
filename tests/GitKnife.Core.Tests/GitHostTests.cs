using GitKnife.Core.Git;

namespace GitKnife.Core.Tests;

public class GitHostTests
{
    [Theory]
    [InlineData(@"\\wsl.localhost\Ubuntu-22.04\home\a\llama.cpp", "Ubuntu-22.04", "/home/a/llama.cpp")]
    [InlineData(@"\\wsl$\Ubuntu\home\a\", "Ubuntu", "/home/a")]
    [InlineData("//wsl.localhost/Ubuntu/home/a", "Ubuntu", "/home/a")]
    [InlineData(@"\\WSL.LOCALHOST\Ubuntu", "Ubuntu", "/")]
    public void ParsesWslUnc(string path, string distro, string linux)
    {
        Assert.True(WslPath.TryParseUnc(path, out var d, out var l));
        Assert.Equal(distro, d);
        Assert.Equal(linux, l);
    }

    [Theory]
    [InlineData(@"C:\Users\a")]
    [InlineData(@"\\server\share\a")]
    public void OtherPathsAreNotWsl(string path)
    {
        Assert.False(WslPath.TryParseUnc(path, out _, out _));
        Assert.IsType<LocalGitHost>(GitHost.FromPath(path));
    }

    [Fact]
    public void WslPathsUseTheDistribution() =>
        Assert.Equal(new WslGitHost("Ubuntu"), GitHost.FromPath(@"\\wsl.localhost\Ubuntu\home\a"));

    [Theory]
    [InlineData(@"C:\Users\a\b", "/mnt/c/Users/a/b")]
    [InlineData("D:/x", "/mnt/d/x")]
    [InlineData(@"E:\", "/mnt/e")]
    public void DrivePaths(string windows, string linux)
    {
        Assert.Equal(linux, WslPath.FromDrivePath(windows));
        Assert.Equal(windows.Replace('/', '\\').TrimEnd('\\') + (windows.Length <= 3 ? @"\" : ""), WslPath.ToDrivePath(linux));
    }

    [Theory]
    [InlineData("/home/a/repo", @"\\wsl.localhost\Ubuntu\home\a\repo")]
    [InlineData("/mnt/c/Users/a", @"C:\Users\a")]
    [InlineData("/mnt/wsl/x", @"\\wsl.localhost\Ubuntu\mnt\wsl\x")]
    [InlineData(".git", ".git")]
    public void WslOutputToLocal(string hostPath, string local) =>
        Assert.Equal(local, new WslGitHost("Ubuntu").ToLocalPath(hostPath));

    [Theory]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\a", "/home/a")]
    [InlineData(@"C:\tmp\patch.diff", "/mnt/c/tmp/patch.diff")]
    [InlineData(@"\\wsl.localhost\Debian\home\a", @"\\wsl.localhost\Debian\home\a")]
    public void LocalToWsl(string local, string hostPath) =>
        Assert.Equal(hostPath, new WslGitHost("Ubuntu").ToHostPath(local));

    [Theory]
    [InlineData("status", "status")]
    [InlineData("main:main", "main:main")]
    [InlineData("src/a.c", "src/a.c")]
    [InlineData(@"C:\tmp\p.diff", "/mnt/c/tmp/p.diff")]
    [InlineData("//wsl.localhost/Ubuntu/home/a/wt", "/home/a/wt")]
    [InlineData(@"--file=C:\tmp\msg.txt", "--file=/mnt/c/tmp/msg.txt")]
    [InlineData("--format=%(refname)%00%(objectname)", "--format=%(refname)%00%(objectname)")]
    public void ConvertsOnlyAbsoluteWindowsPathArguments(string arg, string expected) =>
        Assert.Equal(expected, new WslGitHost("Ubuntu").ConvertArgument(arg));

    [Theory]
    [InlineData(@"\\gitknife-ssh\build01\home\a\repo", "build01", "/home/a/repo")]
    [InlineData("//gitknife-ssh/build01/srv/x/", "build01", "/srv/x")]
    [InlineData(@"\\gitknife-ssh\me@host", "me@host", "/")]
    public void ParsesSshPaths(string path, string name, string remote)
    {
        Assert.True(SshPath.TryParse(path, out var n, out var r));
        Assert.Equal(name, n);
        Assert.Equal(remote, r);
        Assert.Equal(new SshGitHost(name, name), GitHost.FromPath(path));
    }

    [Fact]
    public void SshPathsRoundTrip()
    {
        var host = new SshGitHost("build01", "me@build01.example");
        Assert.Equal(@"\\gitknife-ssh\build01\home\a\repo", host.ToLocalPath("/home/a/repo"));
        Assert.Equal("/home/a/repo", host.ToHostPath(@"\\gitknife-ssh\build01\home\a\repo"));
        Assert.Equal(@"C:\x", host.ToHostPath(@"C:\x"));   // 接続先から見えないパスはそのまま
        Assert.Equal("/home/a/wt", host.ConvertArgument("//gitknife-ssh/build01/home/a/wt"));
        Assert.Equal("src/a.c", host.ConvertArgument("src/a.c"));
    }

    [Fact]
    public void BrowsePathKeepsTheHost()
    {
        Assert.Equal(@"D:\Proj\a", GitHost.Local.BrowsePath(@"D:\Proj\a"));
        Assert.Equal(@"\\gitknife-ssh\build01\home\a", new SshGitHost("build01", "me@build01").BrowsePath("/home/a"));
        // /mnt/c も WSL のまま（ToLocalPath は C:\ になる）
        Assert.Equal(@"\\wsl.localhost\Ubuntu\mnt\c\x", new WslGitHost("Ubuntu").BrowsePath("/mnt/c/x"));
    }

    [Theory]
    [InlineData("me@build01.example", "build01.example")]
    [InlineData("build01", "build01")]
    [InlineData("a@b@c/d", "cd")]
    public void NamesHostFromDestination(string destination, string name) =>
        Assert.Equal(name, GitKnife.Core.Settings.SshHostSettings.NameFromDestination(destination));

    /// <summary>Open Repository の WSL の場所（WSL が無い PC では何もしない）。</summary>
    [Fact]
    public void ListsWslAndFindsHome()
    {
        var names = WslGitHost.Distributions();
        if (names.Count == 0) return;
        var home = new WslGitHost(names[0]).HomeDirectory();
        Assert.StartsWith("/", home);
        Assert.DoesNotContain("/mnt/", home);
    }

    [Fact]
    public void SshHostResolverGivesRegisteredHost()
    {
        var registered = new SshGitHost("build01", "me@build01.example", "/opt/git/bin/git");
        GitHost.SshHostResolver = n => n == "build01" ? registered : null;
        try { Assert.Same(registered, GitHost.FromPath(@"\\gitknife-ssh\build01\home\a")); }
        finally { GitHost.SshHostResolver = null; }
    }

    [Fact]
    public void SshRemoteCommandQuotesEveryWord()
    {
        var host = new SshGitHost("b", "b");
        var cmd = host.RemoteCommand(@"\\gitknife-ssh\b\home\a b\it's", ["log", "--format=%H%x00%s", "--", "file with space"],
            new Dictionary<string, string?> { ["GIT_TERMINAL_PROMPT"] = "0", ["GIT_CONFIG_VALUE_0"] = "*" });
        Assert.Equal(@"cd '/home/a b/it'\''s' && env GIT_TERMINAL_PROMPT='0' GIT_CONFIG_VALUE_0='*' 'git' 'log' '--format=%H%x00%s' '--' 'file with space'", cmd);
    }

    /// <summary>
    /// WSL の中で実行ビット付きのファイルを持つリポジトリを作り、WSL の git では変更なし、Windows の git では mode の変更に見えるのを確かめる（llama.cpp の症状）。
    /// WSL が無い PC では何もしない。
    /// </summary>
    [Fact]
    public async Task WslGitSeesExecutableBits()
    {
        var distro = await DefaultDistributionAsync();
        if (distro is null) return;
        var host = new WslGitHost(distro);
        var wsl = new GitProcess(GitExecutable.Resolve(null), host);
        var linux = "/tmp/gitknife-test-" + Guid.NewGuid().ToString("N");
        var unc = host.ToLocalPath(linux);
        Assert.Equal(0, (await new GitProcess(GitExecutable.Resolve(null), host).RunAsync(new GitRequest(@"C:\", ["init", "-q", unc]))).ExitCode);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(unc, "run.sh"), "#!/bin/sh\n");
            await RunInWsl(distro, "chmod", "+x", linux + "/run.sh");
            await wsl.RunAsync(new GitRequest(unc, ["add", "run.sh"]));
            await wsl.RunAsync(new GitRequest(unc, ["-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", "x"]));

            var top = await wsl.RunAsync(new GitRequest(unc, ["rev-parse", "--show-toplevel"]));
            Assert.Equal(unc, host.ToLocalPath(top.StandardOutput.Trim()));
            Assert.Equal("", (await wsl.RunAsync(new GitRequest(unc, ["status", "--porcelain"]))).StandardOutput);
            var local = await new GitProcess(GitExecutable.Resolve(null)).RunAsync(new GitRequest(unc, ["status", "--porcelain"]));
            Assert.Contains("run.sh", local.StandardOutput);
        }
        finally { await RunInWsl(distro, "rm", "-rf", linux); }
    }

    /// <summary>WSL の git から GitKnife.RI.exe（Windows の exe）を sequence.editor / core.editor として呼び、並べ替えと reword ができる。</summary>
    [Fact]
    public async Task InteractiveRebaseRunsRiFromWsl()
    {
        var distro = await DefaultDistributionAsync();
        if (distro is null) return;
        var host = new WslGitHost(distro);
        var wsl = new GitProcess(GitExecutable.Resolve(null), host);
        var linux = "/tmp/gitknife-ir-" + Guid.NewGuid().ToString("N");
        var unc = host.ToLocalPath(linux);
        var work = Path.Combine(Path.GetTempPath(), "GitKnife.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            await wsl.RunAsync(new GitRequest(@"C:\", ["init", "-q", "--initial-branch=main", unc]));
            async Task<string> Git(params string[] a) => (await wsl.RunAsync(new GitRequest(unc, ["-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", .. a]))).StandardOutput.Trim();
            foreach (var s in new[] { "one", "two", "three", "four" }) await Git("commit", "-q", "--allow-empty", "-m", s);
            var two = await Git("rev-parse", "HEAD~2");
            var three = await Git("rev-parse", "HEAD~1");
            var four = await Git("rev-parse", "HEAD");
            var @base = await Git("rev-parse", "HEAD~3");

            var planFile = Path.Combine(work, "plan.txt");
            var messages = Path.Combine(work, "messages");
            Directory.CreateDirectory(messages);
            await File.WriteAllTextAsync(planFile, InteractiveRebase.FormatPlan([new("pick", three), new("reword", two), new("pick", four)]));
            await File.WriteAllTextAsync(Path.Combine(messages, two + ".txt"), "two (reworded)\n");
            var ri = Path.Combine(AppContext.BaseDirectory, "GitKnife.RI.exe");

            var r = await wsl.RunAsync(new GitRequest(unc, ["-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false",
                .. InteractiveRebase.Arguments(ri, planFile, messages, @base, host)]) { Environment = InteractiveRebase.Environment(ri, planFile, messages, host) });
            Assert.True(r.Succeeded, r.StandardError);
            Assert.Equal("four\ntwo (reworded)\nthree", await Git("log", "-3", "--format=%s"));
        }
        finally
        {
            await RunInWsl(distro, "rm", "-rf", linux);
            try { Directory.Delete(work, true); } catch { }
        }
    }

    /// <summary>SSH の接続先で使う awk 版の計画の当てはめが、C# の ApplyPlan と同じ結果になる（WSL の awk で動かす）。</summary>
    [Fact]
    public async Task ApplyPlanAwkMatchesCSharp()
    {
        var distro = await DefaultDistributionAsync();
        if (distro is null) return;
        var host = new WslGitHost(distro);
        var linux = "/tmp/gitknife-awk-" + Guid.NewGuid().ToString("N");
        var unc = host.ToLocalPath(linux);
        string Sha(char c) => new(c, 40);
        var todo = string.Join("\n",
            "label onto", "",
            $"p {Sha('a')} # first #!_{Sha('a')}",
            $"p {Sha('b')} # second #!_{Sha('b')}",
            "update-ref refs/heads/topic", "",
            $"p {Sha('c')} # third #!_{Sha('c')}",
            $"# p {Sha('d')} commented #!_{Sha('d')}",
            "", "# Rebase onto", "# Commands:", "") + "\n";
        RebasePlanStep[][] plans =
        [
            [new("pick", Sha('a')), new("pick", Sha('b')), new("pick", Sha('c'))],
            [new("pick", Sha('c')), new("reword", Sha('a'))],                                    // 並べ替えと drop
            [new("squash", Sha('b')), new("pick", Sha('a')), new("fixup", Sha('c')), InteractiveRebase.NoUpdateRefs],
            [new("edit", Sha('a')[..7]), new("pick", Sha('b')), new("pick", Sha('c')), new("pick", Sha('e'))],   // 短い sha と無いコミット
        ];
        await RunInWsl(distro, "mkdir", "-p", linux);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(unc, "apply.awk"), InteractiveRebase.ApplyPlanAwk.Replace("\r\n", "\n") + "\n");
            await File.WriteAllTextAsync(Path.Combine(unc, "todo"), todo);
            foreach (var plan in plans)
            {
                var planText = InteractiveRebase.FormatPlan(plan).Replace("\n", ";");
                var psi = new System.Diagnostics.ProcessStartInfo(WslGitHost.WslExe) { RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = System.Text.Encoding.UTF8 };
                foreach (var a in new[] { "-d", distro, "--cd", linux, "--exec", "awk", "-v", "PLAN=" + planText, "-f", "apply.awk", "todo" }) psi.ArgumentList.Add(a);
                using var p = System.Diagnostics.Process.Start(psi)!;
                var awk = await p.StandardOutput.ReadToEndAsync();
                await p.WaitForExitAsync();
                Assert.Equal(InteractiveRebase.ApplyPlan(todo, plan), awk);
            }
        }
        finally { await RunInWsl(distro, "rm", "-rf", linux); }
    }

    /// <summary>常駐のシェル（SSH の使い回し。T1307）の受け渡し：stdin、バイナリの stdout、stderr、終了コード、続けて何回も、並べて。WSL の sh で動かす。</summary>
    [Fact]
    public async Task RemoteShellSessionRoundTrips()
    {
        var distro = await DefaultDistributionAsync();
        if (distro is null) return;
        using var pool = new RemoteShellPool(() =>
        {
            var psi = new System.Diagnostics.ProcessStartInfo(WslGitHost.WslExe);
            foreach (var a in new[] { "-d", distro, "--exec", "sh" }) psi.ArgumentList.Add(a);
            return psi;
        }, size: 2);
        var r = await pool.RunAsync("cat; echo err >&2; exit 3", System.Text.Encoding.UTF8.GetBytes("héllo\nworld"));
        Assert.Equal(3, r.ExitCode);
        Assert.Equal("héllo\nworld", r.Text);
        Assert.Equal("err\n", r.ErrorText);
        var bytes = (await pool.RunAsync("printf '\\000\\001\\377'", null)).Output;
        Assert.Equal(new byte[] { 0, 1, 255 }, bytes);
        // cd はサブシェルの中だけ
        await pool.RunAsync("cd /tmp", null);
        Assert.Equal("/", (await pool.RunAsync("cd / && pwd", null)).Text.Trim());
        var many = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => pool.RunAsync($"echo {i}", null)));
        Assert.Equal(Enumerable.Range(0, 8).Select(i => i.ToString()), many.Select(m => m.Text.Trim()));
        // 大きい出力（コミット一覧の log くらい）
        Assert.Equal(3_000_000, (await pool.RunAsync("head -c 3000000 /dev/zero", null)).Output.Length);
    }

    private static async Task<string?> DefaultDistributionAsync()
    {
        if (!File.Exists(WslGitHost.WslExe)) return null;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(WslGitHost.WslExe, "-l -q") { RedirectStandardOutput = true, StandardOutputEncoding = System.Text.Encoding.Unicode, CreateNoWindow = true };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var output = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return p.ExitCode == 0 ? output.Split('\n',StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(l => l.Length > 0) : null;
        }
        catch { return null; }
    }

    private static async Task RunInWsl(string distro, params string[] args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(WslGitHost.WslExe) { CreateNoWindow = true };
        foreach (var a in new[] { "-d", distro, "--exec" }.Concat(args)) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        await p.WaitForExitAsync();
    }
}

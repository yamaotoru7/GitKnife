using System.Collections.Concurrent;
using System.Diagnostics;
using GitKnife.Core.Git;

namespace GitKnife.Core.Tests;

/// <summary>WSL の中の見張り。WSL が無い PC では何もしない。</summary>
public class RemoteRepositoryWatcherTests
{
    private static string? Distribution() => WslGitHost.Distributions().FirstOrDefault();

    private static string Wsl(string distro, string command)
    {
        var psi = new ProcessStartInfo(WslGitHost.WslExe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in new[] { "-d", distro, "--exec", "sh", "-c", command }) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return output;
    }

    private static bool WaitFor(Func<bool> condition, int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms) { if (condition()) return true; Thread.Sleep(100); }
        return condition();
    }

    [Fact]
    public void ReportsReferenceAndIndexChangesAndStops()
    {
        if (Distribution() is not { } distro) return;
        var dir = "/tmp/gk-watch-test-" + Guid.NewGuid().ToString("N")[..8];
        Wsl(distro, $"git init -q {dir} && cd {dir} && git -c user.name=a -c user.email=a@b commit -q --allow-empty -m a");
        try
        {
            var host = new WslGitHost(distro);
            var local = WslPath.ToUnc(distro, dir);
            var events = new ConcurrentQueue<bool>();
            var reg = RemoteRepositoryWatcher.Watch(host, local, local + @"\.git", local + @"\.git", events.Enqueue)!;
            Assert.True(WaitFor(() => reg.IsWatching, 15_000), "watcher did not become ready");

            // 向こうでの commit → 参照の変更
            Wsl(distro, $"cd {dir} && git -c user.name=a -c user.email=a@b commit -q --allow-empty -m b");
            Assert.True(WaitFor(() => events.Contains(true), 5_000), "commit was not reported");

            // stage → index の変更（status だけ）
            while (events.TryDequeue(out _)) { }
            Wsl(distro, $"cd {dir} && echo x > f && git add f");
            Assert.True(WaitFor(() => events.Contains(false), 5_000), "index change was not reported");
            Assert.DoesNotContain(true, events);

            // ファイルを足しただけ（作業ツリーの変更。inotifywait か python3 があるとき）
            if (Wsl(distro, "command -v inotifywait || command -v python3").Trim().Length > 0)
            {
                while (events.TryDequeue(out _)) { }
                Wsl(distro, $"mkdir -p {dir}/sub && echo y > {dir}/sub/new.txt");
                Assert.True(WaitFor(() => events.Contains(false), 3_000), "new file was not reported");
                // 作った後のフォルダの中も見る
                while (events.TryDequeue(out _)) { }
                Thread.Sleep(300);
                while (events.TryDequeue(out _)) { }
                Wsl(distro, $"echo z > {dir}/sub/other.txt");
                Assert.True(WaitFor(() => events.Contains(false), 3_000), "file in a new folder was not reported");
            }

            // 外したら接続先のループも終わる（[/] は pgrep を動かす sh -c 自身のコマンド行に当たらないように）
            // （sh のループも python3 / inotifywait も、引数にリポジトリのパスがある）
            var pgrep = $"pgrep -f '[/]{dir[1..]}' || true";
            Assert.NotEmpty(Wsl(distro, pgrep).Trim());
            reg.Dispose();
            Assert.True(WaitFor(() => Wsl(distro, pgrep).Trim().Length == 0, 5_000), "remote loop is still running");
        }
        finally
        {
            RemoteRepositoryWatcher.StopAll();
            Wsl(distro, $"rm -rf {dir}");
        }
    }
}

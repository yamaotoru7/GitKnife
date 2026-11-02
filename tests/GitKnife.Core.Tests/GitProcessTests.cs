using System.Text;
using GitKnife.Core.Git;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Logging;

namespace GitKnife.Core.Tests;

/// <summary>実際の git.exe を使う。一時リポジトリを作って消す。</summary>
public class GitProcessTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GitKnife.Tests", Guid.NewGuid().ToString("N"));
    private readonly GitExecutable _git = GitExecutable.Resolve(null);
    private readonly GitProcess _proc;

    public GitProcessTests()
    {
        Directory.CreateDirectory(_dir);
        _proc = new GitProcess(_git);
        Run("init", "-q", "--initial-branch=main");
        Run("config", "user.name", "Test");
        Run("config", "user.email", "test@example.com");
        Run("config", "commit.gpgsign", "false");
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(_dir, true);
        }
        catch { }
    }

    private GitResult Run(params string[] args) => _proc.RunAsync(new GitRequest(_dir, args)).GetAwaiter().GetResult();

    [Fact]
    public async Task VersionIsReadable()
    {
        var v = await _git.GetVersionAsync();
        Assert.Matches(@"^\d+\.\d+", v);
    }

    [Fact]
    public async Task StdinPathsAreStaged()
    {
        File.WriteAllText(Path.Combine(_dir, "a.txt"), "a\n");
        File.WriteAllText(Path.Combine(_dir, "日本語 名前.txt"), "b\n");   // 空白と非 ASCII のパス
        var add = await _proc.RunAsync(new GitRequest(_dir, GitArgs.Add()) { StandardInput = GitArgs.NulSeparated(["a.txt", "日本語 名前.txt"]) });
        add.ThrowIfFailed();
        var status = await _proc.RunAsync(new GitRequest(_dir, GitArgs.StatusPorcelain()));
        var entries = status.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("A  a.txt", entries);
        Assert.Contains("A  日本語 名前.txt", entries);
    }

    [Fact]
    public async Task FailureCarriesStderrAndLogsRunningLine()
    {
        var lines = new List<string>();
        Log.UseSink(lines.Add);
        try
        {
            var r = await _proc.RunAsync(new GitRequest(_dir, ["checkout", "no-such-branch"]));
            Assert.False(r.Succeeded);
            Assert.Contains("no-such-branch", r.StandardError);
            var ex = Assert.Throws<GitException>(() => r.ThrowIfFailed());
            Assert.Contains("no-such-branch", ex.Message);
            Assert.Contains(lines, l => l.Contains("Running '") && l.Contains("checkout no-such-branch"));
        }
        finally { Log.UseSink(null); }
    }

    /// <summary>401 を返すだけの HTTP サーバーに fetch し、認証の失敗で資格情報の診断がログに出ること（CredentialDiagnostics）。</summary>
    [Fact]
    public async Task AuthFailureLogsCredentialDiagnostics()
    {
        var port = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        port.Start();
        var p = ((System.Net.IPEndPoint)port.LocalEndpoint).Port;
        port.Stop();
        using var http = new System.Net.HttpListener();
        http.Prefixes.Add($"http://localhost:{p}/");
        http.Start();
        _ = Task.Run(async () =>
        {
            while (http.IsListening)
            {
                try
                {
                    var ctx = await http.GetContextAsync();
                    ctx.Response.StatusCode = 401;
                    ctx.Response.AddHeader("WWW-Authenticate", "Basic realm=\"test\"");
                    ctx.Response.Close();
                }
                catch { break; }
            }
        });

        var lines = new List<string>();
        Log.UseSink(lines.Add);
        try
        {
            // ヘルパーを空にして GCM の画面を出さない → GIT_TERMINAL_PROMPT=0 で "could not read Username"
            var r = await _proc.RunAsync(new GitRequest(_dir, ["-c", "credential.helper=", "fetch", $"http://localhost:{p}/repo.git"]));
            Assert.False(r.Succeeded);
            Assert.True(CredentialDiagnostics.LooksLikeAuthFailure(r.StandardError), r.StandardError);
            var diag = Assert.Single(lines, l => l.Contains("Credential diagnostics"));
            Assert.Contains($"remote http://localhost:{p}", diag);
            Assert.Contains("credential config", diag);
            Assert.Contains("git-credential-* on PATH", diag);
        }
        finally { Log.UseSink(null); http.Stop(); }
    }

    [Fact]
    public async Task CommitViaMessageFileAndBinaryOutput()
    {
        File.WriteAllText(Path.Combine(_dir, "a.bin"), "x");
        (await _proc.RunAsync(new GitRequest(_dir, GitArgs.Add()) { StandardInput = GitArgs.NulSeparated(["a.bin"]) })).ThrowIfFailed();
        var msgFile = Path.Combine(_dir, ".git", "COMMITMESSAGE");
        File.WriteAllText(msgFile, "Subject\n\nBody\n", new UTF8Encoding(false));
        (await _proc.RunAsync(new GitRequest(_dir, GitArgs.Commit(msgFile, amend: false)))).ThrowIfFailed();
        var log = await _proc.RunAsync(new GitRequest(_dir, GitArgs.LastCommitMessage()));
        Assert.StartsWith("Subject", log.StandardOutput);
        var blob = await _proc.RunAsync(new GitRequest(_dir, ["show", "HEAD:a.bin"]) { BinaryOutput = true });
        Assert.Equal("x"u8.ToArray(), blob.BinaryStandardOutput);
    }

    [Fact]
    public async Task CancellationKillsTheProcess()
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(300);
        // 応答しないアドレスへの接続待ち（タイムアウト 60 秒）を 300ms でキャンセルする
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _proc.RunAsync(new GitRequest(_dir, ["-c", "http.connectTimeout=60", "ls-remote", "http://10.255.255.1:9/x.git"]), cts.Token));
    }
}

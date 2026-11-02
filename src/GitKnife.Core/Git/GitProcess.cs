using System.Diagnostics;
using System.Text;
using GitKnife.Core.Logging;

namespace GitKnife.Core.Git;

/// <summary>1 回の git 起動の指示。引数は配列で持ち、クォートは Process に任せる。</summary>
public sealed record GitRequest(string WorkingDirectory, IReadOnlyList<string> Arguments)
{
    /// <summary>stdin に流す内容（NUL 区切りのパス一覧など）。null なら stdin を閉じる。</summary>
    public byte[]? StandardInput { get; init; }
    /// <summary>追加の環境変数（GIT_ASKPASS、GIT_SEQUENCE_EDITOR など）。</summary>
    public IReadOnlyDictionary<string, string>? Environment { get; init; }
    /// <summary>stderr の 1 行（または進捗の 1 片）ごとに呼ばれる。進捗表示用。</summary>
    public Action<string>? OnProgress { get; init; }
    /// <summary>stdout をバイナリとして受けたい（blob の内容など）。</summary>
    public bool BinaryOutput { get; init; }

    /// <summary>Activity Manager とログに出す 1 行（ログでは "Running '...'" の形）。</summary>
    public string CommandLine => "git " + string.Join(' ', Arguments.Select(QuoteForDisplay));

    private static string QuoteForDisplay(string a) => a.Length == 0 || a.Any(char.IsWhiteSpace) ? '"' + a.Replace("\"", "\\\"") + '"' : a;
}

public sealed class GitResult
{
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }
    public byte[]? BinaryStandardOutput { get; init; }
    public required TimeSpan Elapsed { get; init; }
    public required string CommandLine { get; init; }
    public bool Succeeded => ExitCode == 0;

    /// <summary>失敗していれば GitException を投げる（exit 1 を正常とみなす diff などは呼ばない）。</summary>
    public GitResult ThrowIfFailed()
    {
        if (!Succeeded) throw new GitException(this);
        return this;
    }
}

public sealed class GitException(GitResult result) : Exception(BuildMessage(result))
{
    public GitResult Result { get; } = result;

    private static string BuildMessage(GitResult r)
    {
        var err = r.StandardError.Trim();
        return err.Length > 0 ? err : $"{r.CommandLine} failed with exit code {r.ExitCode}";
    }
}

/// <summary>
/// git.exe の起動。UTF-8 固定、プロンプト禁止、進捗はパイプ経由（--progress は stderr に CR 区切りで来る）、キャンセルで kill。
/// 「Running '...'」をログに残す。
/// </summary>
public sealed class GitProcess(GitExecutable git, GitHost? host = null)
{
    public GitExecutable Git { get; } = git;

    /// <summary>接続先（T1301）。Local は <see cref="Git"/> の git.exe、WSL はそのディストリビューションの git。</summary>
    public GitHost Host { get; } = host ?? GitHost.Local;

    /// <summary>同じ git の設定で別の接続先に向けたもの。Completed はこちらにも流す（Activity Manager はこちらだけを購読している）。</summary>
    public GitProcess WithHost(GitHost host)
    {
        var p = new GitProcess(Git, host);
        p.Completed += r => Completed?.Invoke(r);
        return p;
    }

    /// <summary>git が 1 回終わるたび（Activity Manager が実行中の操作の出力を集める。呼び出し元の非同期の流れの中で呼ばれる）。</summary>
    public event Action<GitResult>? Completed;

    /// <summary>
    /// 持ち主の違うフォルダ（WSL の \\wsl.localhost\… や U: など）でも git を動かす：safe.directory=* をこの呼び出しだけに渡す
    /// （GIT_CONFIG_COUNT。コマンドの文字列は変わらないので Activity Manager の表示は変わらない）。
    /// git の安全チェック（dubious ownership）で、safe.directory に登録していない epgrec-next が何も読めなかった。
    /// GitKnife が git を呼ぶのはユーザーが開いた・選んだリポジトリだけなので、
    /// ユーザーの判断（2026-10-27「開いたものは通す」）でチェックを外す。グローバル設定は変えない。
    /// </summary>
    private static void AllowAnyOwner(IDictionary<string, string?> env)
    {
        var n = env.TryGetValue("GIT_CONFIG_COUNT", out var c) && int.TryParse(c, out var existing) && existing > 0 ? existing : 0;
        env[$"GIT_CONFIG_KEY_{n}"] = "safe.directory";
        env[$"GIT_CONFIG_VALUE_{n}"] = "*";
        env["GIT_CONFIG_COUNT"] = (n + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<GitResult> RunAsync(GitRequest request, CancellationToken ct = default)
    {
        // 対話を禁止し、出力を機械的に読める形に固定する
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_PAGER"] = "cat",
            ["LC_ALL"] = "C.UTF-8",
            ["GIT_OPTIONAL_LOCKS"] = "0",
        };
        // GIT_CONFIG_COUNT は利用者の環境に既にあれば続きの番号から。SSH の先には環境が渡らないので、元の KEY / VALUE も入れて一緒に渡す
        if (Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT") is { } count && int.TryParse(count, out var n))
        {
            env["GIT_CONFIG_COUNT"] = count;
            for (var i = 0; i < n; i++)
            {
                env[$"GIT_CONFIG_KEY_{i}"] = Environment.GetEnvironmentVariable($"GIT_CONFIG_KEY_{i}") ?? "";
                env[$"GIT_CONFIG_VALUE_{i}"] = Environment.GetEnvironmentVariable($"GIT_CONFIG_VALUE_{i}") ?? "";
            }
        }
        AllowAnyOwner(env);
        if (request.Environment is not null) foreach (var (k, v) in request.Environment) env[k] = v;

        // SSH で進捗の要らない git は常駐のシェルに流す（ssh を張り直さない。T1307）
        if (Host is SshGitHost ssh && request.OnProgress is null)
        {
            var command = ssh.RemoteCommand(request.WorkingDirectory, request.Arguments, env);
            Log.Info($"Running '{command}' ({Host.DisplayName}, session)");
            var watch = Stopwatch.StartNew();
            ShellResult? shell = null;
            try { shell = await ssh.Pool.RunAsync(command, request.StandardInput, ct).ConfigureAwait(false); }
            catch (IOException ex) { Log.Warning($"remote shell failed, falling back to one ssh per git: {ex.Message}"); }
            if (shell is not null)
            {
                watch.Stop();
                var pooled = new GitResult
                {
                    ExitCode = shell.ExitCode,
                    StandardOutput = request.BinaryOutput ? "" : new UTF8Encoding(false).GetString(shell.Output),
                    StandardError = shell.ErrorText,
                    BinaryStandardOutput = request.BinaryOutput ? shell.Output : null,
                    Elapsed = watch.Elapsed,
                    CommandLine = request.CommandLine,
                };
                if (!pooled.Succeeded) Log.Debug($"git exited {pooled.ExitCode}: {pooled.StandardError.Trim()}");
                Completed?.Invoke(pooled);
                return pooled;
            }
        }

        // 起動の仕方は接続先が決める（Local：git.exe、WSL：wsl.exe、SSH：ssh.exe）
        var psi = Host.CreateStartInfo(Git, request, env);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardOutputEncoding = request.BinaryOutput ? null : new UTF8Encoding(false);
        psi.StandardErrorEncoding = new UTF8Encoding(false);

        Log.Info(Host is LocalGitHost ? $"Running '{Git.Path} {string.Join(' ', request.Arguments)}'" : $"Running '{string.Join(' ', psi.ArgumentList)}' ({Host.DisplayName})");
        var sw = Stopwatch.StartNew();
        using var p = new Process { StartInfo = psi };
        p.Start();

        // stdin
        var stdinTask = Task.Run(async () =>
        {
            try
            {
                if (request.StandardInput is not null) await p.StandardInput.BaseStream.WriteAsync(request.StandardInput, ct).ConfigureAwait(false);
            }
            finally { try { p.StandardInput.Close(); } catch { } }
        }, ct);

        // stdout（テキストかバイナリ）
        Task<string>? stdoutText = null;
        Task<byte[]>? stdoutBytes = null;
        if (request.BinaryOutput)
            stdoutBytes = Task.Run(async () => { using var ms = new MemoryStream(); await p.StandardOutput.BaseStream.CopyToAsync(ms, ct).ConfigureAwait(false); return ms.ToArray(); }, ct);
        else
            stdoutText = p.StandardOutput.ReadToEndAsync(ct);

        // stderr：進捗（\r 区切り）を逐次通知しつつ全文も貯める
        var stderrTask = Task.Run(async () =>
        {
            var all = new StringBuilder();
            var current = new StringBuilder();
            var buffer = new char[1024];
            int n;
            while ((n = await p.StandardError.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                for (var i = 0; i < n; i++)
                {
                    var c = buffer[i];
                    all.Append(c);
                    if (c is '\r' or '\n')
                    {
                        if (current.Length > 0) { request.OnProgress?.Invoke(current.ToString()); current.Clear(); }
                    }
                    else current.Append(c);
                }
            }
            if (current.Length > 0) request.OnProgress?.Invoke(current.ToString());
            return all.ToString();
        }, ct);

        try
        {
            await p.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        await stdinTask.ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        var stdout = stdoutText is not null ? await stdoutText.ConfigureAwait(false) : "";
        var bytes = stdoutBytes is not null ? await stdoutBytes.ConfigureAwait(false) : null;
        sw.Stop();

        var result = new GitResult
        {
            ExitCode = p.ExitCode,
            StandardOutput = stdout,
            StandardError = stderr,
            BinaryStandardOutput = bytes,
            Elapsed = sw.Elapsed,
            CommandLine = request.CommandLine,
        };
        if (!result.Succeeded) Log.Debug($"git exited {result.ExitCode}: {stderr.Trim()}");
        if (!result.Succeeded && CredentialDiagnostics.LooksLikeAuthFailure(stderr))
        {
            try { await CredentialDiagnostics.LogAsync(this, request, result).ConfigureAwait(false); }
            catch (Exception ex) { Log.Debug($"credential diagnostics failed: {ex.Message}"); }
        }
        Completed?.Invoke(result);
        return result;
    }
}

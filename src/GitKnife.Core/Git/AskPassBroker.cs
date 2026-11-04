using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using GitKnife.Core.Logging;

namespace GitKnife.Core.Git;

/// <summary>答えを保存する場所（アプリでは Windows の資格情報マネージャー）。</summary>
public interface IAskPassStore
{
    string? Read(string key);
    void Write(string key, string secret);
    void Delete(string key);
}

/// <summary>利用者の答え。Remember は保存するか（パスワードとパスフレーズだけ）。</summary>
public sealed record AskPassAnswer(string Value, bool Remember);

/// <summary>画面に出す質問。Retry は前の答えが通らなかったとき（パスワードが違う）。</summary>
public sealed record AskPassRequest(AskPassQuestion Question, bool Retry);

/// <summary>
/// ssh の SSH_ASKPASS（GitKnife.AskPass.exe）からの質問を名前付きパイプで受けて答える。
/// ssh を起動するたびに <see cref="RegisterSession"/> でセッションを作り、環境変数で AskPass に渡す（どの接続先の質問か分かる。知らないセッションには答えない）。
/// パスワードとパスフレーズは、アプリが動いている間は覚え（同じ接続先に ssh を何本も張るので）、Remember なら保存もする。
/// 同じ ssh が同じ質問をもう一度したら、前の答えは違っていたとして忘れ、画面で聞き直す。
/// </summary>
public sealed class AskPassBroker : IDisposable
{
    private readonly Func<AskPassRequest, CancellationToken, Task<AskPassAnswer?>> _prompt;
    private readonly IAskPassStore _store;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTime> _declined = new(StringComparer.Ordinal);

    /// <summary>キャンセルの後、同じ質問を画面に出さずに断る時間（ssh を何本も張るので、続けて何度も聞かない）。</summary>
    public TimeSpan DeclineQuietPeriod { get; init; } = TimeSpan.FromSeconds(15);

    public string PipeName { get; }

    private sealed class Session(string destination)
    {
        public string Destination { get; } = destination;
        public DateTime Created { get; } = DateTime.UtcNow;
        /// <summary>このセッションに返した答え（質問の Key → 答え）。</summary>
        public ConcurrentDictionary<string, string> Answered { get; } = new(StringComparer.Ordinal);
        /// <summary>このセッションで利用者が断った質問（ssh は断っても NumberOfPasswordPrompts の回数まで聞き直すので、もう画面を出さない）。</summary>
        public ConcurrentDictionary<string, bool> Declined { get; } = new(StringComparer.Ordinal);
    }

    public AskPassBroker(Func<AskPassRequest, CancellationToken, Task<AskPassAnswer?>> prompt, IAskPassStore store, string? pipeName = null)
    {
        _prompt = prompt;
        _store = store;
        PipeName = pipeName ?? $"GitKnife.AskPass.{Environment.ProcessId}.{Guid.NewGuid():N}";
        for (var i = 0; i < 4; i++) _ = Task.Run(() => ListenAsync(_cts.Token));
    }

    /// <summary>ssh を 1 本起動するためのセッション（AskPass に渡す値）。</summary>
    public string RegisterSession(string destination)
    {
        // 古いセッションを掃除する（常駐のシェルは長く生きるので、数で区切る）
        if (_sessions.Count > 2000)
            foreach (var (id, s) in _sessions.OrderBy(p => p.Value.Created).Take(1000).ToList()) _sessions.TryRemove(id, out _);
        var session = Guid.NewGuid().ToString("N");
        _sessions[session] = new Session(destination);
        return session;
    }

    /// <summary>覚えた答え（保存したものも）を消す（Preferences でホストを消したときなど）。</summary>
    public void Forget(string key)
    {
        _cache.TryRemove(key, out _);
        _declined.TryRemove(key, out _);
        try { _store.Delete(key); } catch (Exception ex) { Log.Warning($"AskPass: deleting the saved secret failed: {ex.Message}"); }
    }

    /// <summary>接続先のパスワードを覚えているか（保存したものも）。</summary>
    public bool HasSecret(string key) => _cache.ContainsKey(key) || ReadStore(key) is not null;

    private async Task ListenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? server = null;
            try
            {
                server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(ct).ConfigureAwait(false);
                var connected = server;
                server = null;
                _ = Task.Run(() => ServeAsync(connected, ct), ct);
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                Log.Warning($"AskPass: listening failed: {ex.Message}");
                try { await Task.Delay(500, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
            finally { server?.Dispose(); }
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe;
        try
        {
            using var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 1024, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, leaveOpen: true) { NewLine = "\n", AutoFlush = true };
            var request = await AskPassProtocol.ReadRequestAsync(reader, ct).ConfigureAwait(false);
            string? answer = null;
            if (request is { } r) answer = await AnswerAsync(r.Session, r.Prompt, ct).ConfigureAwait(false);
            await writer.WriteLineAsync(AskPassProtocol.FormatAnswer(answer)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException) { Log.Debug($"AskPass: {ex.Message}"); }
        catch (Exception ex) { Log.Warning($"AskPass: answering failed: {ex}"); }
    }

    /// <summary>質問に答える（断るときは null）。パイプを通さずにテストからも呼ぶ。</summary>
    public async Task<string?> AnswerAsync(string sessionId, string prompt, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            Log.Warning("AskPass: a question from an unknown session was refused");
            return null;
        }
        var q = AskPassClassifier.Classify(session.Destination, prompt);
        Log.Info($"AskPass: {q.Kind} for {q.Destination}");
        // 同じ接続先・同じ鍵の質問は 1 つずつ（重なった ssh には 1 回聞いた答えを使い回す）
        var gate = _locks.GetOrAdd(q.Key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (session.Declined.ContainsKey(q.Key)) return q.Kind == AskPassKind.HostKey ? "no" : null;
            var retry = false;
            if (q.IsSecret)
            {
                if (session.Answered.TryGetValue(q.Key, out var previous))
                {
                    // 同じ ssh がもう一度聞いた：前の答えは通らなかった
                    retry = true;
                    if (_cache.TryGetValue(q.Key, out var cached) && cached == previous) _cache.TryRemove(q.Key, out _);
                    if (ReadStore(q.Key) == previous) Forget(q.Key);
                }
                else if ((_cache.TryGetValue(q.Key, out var known) ? known : ReadStore(q.Key)) is { } secret)
                {
                    _cache[q.Key] = secret;
                    session.Answered[q.Key] = secret;
                    return secret;
                }
            }
            if (!retry && _declined.TryGetValue(q.Key, out var at) && DateTime.UtcNow - at < DeclineQuietPeriod)
                return q.Kind == AskPassKind.HostKey ? "no" : null;

            var a = await _prompt(new AskPassRequest(q, retry), ct).ConfigureAwait(false);
            if (a is null)
            {
                _declined[q.Key] = DateTime.UtcNow;
                session.Declined[q.Key] = true;
                return q.Kind == AskPassKind.HostKey ? "no" : null;
            }
            _declined.TryRemove(q.Key, out _);
            if (q.IsSecret)
            {
                _cache[q.Key] = a.Value;
                session.Answered[q.Key] = a.Value;
                try
                {
                    if (a.Remember) _store.Write(q.Key, a.Value);
                    else _store.Delete(q.Key);
                }
                catch (Exception ex) { Log.Warning($"AskPass: saving the secret failed: {ex.Message}"); }
            }
            return a.Value;
        }
        finally { gate.Release(); }
    }

    private string? ReadStore(string key)
    {
        try { return _store.Read(key); }
        catch (Exception ex) { Log.Warning($"AskPass: reading the saved secret failed: {ex.Message}"); return null; }
    }

    public void Dispose() => _cts.Cancel();
}

/// <summary>
/// ssh の起動に AskPass を付ける。アプリが <see cref="Broker"/> と <see cref="HelperPath"/> を入れていなければ、今までどおり BatchMode（鍵と ssh-agent だけ）。
/// </summary>
public static class SshAskPass
{
    public static AskPassBroker? Broker { get; set; }

    /// <summary>GitKnife.AskPass.exe の場所。</summary>
    public static string? HelperPath { get; set; }

    /// <summary>
    /// 同期で ssh の終わりを待っている間に呼ぶ（UI のスレッドが待っているときに、そこで質問の画面を出すため。出さないと、画面を出す番を待って止まる）。
    /// </summary>
    public static Action? WhileWaiting { get; set; }

    public static bool Enabled => Broker is not null && HelperPath is not null && File.Exists(HelperPath);

    /// <summary>ssh の -o（接続先の前に置く）。</summary>
    public static IEnumerable<string> Options => Enabled ? [] : ["-o", "BatchMode=yes"];

    /// <summary>ssh の環境変数に AskPass を入れる。</summary>
    public static void Apply(ProcessStartInfo psi, string destination)
    {
        if (!Enabled) return;
        psi.Environment["SSH_ASKPASS"] = HelperPath;
        psi.Environment["SSH_ASKPASS_REQUIRE"] = "force";
        psi.Environment[AskPassProtocol.PipeVariable] = Broker!.PipeName;
        psi.Environment[AskPassProtocol.SessionVariable] = Broker.RegisterSession(destination);
    }

    /// <summary>ssh の起動の引数（-T、-o …、接続先、コマンド）。</summary>
    public static ProcessStartInfo StartInfo(string destination, IEnumerable<string> options, IEnumerable<string> command)
    {
        var psi = new ProcessStartInfo(SshGitHost.SshExe) { WorkingDirectory = Environment.SystemDirectory };
        psi.ArgumentList.Add("-T");
        foreach (var o in Options.Concat(options)) psi.ArgumentList.Add(o);
        psi.ArgumentList.Add(destination);
        foreach (var c in command) psi.ArgumentList.Add(c);
        Apply(psi, destination);
        return psi;
    }

    /// <summary>タスクの終わりを同期で待つ（待つ間 <see cref="WhileWaiting"/> を呼ぶ）。</summary>
    public static T Wait<T>(Task<T> task)
    {
        // task.Wait は失敗したタスクで例外を投げるので、終わったかだけを見る
        while (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(50)) WhileWaiting?.Invoke();
        return task.GetAwaiter().GetResult();
    }
}

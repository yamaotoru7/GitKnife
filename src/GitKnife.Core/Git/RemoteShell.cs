using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace GitKnife.Core.Git;

/// <summary>
/// 常駐のシェル（T1307）。`ssh … sh` を 1 本起動したままにして、コマンドを順に流す。Windows の OpenSSH は ControlMaster が使えないので、
/// git を呼ぶたびに ssh を張る（0.3 秒ほど）代わりにこれを使い回す。
/// 1 回のやり取り：コマンドの stdout / stderr を一時ファイルに受け、終わったら目印の行と base64 で返す
/// （"@@GK-BEGIN id" / stdout の base64 / "@@GK-ERR" / stderr の base64 / "@@GK-RC 終了コード"）。
/// 接続先に sh、base64、mktemp が要る（Linux の coreutils）。進捗を逐次出したい git（fetch など）はこれを使わない。
/// </summary>
public sealed class RemoteShellSession : IDisposable
{
    private readonly Process _process;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private long _next;

    public RemoteShellSession(ProcessStartInfo startInfo)
    {
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.RedirectStandardInput = true;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = new UTF8Encoding(false);
        startInfo.StandardErrorEncoding = new UTF8Encoding(false);
        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("could not start the shell");
        _process.StandardInput.AutoFlush = true;
        _process.StandardInput.NewLine = "\n";
        // ssh 自体の stderr（接続の警告など）は読み捨てる（溜まると止まる）
        _process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d) Logging.Log.Debug($"remote shell: {d}"); };
        _process.BeginErrorReadLine();
    }

    public bool IsAlive => !_process.HasExited;

    /// <summary>シェルの 1 語にする（'…'）。</summary>
    private static string Q(string s) => "'" + s.Replace("'", @"'\''") + "'";

    /// <summary>コマンドを 1 つ動かす。input は stdin に流す内容。キャンセルするとシェルごと終わらせる（この session は使えなくなる）。</summary>
    public async Task<ShellResult> RunAsync(string command, byte[]? input, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!IsAlive) throw new IOException("the remote shell has exited");
            var id = Interlocked.Increment(ref _next).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append("__gk=$(mktemp -d) && { ");
            if (input is { Length: > 0 }) sb.Append("printf '%s' ").Append(Q(Convert.ToBase64String(input))).Append(" | base64 -d > \"$__gk/i\"; ");
            else sb.Append(": > \"$__gk/i\"; ");
            // サブシェルで動かす（cd や exit がこのシェルに残らない）
            sb.Append("( ").Append(command).Append(" ) < \"$__gk/i\" > \"$__gk/o\" 2> \"$__gk/e\"; echo $? > \"$__gk/r\"; ");
            sb.Append("printf '\\n@@GK-BEGIN %s\\n' ").Append(id).Append("; base64 < \"$__gk/o\"; printf '@@GK-ERR\\n'; base64 < \"$__gk/e\"; ");
            sb.Append("printf '@@GK-RC %s\\n' \"$(cat \"$__gk/r\")\"; rm -rf \"$__gk\"; }");
            using var reg = ct.Register(() => { try { _process.Kill(entireProcessTree: true); } catch { } });
            await _process.StandardInput.WriteLineAsync(sb.ToString()).ConfigureAwait(false);

            var begin = "@@GK-BEGIN " + id;
            string? line;
            while ((line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) is not null && line != begin) { }
            if (line is null) throw new IOException("the remote shell has exited");
            var output = new StringBuilder();
            while ((line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) is not null && line != "@@GK-ERR") output.Append(line);
            var error = new StringBuilder();
            while ((line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false)) is not null && !line.StartsWith("@@GK-RC ", StringComparison.Ordinal)) error.Append(line);
            if (line is null) throw new IOException("the remote shell has exited");
            var exit = int.TryParse(line["@@GK-RC ".Length..].Trim(), out var rc) ? rc : -1;
            return new ShellResult(exit, Convert.FromBase64String(output.ToString()), Encoding.UTF8.GetString(Convert.FromBase64String(error.ToString())));
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        try { if (!_process.HasExited) { _process.StandardInput.Close(); if (!_process.WaitForExit(500)) _process.Kill(entireProcessTree: true); } } catch { }
        _process.Dispose();
    }
}

/// <summary>同じ接続先の常駐のシェルを数本まで持ち、空いているものに流す（status と log と for-each-ref を並べて読むため）。</summary>
public sealed class RemoteShellPool(Func<ProcessStartInfo> startInfo, int size = 4) : IDisposable
{
    private readonly ConcurrentBag<RemoteShellSession> _idle = [];
    private readonly SemaphoreSlim _slots = new(size, size);

    public async Task<ShellResult> RunAsync(string command, byte[]? input, CancellationToken ct = default)
    {
        await _slots.WaitAsync(ct).ConfigureAwait(false);
        RemoteShellSession? session = null;
        try
        {
            while (_idle.TryTake(out var s)) { if (s.IsAlive) { session = s; break; } s.Dispose(); }
            session ??= new RemoteShellSession(startInfo());
            var r = await session.RunAsync(command, input, ct).ConfigureAwait(false);
            _idle.Add(session);
            session = null;
            return r;
        }
        finally
        {
            session?.Dispose();   // 失敗・キャンセルしたシェルは捨てる
            _slots.Release();
        }
    }

    public void Dispose()
    {
        while (_idle.TryTake(out var s)) s.Dispose();
    }
}

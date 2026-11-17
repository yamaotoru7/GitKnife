using System.Diagnostics;
using System.Text;
using GitKnife.Core.Logging;

namespace GitKnife.Core.Git;

/// <summary>
/// WSL と SSH のリポジトリの変更の見張り。Windows の FileSystemWatcher は \\wsl.localhost に Linux 側の変更を通知せず、SSH のパスは見張れないので、
/// 接続先の中でシェルのループを 1 本動かし（接続先ごとに 1 本。そこで開いているリポジトリをまとめて見る）、変わったときだけ 1 行を受け取る。
/// <list type="bullet">
/// <item>参照：1 秒ごとに HEAD、packed-refs、refs の下のフォルダ、MERGE_HEAD などを stat し、変わったら "R &lt;番号&gt;"（全体を読み直す）。
/// 参照の書き換えは .lock からの rename なので、ファイルのある フォルダの更新時刻と inode が変わる。</item>
/// <item>index：同じく stat して、変わったら "S &lt;番号&gt;"（status を読み直す）。</item>
/// <item>作業ツリー：接続先に inotifywait があればそれ、無ければ python3 から inotify を直接使って、"S &lt;番号&gt;" を書く（.git の下は見ない）。
/// どちらも無ければ見ない（定期の status とウィンドウの切り替えで拾う）。</item>
/// </list>
/// 止め忘れない仕組み：
/// <list type="bullet">
/// <item>wsl.exe / ssh.exe は <see cref="ChildProcessJob"/> に入れる（GitKnife が落ちても Windows が終わらせる）。</item>
/// <item>接続先のループは、標準入力が閉じたら 1 秒以内に終わる（wsl.exe や ssh が終わったとき）。</item>
/// <item>GitKnife は 10 秒ごとに空の行を送り、ループは 1〜2 分届かなければ終わる（接続が切れたのに sshd が気付かないとき）。</item>
/// <item>ループは 10 秒ごとに "." を書く。書けなければ（読み手がいない）終わる。GitKnife は 35 秒何も届かなければ止まったとみなして起動し直す。</item>
/// <item>終わるときに inotifywait と標準入力の読み手を kill する。</item>
/// </list>
/// 接続先に sh、stat -c（GNU coreutils か busybox）、find、mktemp が要る。無ければ見張らない（<see cref="Registration.IsWatching"/> が false）。
/// </summary>
public static class RemoteRepositoryWatcher
{
    private static readonly Dictionary<string, HostWatch> s_hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// リポジトリを見張る。changed は別のスレッドから呼ばれる（引数は参照の変更を含むか）。WSL と SSH でなければ null。
    /// パスは Windows の形（RepositoryReader の WorkingDirectory、GitDirectory、CommonGitDirectory）。
    /// </summary>
    public static Registration? Watch(GitHost host, string workingDirectory, string gitDirectory, string commonGitDirectory, Action<bool> changed)
    {
        if (Key(host) is not { } key) return null;
        HostWatch hw;
        lock (s_hosts)
        {
            if (!s_hosts.TryGetValue(key, out hw!)) s_hosts[key] = hw = new HostWatch(host);
        }
        return hw.Add(host.ToHostPath(workingDirectory), host.ToHostPath(gitDirectory), host.ToHostPath(commonGitDirectory), changed);
    }

    /// <summary>見張りを全部止める（アプリの終了時）。</summary>
    public static void StopAll()
    {
        lock (s_hosts)
        {
            foreach (var hw in s_hosts.Values) hw.Dispose();
            s_hosts.Clear();
        }
    }

    private static string? Key(GitHost host) => host switch
    {
        WslGitHost w => "wsl:" + w.Distribution,
        SshGitHost s => "ssh:" + s.Destination,
        _ => null,
    };

    /// <summary>1 つのリポジトリの見張り。Dispose で外す（最後の 1 つを外すと接続先のループも止める）。</summary>
    public sealed class Registration : IDisposable
    {
        internal Registration(HostWatch owner, string workingDirectory, string gitDirectory, string commonGitDirectory, Action<bool> changed)
        {
            Owner = owner;
            WorkingDirectory = workingDirectory;
            GitDirectory = gitDirectory;
            CommonGitDirectory = commonGitDirectory;
            Changed = changed;
        }

        internal HostWatch Owner { get; }
        internal string WorkingDirectory { get; }
        internal string GitDirectory { get; }
        internal string CommonGitDirectory { get; }
        internal Action<bool> Changed { get; }

        /// <summary>今このリポジトリの参照の変更が届くか（見張りが起動し終わっていて、止まっていない）。</summary>
        public bool IsWatching => Owner.Covers(this);

        public void Dispose() => Owner.Remove(this);
    }

    /// <summary>接続先ごとの見張り。リポジトリの増減で、新しいループを起動し、それが動き始めてから古いものを止める（入れ替えの間の変更を落とさない）。</summary>
    internal sealed class HostWatch : IDisposable
    {
        private readonly GitHost _host;
        private readonly object _gate = new();
        private readonly List<Registration> _regs = [];
        private readonly Timer _reconfigure;
        private readonly Timer _watchdog;
        private WatchProcess? _current;
        private WatchProcess? _starting;
        private int _failures;
        private bool _notifyOnReady;
        private bool _disposed;

        public HostWatch(GitHost host)
        {
            _host = host;
            _reconfigure = new Timer(_ => Reconfigure());
            _watchdog = new Timer(_ => Watchdog(), null, PingInterval, PingInterval);
        }

        private static readonly TimeSpan PingInterval = TimeSpan.FromSeconds(10);
        private const long SilenceLimitMs = 35_000;

        public Registration Add(string workingDirectory, string gitDirectory, string commonGitDirectory, Action<bool> changed)
        {
            var reg = new Registration(this, workingDirectory, gitDirectory, commonGitDirectory, changed);
            lock (_gate)
            {
                _regs.Add(reg);
                _failures = 0;
                // タブをまとめて開いたとき（起動時の復元）に何度も起動し直さないように少し待つ
                if (!_disposed) _reconfigure.Change(300, Timeout.Infinite);
            }
            return reg;
        }

        public void Remove(Registration reg)
        {
            lock (_gate)
            {
                if (!_regs.Remove(reg) || _disposed) return;
                _reconfigure.Change(300, Timeout.Infinite);
            }
        }

        public bool Covers(Registration reg)
        {
            lock (_gate) return _current is { IsReady: true, HasExited: false } p && p.Regs.Contains(reg);
        }

        private void Reconfigure()
        {
            lock (_gate)
            {
                if (_disposed) return;
                _starting?.Stop();
                _starting = null;
                if (_regs.Count == 0)
                {
                    _current?.Stop();
                    _current = null;
                    return;
                }
                try
                {
                    _starting = WatchProcess.Start(_host, [.. _regs], OnLine, OnExit);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
                {
                    Log.Warning($"Watching {_host.DisplayName} failed to start: {ex.Message}");
                    _failures++;
                    _reconfigure.Change(RetryDelay(), Timeout.Infinite);
                }
            }
        }

        private int RetryDelay() => (int)Math.Min(60_000, 5_000 * Math.Pow(2, Math.Max(0, _failures - 1)));

        private void OnLine(WatchProcess p, string line)
        {
            if (line == "ready")
            {
                Registration[] notify = [];
                lock (_gate)
                {
                    if (p != _starting) return;
                    var old = _current;
                    _current = p;
                    _starting = null;
                    old?.Stop();
                    _failures = 0;
                    // 止まっていた間の変更を拾う
                    if (_notifyOnReady) { _notifyOnReady = false; notify = p.Regs; }
                }
                Log.Debug($"Watching {p.Regs.Length} repositories on {_host.DisplayName}");
                foreach (var r in notify) Invoke(r, true);
                return;
            }
            if (line.StartsWith('!'))
            {
                Log.Warning($"Watching {_host.DisplayName} is not supported: {line[1..]}");
                p.Unsupported = true;
                return;
            }
            if (line.Length < 3 || line[1] != ' ' || line[0] is not ('R' or 'S')) return;
            if (!int.TryParse(line.AsSpan(2), out var i) || i < 1 || i > p.Regs.Length) return;
            Invoke(p.Regs[i - 1], line[0] == 'R');
        }

        private void Invoke(Registration reg, bool full)
        {
            lock (_gate) if (!_regs.Contains(reg)) return;
            try { reg.Changed(full); }
            catch (Exception ex) { Log.Error($"Watching callback failed: {ex}"); }
        }

        private void OnExit(WatchProcess p)
        {
            lock (_gate)
            {
                if (p.Stopped || _disposed) return;
                if (p == _current)
                {
                    Log.Warning($"Watching {_host.DisplayName} stopped; restarting");
                    _current = null;
                    _notifyOnReady = true;
                }
                else if (p == _starting)
                {
                    _starting = null;
                    _failures++;
                    if (!p.Unsupported) Log.Warning($"Watching {_host.DisplayName} could not start (exit {p.ExitCode})");
                }
                else return;
                if (_starting is not null) return;
                // 接続先に要るものが無いときは、タブを開き直すまで（Add で _failures が 0 になるまで）長く待つ
                _reconfigure.Change(p.Unsupported ? 600_000 : _failures == 0 ? 1_000 : RetryDelay(), Timeout.Infinite);
            }
        }

        private void Watchdog()
        {
            WatchProcess?[] procs;
            lock (_gate) procs = [_current, _starting];
            var now = Environment.TickCount64;
            foreach (var p in procs)
            {
                if (p is null) continue;
                if (now - Interlocked.Read(ref p.LastSeen) > SilenceLimitMs)
                {
                    Log.Warning($"Watching {_host.DisplayName} is silent; killing");
                    p.Kill();   // OnExit が起動し直す
                }
                else p.Ping();
            }
        }

        public void Dispose()
        {
            lock (_gate)
            {
                _disposed = true;
                _regs.Clear();
                _starting?.Stop();
                _current?.Stop();
                _starting = _current = null;
            }
            _reconfigure.Dispose();
            _watchdog.Dispose();
        }
    }

    /// <summary>接続先で動かす見張りのループ 1 本（wsl.exe か ssh.exe）。</summary>
    internal sealed class WatchProcess
    {
        private readonly Process _process;
        public Registration[] Regs { get; }
        public long LastSeen;
        public bool IsReady { get; private set; }
        public bool Stopped { get; private set; }
        public bool Unsupported { get; set; }
        public bool HasExited { get { try { return _process.HasExited; } catch (InvalidOperationException) { return true; } } }
        public int ExitCode { get { try { return _process.ExitCode; } catch (InvalidOperationException) { return -1; } } }

        private WatchProcess(Process process, Registration[] regs)
        {
            _process = process;
            Regs = regs;
            LastSeen = Environment.TickCount64;
        }

        public static WatchProcess Start(GitHost host, Registration[] regs, Action<WatchProcess, string> onLine, Action<WatchProcess> onExit)
        {
            var args = regs.SelectMany(r => new[] { r.WorkingDirectory, r.GitDirectory, r.CommonGitDirectory }).ToList();
            ProcessStartInfo psi;
            switch (host)
            {
                case WslGitHost w:
                    psi = new ProcessStartInfo(WslGitHost.WslExe) { WorkingDirectory = Environment.SystemDirectory };
                    foreach (var a in new[] { "-d", w.Distribution, "--exec", "sh", "-s", "--" }.Concat(args)) psi.ArgumentList.Add(a);
                    break;
                case SshGitHost s:
                    // パスワードを聞く画面は出さない（鍵と ssh-agent だけ。入れなければ見張らずに、ウィンドウの切り替えで全体を読み直す）
                    psi = SshAskPass.StartInfo(s.Destination,
                        ["-o", "BatchMode=yes", "-o", "ServerAliveInterval=15", "-o", "ServerAliveCountMax=3"],
                        ["sh -s --" + string.Concat(args.Select(a => " " + SshGitHost.Quote(a)))]);
                    break;
                default:
                    throw new InvalidOperationException("not a remote host");
            }
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardInput = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardInputEncoding = new UTF8Encoding(false);
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            var process = Process.Start(psi) ?? throw new InvalidOperationException("could not start the watcher");
            ChildProcessJob.Add(process);
            var wp = new WatchProcess(process, regs);
            process.ErrorDataReceived += (_, e) => { if (e.Data is { Length: > 0 } d) Log.Debug($"watch {host.DisplayName}: {d}"); };
            process.BeginErrorReadLine();
            process.StandardInput.NewLine = "\n";
            process.StandardInput.Write(Script);
            process.StandardInput.Flush();
            _ = Task.Run(async () =>
            {
                try
                {
                    string? line;
                    while ((line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false)) is not null)
                    {
                        Interlocked.Exchange(ref wp.LastSeen, Environment.TickCount64);
                        if (line == "ready") wp.IsReady = true;
                        onLine(wp, line);
                    }
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (Exception ex) { Log.Debug($"watch {host.DisplayName}: {ex.Message}"); }
                onExit(wp);
                process.Dispose();
            });
            return wp;
        }

        /// <summary>生きていることを知らせる（接続先のループは 1〜2 分届かなければ終わる）。</summary>
        public void Ping()
        {
            try { _process.StandardInput.Write('\n'); _process.StandardInput.Flush(); }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
        }

        /// <summary>止める（起動し直さない）。標準入力を閉じると接続先のループが終わる。</summary>
        public void Stop()
        {
            Stopped = true;
            try { _process.StandardInput.Close(); } catch { }
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(2000);
                    await _process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { Kill(); }
                catch (InvalidOperationException) { }
            });
        }

        public void Kill()
        {
            try { _process.Kill(entireProcessTree: true); } catch { }
        }

        /// <summary>
        /// 接続先の sh -s に流すスクリプト。引数はリポジトリごとに 作業ツリー、git のフォルダ、共通の git のフォルダ。
        /// 全体を関数にしてから呼ぶ（sh は読んだところから実行するので、読み残しがあると標準入力の読み手がスクリプトの続きを読んでしまう）。
        /// </summary>
        private static readonly string Script = """
            gk_main() {
            command -v stat >/dev/null 2>&1 && stat -c %i / >/dev/null 2>&1 || { echo '!stat'; exit 1; }
            command -v find >/dev/null 2>&1 || { echo '!find'; exit 1; }
            gk_p=$(mktemp 2>/dev/null) || { echo '!mktemp'; exit 1; }
            gk_k=
            gk_py=$(cat <<'GKPY'
            import ctypes, os, struct, sys
            libc = ctypes.CDLL(None, use_errno=True)
            fd = libc.inotify_init1(os.O_CLOEXEC)
            if fd < 0:
                sys.exit(1)
            MASK = 0x8 | 0x40 | 0x80 | 0x100 | 0x200 | 0x01000000
            wds = {}
            def add(path, i):
                for root, dirs, _ in os.walk(path):
                    dirs[:] = [d for d in dirs if d != '.git']
                    w = libc.inotify_add_watch(fd, os.fsencode(root), MASK)
                    if w >= 0:
                        wds[w] = (root, i)
            args = sys.argv[1:]
            repos = list(zip(args[0::2], args[1::2]))
            for i, p in repos:
                add(p, i)
            while True:
                data = os.read(fd, 65536)
                hit = set()
                off = 0
                while off < len(data):
                    w, m, _, n = struct.unpack_from('iIII', data, off)
                    name = data[off + 16:off + 16 + n].rstrip(b'\0')
                    off += 16 + n
                    if m & 0x4000:
                        hit.update(i for i, _ in repos)
                        continue
                    e = wds.get(w)
                    if e is None or name == b'.git':
                        continue
                    if m & 0x40000000 and m & (0x100 | 0x80):
                        add(os.path.join(e[0], os.fsdecode(name)), e[1])
                    hit.add(e[1])
                for i in hit:
                    sys.stdout.write('S %s\n' % i)
                sys.stdout.flush()
            GKPY
            )
            trap 'exit 0' HUP INT TERM PIPE
            trap 'rm -f "$gk_p"; [ -n "$gk_k" ] && kill $gk_k 2>/dev/null' EXIT
            exec 3<&0 </dev/null
            ( while read -r _ <&3; do : > "$gk_p"; done; rm -f "$gk_p" ) &
            gk_k=$!
            exec 3<&-
            gk_n=0
            while [ $# -ge 3 ]; do
              gk_n=$((gk_n+1))
              eval "gk_w$gk_n=\$1 gk_g$gk_n=\$2 gk_c$gk_n=\$3"
              shift 3
            done
            if command -v inotifywait >/dev/null 2>&1; then
              gk_i=1
              while [ $gk_i -le $gk_n ]; do
                eval "gk_x=\$gk_w$gk_i"
                inotifywait -m -r -q -e close_write,create,delete,move --exclude '(^|/)\.git(/|$)' --format "S $gk_i" "$gk_x" 2>/dev/null </dev/null &
                gk_k="$gk_k $!"
                gk_i=$((gk_i+1))
              done
            elif command -v python3 >/dev/null 2>&1; then
              set --
              gk_i=1
              while [ $gk_i -le $gk_n ]; do
                eval "gk_x=\$gk_w$gk_i"
                set -- "$@" "$gk_i" "$gk_x"
                gk_i=$((gk_i+1))
              done
              python3 -c "$gk_py" "$@" 2>/dev/null </dev/null &
              gk_k="$gk_k $!"
            fi
            gk_i=1
            while [ $gk_i -le $gk_n ]; do
              eval "gk_g=\$gk_g$gk_i gk_c=\$gk_c$gk_i"
              gk_v=$(gk_refs "$gk_g" "$gk_c"); eval "gk_r$gk_i=\$gk_v"
              gk_v=$(gk_index "$gk_g"); eval "gk_x$gk_i=\$gk_v"
              gk_i=$((gk_i+1))
            done
            echo ready
            gk_t=0
            while :; do
              sleep 1
              [ -f "$gk_p" ] || exit 0
              gk_i=1
              while [ $gk_i -le $gk_n ]; do
                eval "gk_g=\$gk_g$gk_i gk_c=\$gk_c$gk_i gk_or=\$gk_r$gk_i gk_ox=\$gk_x$gk_i"
                gk_v=$(gk_refs "$gk_g" "$gk_c")
                gk_y=$(gk_index "$gk_g")
                if [ "$gk_v" != "$gk_or" ]; then
                  eval "gk_r$gk_i=\$gk_v gk_x$gk_i=\$gk_y"
                  echo "R $gk_i"
                elif [ "$gk_y" != "$gk_ox" ]; then
                  eval "gk_x$gk_i=\$gk_y"
                  echo "S $gk_i"
                fi
                gk_i=$((gk_i+1))
              done
              gk_t=$((gk_t+1))
              if [ $gk_t -ge 10 ]; then
                gk_t=0
                echo .
                [ -z "$(find "$gk_p" -mmin +1 2>/dev/null)" ] || exit 0
              fi
            done
            }
            gk_refs() {
              IFS='
            '
              set -- $(find "$2/refs" "$2/reftable" -type d 2>/dev/null) "$1/HEAD" "$2/packed-refs" "$2/reftable/tables.list" "$1/MERGE_HEAD" "$1/REBASE_HEAD" "$1/CHERRY_PICK_HEAD" "$1/REVERT_HEAD" "$1/rebase-merge" "$1/rebase-apply" "$1/BISECT_LOG"
              unset IFS
              stat -c '%n %i %s %y' "$@" 2>/dev/null
            }
            gk_index() {
              stat -c '%i %s %y' "$1/index" 2>/dev/null
            }
            gk_main "$@"

            """.Replace("\r\n", "\n");
    }
}

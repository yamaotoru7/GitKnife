using System.Diagnostics;
using System.Text;

namespace GitKnife.Core.Git;

/// <summary>
/// リポジトリの中のファイルの読み書き。System.IO.File と同じ形で、パスが SSH のリポジトリ（\\gitknife-ssh\…）なら
/// 接続先のシェル（test、cat、mv、rm、find）で、それ以外（ローカル、WSL の UNC）はそのまま System.IO で行う。
/// リポジトリのパスを受けるコードは File の代わりにこちらを使う。
/// 推定：1 回の操作で ssh を 1 回張る（1 回 0.3 秒ほど）。多い所は git で取るか、まとめて読むようにする。
/// </summary>
public static class HostFile
{
    private static SshGitHost? Ssh(string path) => SshPath.IsSshPath(path) ? GitHost.FromPath(path) as SshGitHost : null;

    public static bool Exists(string path) => Ssh(path) is { } h ? h.Shell($"test -f {h.Q(path)}").ExitCode == 0 : File.Exists(path);

    public static byte[] ReadAllBytes(string path)
    {
        if (Ssh(path) is not { } h) return File.ReadAllBytes(path);
        var r = h.Shell($"cat {h.Q(path)}");
        return r.ExitCode == 0 ? r.Output : throw new FileNotFoundException(r.ErrorText, path);
    }

    public static string ReadAllText(string path) => Ssh(path) is null ? File.ReadAllText(path) : Decode(ReadAllBytes(path));
    public static string ReadAllText(string path, Encoding encoding) => Ssh(path) is null ? File.ReadAllText(path, encoding) : Decode(ReadAllBytes(path));
    public static string[] ReadAllLines(string path) => Ssh(path) is null ? File.ReadAllLines(path) : ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
    public static IEnumerable<string> ReadLines(string path) => Ssh(path) is null ? File.ReadLines(path) : ReadAllLines(path);

    public static Task<string> ReadAllTextAsync(string path, CancellationToken ct = default) =>
        Ssh(path) is null ? File.ReadAllTextAsync(path, ct) : Task.Run(() => ReadAllText(path), ct);
    public static Task<byte[]> ReadAllBytesAsync(string path, CancellationToken ct = default) =>
        Ssh(path) is null ? File.ReadAllBytesAsync(path, ct) : Task.Run(() => ReadAllBytes(path), ct);

    public static void WriteAllBytes(string path, byte[] bytes)
    {
        if (Ssh(path) is not { } h) { File.WriteAllBytes(path, bytes); return; }
        h.Shell($"cat > {h.Q(path)}", bytes).ThrowIfFailed(path);
    }

    public static void WriteAllText(string path, string? text) => WriteAllText(path, text, new UTF8Encoding(false));
    public static void WriteAllText(string path, string? text, Encoding encoding)
    {
        if (Ssh(path) is null) File.WriteAllText(path, text, encoding);
        else WriteAllBytes(path, encoding.GetBytes(text ?? ""));
    }

    public static Task WriteAllTextAsync(string path, string? text, CancellationToken ct = default) => WriteAllTextAsync(path, text, new UTF8Encoding(false), ct);
    public static Task WriteAllTextAsync(string path, string? text, Encoding encoding, CancellationToken ct = default) =>
        Ssh(path) is null ? File.WriteAllTextAsync(path, text, encoding, ct) : Task.Run(() => WriteAllText(path, text, encoding), ct);
    public static Task WriteAllBytesAsync(string path, byte[] bytes, CancellationToken ct = default) =>
        Ssh(path) is null ? File.WriteAllBytesAsync(path, bytes, ct) : Task.Run(() => WriteAllBytes(path, bytes), ct);

    public static void AppendAllText(string path, string? text)
    {
        if (Ssh(path) is not { } h) { File.AppendAllText(path, text); return; }
        h.Shell($"cat >> {h.Q(path)}", Encoding.UTF8.GetBytes(text ?? "")).ThrowIfFailed(path);
    }
    public static Task AppendAllTextAsync(string path, string? text, CancellationToken ct = default) =>
        Ssh(path) is null ? File.AppendAllTextAsync(path, text, ct) : Task.Run(() => AppendAllText(path, text), ct);

    public static void Delete(string path)
    {
        if (Ssh(path) is not { } h) { File.Delete(path); return; }
        h.Shell($"rm -f {h.Q(path)}").ThrowIfFailed(path);
    }

    public static void Move(string source, string destination, bool overwrite = false)
    {
        if (Ssh(source) is not { } h) { File.Move(source, destination, overwrite); return; }
        h.Shell($"mv {(overwrite ? "-f" : "-n")} {h.Q(source)} {h.Q(destination)}").ThrowIfFailed(source);
    }

    public static void Copy(string source, string destination, bool overwrite = false)
    {
        var s = Ssh(source);
        var d = Ssh(destination);
        if (s is null && d is null) { File.Copy(source, destination, overwrite); return; }
        if (s is not null && d is not null && s == d) { s.Shell($"cp {(overwrite ? "-f" : "-n")} {s.Q(source)} {s.Q(destination)}").ThrowIfFailed(source); return; }
        if (!overwrite && Exists(destination)) throw new IOException($"'{destination}' already exists.");
        WriteAllBytes(destination, ReadAllBytes(source));   // ローカルと接続先の間は読んで書く
    }

    public static DateTime GetLastWriteTimeUtc(string path)
    {
        if (Ssh(path) is not { } h) return File.GetLastWriteTimeUtc(path);
        var r = h.Shell($"stat -c %Y {h.Q(path)}");
        return long.TryParse(r.Text.Trim(), out var s) ? DateTimeOffset.FromUnixTimeSeconds(s).UtcDateTime : DateTime.MinValue;
    }

    private static string Decode(byte[] bytes)
    {
        // File.ReadAllText と同じく BOM を見て、無ければ UTF-8
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}

/// <summary>System.IO.Directory と同じ形の、接続先を通すフォルダの操作（<see cref="HostFile"/> と同じ）。</summary>
public static class HostDirectory
{
    private static SshGitHost? Ssh(string path) => SshPath.IsSshPath(path) ? GitHost.FromPath(path) as SshGitHost : null;

    public static bool Exists(string path) => Ssh(path) is { } h ? h.Shell($"test -d {h.Q(path)}").ExitCode == 0 : Directory.Exists(path);

    public static void CreateDirectory(string path)
    {
        if (Ssh(path) is not { } h) { Directory.CreateDirectory(path); return; }
        h.Shell($"mkdir -p {h.Q(path)}").ThrowIfFailed(path);
    }

    public static void Delete(string path, bool recursive = false)
    {
        if (Ssh(path) is not { } h) { Directory.Delete(path, recursive); return; }
        h.Shell(recursive ? $"rm -rf {h.Q(path)}" : $"rmdir {h.Q(path)}").ThrowIfFailed(path);
    }

    /// <summary>直下のファイルとフォルダの名前（無ければ空）。いくつもの目印をまとめて確かめるとき用。</summary>
    public static IReadOnlySet<string> EntryNames(string path)
    {
        if (Ssh(path) is { } h)
        {
            var r = h.Shell($"ls -A {h.Q(path)}");
            return r.ExitCode == 0 ? r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal) : new HashSet<string>();
        }
        try { return Directory.EnumerateFileSystemEntries(path).Select(p => Path.GetFileName(p)).ToHashSet(StringComparer.OrdinalIgnoreCase); }
        catch (IOException) { return new HashSet<string>(); }
        catch (UnauthorizedAccessException) { return new HashSet<string>(); }
    }

    public static IEnumerable<string> EnumerateFiles(string path, string pattern = "*") => Enumerate(path, pattern, "f") ?? Directory.EnumerateFiles(path, pattern);
    public static IEnumerable<string> EnumerateDirectories(string path, string pattern = "*") => Enumerate(path, pattern, "d") ?? Directory.EnumerateDirectories(path, pattern);

    /// <summary>SSH なら find で 1 階層だけ（ローカルなら null）。</summary>
    private static IEnumerable<string>? Enumerate(string path, string pattern, string type)
    {
        if (Ssh(path) is not { } h) return null;
        var r = h.Shell($"find {h.Q(path)} -mindepth 1 -maxdepth 1 -type {type} -name {SshGitHost.Quote(pattern)}");
        if (r.ExitCode != 0) throw new DirectoryNotFoundException(r.ErrorText);
        return r.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(h.ToLocalPath).ToList();
    }
}

/// <summary>接続先のシェルで 1 つのコマンドを動かした結果。</summary>
public sealed record ShellResult(int ExitCode, byte[] Output, string ErrorText)
{
    public string Text => Encoding.UTF8.GetString(Output);

    public void ThrowIfFailed(string path)
    {
        if (ExitCode != 0) throw new IOException($"{path}: {ErrorText.Trim()}");
    }
}

public sealed partial record SshGitHost
{
    /// <summary>Windows の形のパスを接続先の形にしてシェルの 1 語にする。</summary>
    public string Q(string localPath) => Quote(ToHostPath(localPath));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, RemoteShellPool> s_pools = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>この接続先の常駐のシェル（`ssh -T … sh`。接続先ごとに最大 4 本。T1307）。</summary>
    public RemoteShellPool Pool => s_pools.GetOrAdd(Destination, _ => new RemoteShellPool(() => SshAskPass.StartInfo(Destination, ["-o", "ServerAliveInterval=30"], ["sh"])));

    /// <summary>常駐のシェルを全部閉じる（アプリの終了時）。</summary>
    public static void CloseSessions()
    {
        foreach (var pool in s_pools.Values) pool.Dispose();
        s_pools.Clear();
    }

    /// <summary>接続先のシェルでコマンドを動かす（同期。ファイルの読み書き用）。stdin に input を流す。常駐のシェルが使えなければ 1 回ずつ ssh を張る。</summary>
    public ShellResult Shell(string command, byte[]? input = null)
    {
        Logging.Log.Debug($"ssh {Destination} {command}");
        // 待つ間に AskPass の質問を出せるようにする（UI のスレッドから呼ばれることがある）
        try { return SshAskPass.Wait(Task.Run(() => Pool.RunAsync(command, input))); }
        catch (IOException ex) { Logging.Log.Warning($"remote shell failed, falling back to one ssh: {ex.Message}"); }
        return ShellOnce(command, input);
    }

    /// <summary>ssh を 1 回張ってコマンドを動かす。</summary>
    public ShellResult ShellOnce(string command, byte[]? input = null) => SshAskPass.Wait(Task.Run(() => RunOnce(command, input)));

    private ShellResult RunOnce(string command, byte[]? input)
    {
        var psi = SshAskPass.StartInfo(Destination, [], [command]);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.StandardErrorEncoding = new UTF8Encoding(false);
        using var p = Process.Start(psi)!;
        var stdin = Task.Run(() =>
        {
            try { if (input is not null) p.StandardInput.BaseStream.Write(input); }
            finally { try { p.StandardInput.Close(); } catch { } }
        });
        var err = p.StandardError.ReadToEndAsync();
        using var ms = new MemoryStream();
        p.StandardOutput.BaseStream.CopyTo(ms);
        p.WaitForExit();
        stdin.Wait();
        return new ShellResult(p.ExitCode, ms.ToArray(), err.Result);
    }
}

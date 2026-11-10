using System.Text;

namespace GitKnife.Core.Logging;

public enum LogLevel { Debug, Info, Warning, Error }

/// <summary>
/// アプリのログ。1 行形式：`<印> <yyyy-MM-dd HH:mm:ss.fff> <message>`（🔷 info、◽️ debug、⚠️ warning、❌ error）。
/// 既定の出力先は %LocalAppData%\GitKnife\logs\gitknife.log。テストはシンクを差し替えられる。
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static Action<string>? _sink;
    public static LogLevel MinimumLevel { get; set; } = LogLevel.Debug;

    public static string DefaultPath => Path.Combine(ProductInfo.AppDataDirectory, "logs", "gitknife.log");

    /// <summary>ログファイルの上限。超えたら 1 つ前の世代（gitknife.1.log）に移して新しく書き始める。</summary>
    public const long MaxFileBytes = 10 * 1024 * 1024;
    /// <summary>何行書くごとに大きさを確かめるか。</summary>
    private const int RotateCheckInterval = 1000;

    /// <summary>ファイルに書く既定のシンクを有効にする。起動時と、書いた行数が一定を超えるたびに大きさを見て切り替える。</summary>
    public static void UseFile(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        RotateIfLarger(path, MaxFileBytes);
        var written = 0;
        _sink = line =>
        {
            lock (Gate)
            {
                if (++written % RotateCheckInterval == 0) RotateIfLarger(path, MaxFileBytes);
                File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false));
            }
        };
    }

    /// <summary>1 つ前の世代のパス（gitknife.log → gitknife.1.log）。</summary>
    public static string PreviousPath(string path) =>
        Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + ".1" + Path.GetExtension(path));

    /// <summary>path が maxBytes を超えていたら、1 つ前の世代に移す（前の世代は消す）。移したら true。</summary>
    public static bool RotateIfLarger(string path, long maxBytes)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= maxBytes) return false;
            File.Move(path, PreviousPath(path), overwrite: true);
            return true;
        }
        catch (IOException) { return false; }   // 別のプロセスが開いているなどで移せなければ、そのまま書き足す
        catch (UnauthorizedAccessException) { return false; }
    }

    public static void UseSink(Action<string>? sink) => _sink = sink;

    public static void Debug(string message) => Write(LogLevel.Debug, message);
    public static void Info(string message) => Write(LogLevel.Info, message);
    public static void Warning(string message) => Write(LogLevel.Warning, message);
    public static void Error(string message) => Write(LogLevel.Error, message);

    public static string Format(LogLevel level, string message, DateTime time)
    {
        var mark = level switch { LogLevel.Debug => "◽️", LogLevel.Info => "🔷", LogLevel.Warning => "⚠️", _ => "❌" };
        return $"{mark} {time:yyyy-MM-dd HH:mm:ss.fff} {message}";
    }

    private static void Write(LogLevel level, string message)
    {
        if (level < MinimumLevel) return;
        _sink?.Invoke(Format(level, message, DateTime.Now));
    }
}

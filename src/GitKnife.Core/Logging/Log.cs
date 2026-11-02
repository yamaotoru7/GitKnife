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

    /// <summary>ファイルに書く既定のシンクを有効にする。</summary>
    public static void UseFile(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _sink = line => { lock (Gate) File.AppendAllText(path, line + Environment.NewLine, new UTF8Encoding(false)); };
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

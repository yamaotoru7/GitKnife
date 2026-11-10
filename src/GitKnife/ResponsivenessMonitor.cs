using System.Diagnostics;
using System.Windows.Threading;
using GitKnife.Core.Logging;

namespace GitKnife;

/// <summary>
/// 画面のスレッドが 0.5 秒以上止まったら、止まっていた長さをログに書く
/// （別スレッドから定期的に画面のスレッドへ空の処理を送り、戻るまでの時間を測る）。
/// 止まり始めた時刻を書くので、前後の git の行と見比べれば何をしていたか分かる。
/// </summary>
public static class ResponsivenessMonitor
{
    private const int StallThresholdMs = 500;
    private const int ProbeIntervalMs = 250;

    public static void Start(Dispatcher dispatcher)
    {
        var thread = new Thread(() => Watch(dispatcher)) { IsBackground = true, Name = "ResponsivenessMonitor", Priority = ThreadPriority.AboveNormal };
        thread.Start();
    }

    private static void Watch(Dispatcher dispatcher)
    {
        using var done = new AutoResetEvent(false);
        while (!dispatcher.HasShutdownStarted)
        {
            Thread.Sleep(ProbeIntervalMs);
            var start = Stopwatch.GetTimestamp();
            var startTime = DateTime.Now;
            try { dispatcher.BeginInvoke(DispatcherPriority.Input, () => done.Set()); }
            catch { return; }
            if (done.WaitOne(StallThresholdMs)) continue;
            // 止まっている：戻るまで待って長さを書く
            while (!done.WaitOne(1000)) if (dispatcher.HasShutdownStarted) return;
            var ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            Log.Warning($"UI thread stalled {ms:0} ms (from {startTime:HH:mm:ss.fff})");
        }
    }
}

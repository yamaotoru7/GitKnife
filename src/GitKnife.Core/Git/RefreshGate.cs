namespace GitKnife.Core.Git;

/// <summary>
/// ファイルの変更通知を受けたとき、読み直しを省けるかの判定（時刻は Environment.TickCount64）。
/// 読み直しを始めた後に届いた参照の変更は、読み終えた内容に入っていないことがあるので省かない（Issue #12）。
/// </summary>
public static class RefreshGate
{
    /// <summary>読み直しと通知の時刻の差がこれ以内なら、どちらが先か分からないので読み直す。</summary>
    public const long EventTimeMarginMs = 50;

    /// <summary>status の読み直しが続かないように空ける間隔。</summary>
    public const long StatusIntervalMs = 1500;

    /// <param name="full">参照の変更を含む（全体を読み直す）通知か。</param>
    /// <param name="lastRefresh">最後の読み直し（status だけのものも含む）を始めた時刻。</param>
    /// <param name="lastFullRefresh">最後の全体の読み直しを始めた時刻。</param>
    public static bool CanSkip(bool full, long now, long lastEvent, long lastRefresh, long lastFullRefresh)
    {
        if (full) return lastFullRefresh > lastEvent + EventTimeMarginMs;
        return lastRefresh > lastEvent + EventTimeMarginMs || now - lastRefresh < StatusIntervalMs;
    }
}

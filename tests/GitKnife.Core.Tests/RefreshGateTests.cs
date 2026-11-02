using GitKnife.Core.Git;

namespace GitKnife.Core.Tests;

public class RefreshGateTests
{
    /// <summary>読み直しの 0.7 秒後に届いた参照の変更を捨てない（Issue #12。1.5 秒以内の通知を捨てていた）。</summary>
    [Fact]
    public void ReferenceChangeAfterRefreshStartIsRead() =>
        Assert.False(RefreshGate.CanSkip(full: true, now: 11_100, lastEvent: 10_700, lastRefresh: 10_000, lastFullRefresh: 10_000));

    [Fact]
    public void ReferenceChangeBeforeRefreshStartIsSkipped() =>
        Assert.True(RefreshGate.CanSkip(full: true, now: 10_500, lastEvent: 10_000, lastRefresh: 10_100, lastFullRefresh: 10_100));

    [Fact]
    public void CloseTimesAreRead() =>
        Assert.False(RefreshGate.CanSkip(full: true, now: 10_500, lastEvent: 10_000, lastRefresh: 10_050, lastFullRefresh: 10_050));

    /// <summary>窓に戻ったときの status の読み直しでは、参照の変更の分を省かない。</summary>
    [Fact]
    public void StatusRefreshDoesNotCoverReferenceChange() =>
        Assert.False(RefreshGate.CanSkip(full: true, now: 10_500, lastEvent: 10_000, lastRefresh: 10_200, lastFullRefresh: 5_000));

    [Theory]
    [InlineData(10_500, 10_400, 8_900, false)]  // 間が空いている
    [InlineData(10_500, 10_400, 9_600, true)]   // 読み直したばかり
    [InlineData(20_000, 10_000, 10_100, true)]  // 通知より後に読んでいる
    public void StatusChangeKeepsTheInterval(long now, long lastEvent, long lastRefresh, bool skip) =>
        Assert.Equal(skip, RefreshGate.CanSkip(full: false, now, lastEvent, lastRefresh, lastFullRefresh: 0));
}

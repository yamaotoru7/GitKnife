using GitKnife.Core.Git;
using Xunit;

namespace GitKnife.Core.Tests;

public class HostIOTests
{
    [Fact]
    public async Task RunAsync_PassesTheTokenToTheWork()
    {
        using var cts = new CancellationTokenSource();
        var seen = await HostIO.RunAsync(() => HostIO.CancellationToken, cts.Token);
        Assert.Equal(cts.Token, seen);
        // 外には残らない
        Assert.Equal(CancellationToken.None, HostIO.CancellationToken);
    }

    [Fact]
    public async Task RunAsync_RunsOffTheCallingThread()
    {
        var caller = Environment.CurrentManagedThreadId;
        var worker = await HostIO.RunAsync(() => Environment.CurrentManagedThreadId);
        Assert.NotEqual(caller, worker);
    }

    [Fact]
    public async Task RunAsync_CanceledBeforeStart_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var ran = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => HostIO.RunAsync(() => ran = true, cts.Token));
        Assert.False(ran);
    }

    [Fact]
    public void ExistsMany_LocalFolders()
    {
        var dir = Directory.CreateTempSubdirectory("gk-exists-").FullName;
        try
        {
            var missing = Path.Combine(dir, "missing");
            var result = HostDirectory.ExistsMany([dir, missing, dir]);
            Assert.Equal(2, result.Count);
            Assert.True(result[dir]);
            Assert.False(result[missing]);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

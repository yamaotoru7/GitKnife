using GitKnife.Core.Logging;

namespace GitKnife.Core.Tests;

public class LogRotationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gk-logrot-" + Guid.NewGuid().ToString("N"));
    private string LogPath => Path.Combine(_dir, "gitknife.log");

    public LogRotationTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void PreviousPathAddsGenerationBeforeExtension()
    {
        Assert.Equal(Path.Combine(_dir, "gitknife.1.log"), Log.PreviousPath(LogPath));
    }

    [Fact]
    public void SmallFileIsKept()
    {
        File.WriteAllText(LogPath, "short");
        Assert.False(Log.RotateIfLarger(LogPath, 100));
        Assert.True(File.Exists(LogPath));
        Assert.False(File.Exists(Log.PreviousPath(LogPath)));
    }

    [Fact]
    public void LargeFileMovesToPreviousAndReplacesOldPrevious()
    {
        File.WriteAllText(Log.PreviousPath(LogPath), "older");
        File.WriteAllText(LogPath, new string('x', 200));
        Assert.True(Log.RotateIfLarger(LogPath, 100));
        Assert.False(File.Exists(LogPath));
        Assert.Equal(new string('x', 200), File.ReadAllText(Log.PreviousPath(LogPath)));
    }

    [Fact]
    public void MissingFileIsNotAnError()
    {
        Assert.False(Log.RotateIfLarger(LogPath, 100));
    }
}

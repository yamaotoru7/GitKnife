using GitKnife.Core.Git;

namespace GitKnife.Core.Tests;

/// <summary>Repository Manager の要約（Commits 12、リモートブランチ 3、タグ 2）。</summary>
[Collection("sandbox")]
public class RepositorySummaryReaderTests(SandboxFixture sandbox)
{
    [Fact]
    public async Task ReadsExpectedNumbers()
    {
        var s = await new RepositorySummaryReader(sandbox.Git, sandbox.Basic).ReadAsync();
        Assert.Equal(2, s.UncommittedFiles);
        Assert.Equal(12, s.Commits);
        Assert.Equal(["origin"], s.Remotes);
        Assert.Equal(["origin/HEAD", "origin/main", "origin/bugfix/greeting"], s.Branches.Select(b => b.Name));
        Assert.Equal(["v0.2", "v0.1"], s.Tags.Select(t => t.Name));
        // stash の index コミットの件名には SHA が入る（サンドボックスの作り方で変わるので前後だけ見る）
        Assert.Equal(["On main: WIP: greeting experiment", "index on main:", "Document options", "Add changelog", "Capitalize greeting"],
            s.RecentCommits.Select(c => c.Subject.StartsWith("index on main:") ? "index on main:" : c.Subject));
        Assert.Equal("README.md", s.ReadmeName);
    }

    [Fact]
    public void FormatsRelativeTime()
    {
        var now = new DateTimeOffset(2026, 10, 25, 9, 0, 0, TimeSpan.FromHours(9));
        Assert.Equal("9 months ago", RepositorySummaryReader.Relative(new DateTimeOffset(2026, 1, 5, 16, 33, 0, TimeSpan.FromHours(9)), now));
        Assert.Equal("1 day ago", RepositorySummaryReader.Relative(now.AddDays(-1), now));
    }

    [Fact]
    public void ParsesRemoteUrls()
    {
        var r = RepositorySummaryReader.ParseRemotes("origin\tD:/x/remote (fetch)\norigin\tD:/x/remote-push (push)\nup\tgit@github.com:o/r.git (fetch)\n");
        Assert.Equal(["origin", "up"], r.Keys);
        Assert.Equal("D:/x/remote", r["origin"]);
    }

    [Fact]
    public void BuildsIssueAndPullRequestLinks()
    {
        Assert.Equal(("https://github.com/o/r/issues", "https://github.com/o/r/pulls"), RemoteWebLinks.For("git@github.com:o/r.git"));
        Assert.Equal(("https://github.com/o/r/issues", "https://github.com/o/r/pulls"), RemoteWebLinks.For("https://user@github.com/o/r.git"));
        Assert.Equal(("https://gitlab.com/g/s/p/-/issues", "https://gitlab.com/g/s/p/-/merge_requests"), RemoteWebLinks.For("ssh://git@gitlab.com/g/s/p.git"));
        Assert.Equal(("https://bitbucket.org/o/r/issues", "https://bitbucket.org/o/r/pull-requests"), RemoteWebLinks.For("git@bitbucket.org:o/r.git"));
        Assert.Equal(("https://dev.azure.com/org/proj/_workitems", "https://dev.azure.com/org/proj/_git/repo/pullrequests"), RemoteWebLinks.For("git@ssh.dev.azure.com:v3/org/proj/repo"));
        Assert.Equal((null, null), RemoteWebLinks.For("D:/Proj/tools/GitKnife/.work/sandbox/remote"));
        Assert.Equal((null, null), RemoteWebLinks.For(@"\\server\share\repo.git"));
    }

    [Fact]
    public void ConvertsBetweenHttpsAndSsh()
    {
        // ▾ の 2 つ
        Assert.Equal(("https://github.com/owner/repo.git", "git@github.com:owner/repo.git"), RemoteWebLinks.ProtocolForms("https://github.com/owner/repo.git"));
        Assert.Equal(("https://github.com/owner/repo.git", "git@github.com:owner/repo.git"), RemoteWebLinks.ProtocolForms("git@github.com:owner/repo"));
        Assert.Null(RemoteWebLinks.ProtocolForms("D:/Proj/tools/GitKnife/.work/sandbox/remote"));
        Assert.Equal("HTTPS", RemoteWebLinks.ProtocolName("D:/x"));
        Assert.Equal("HTTPS", RemoteWebLinks.ProtocolName("https://github.com/owner/repo.git"));
        Assert.Equal("SSH", RemoteWebLinks.ProtocolName("git@github.com:owner/repo.git"));
    }
}

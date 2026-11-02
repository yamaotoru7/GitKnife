using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Tests;

/// <summary>コミット一覧の Ctrl+F。</summary>
public class CommitListFindTests
{
    private static CommitInfo C(string sha, string subject, string author = "Sandbox User", string email = "sandbox@example.com", string body = "")
        => new(sha, [], author, email, DateTimeOffset.UnixEpoch, author, email, DateTimeOffset.UnixEpoch, subject, body);

    // sandbox の一覧（上から。stash を含む 11 行）とラベル
    private static readonly (CommitInfo Commit, string[] Refs)[] Rows =
    [
        (C("b467ce1aaaa", "On main: WIP: greeting experiment"), ["stash@{0}"]),
        (C("3b121ca", "Document options"), ["main"]),
        (C("7cabd6e", "Add changelog", "Other Dev", "other@example.com"), ["origin/main"]),
        (C("9401224", "Capitalize greeting"), ["bugfix/greeting"]),
        (C("a03091b", "Greet the world"), ["v0.2"]),
        (C("4f248f5", "Add usage doc"), []),
        (C("9128ebb", "Require a password"), ["feature/login"]),
        (C("5a049c2", "Add login check"), []),
        (C("7450f74", "Print greeting", body: "The first real feature. This commit has a description body"), ["v0.1"]),
        (C("ce604a1", "Add app skeleton"), []),
        (C("ba0dba8", "Initial commit"), []),
    ];

    private static List<int> Find(string q) => Rows.Select((r, i) => (r, i)).Where(x => CommitListFind.Matches(x.r.Commit, x.r.Refs, q)).Select(x => x.i).ToList();

    [Theory]
    [InlineData("greet", 4)]
    [InlineData("description", 1)]
    [InlineData("other", 1)]
    [InlineData("example.com", 11)]
    [InlineData("a03091", 1)]
    [InlineData("login", 2)]
    [InlineData("SANDBOX", 10)]
    [InlineData("zzz", 0)]
    public void CountsMatch(string q, int count) => Assert.Equal(count, Find(q).Count);

    [Fact]
    public void StartAndStepMatch()
    {
        var m = Find("greet");                       // 0, 3, 4, 8
        Assert.Equal(3, CommitListFind.Start(m, 2));  // origin/main を選んでいた → Capitalize greeting
        Assert.Equal(4, CommitListFind.Start(m, 4));  // 一致している行ならそのまま
        Assert.Equal(8, CommitListFind.Step(m, 4, forward: true));
        Assert.Equal(-1, CommitListFind.Step(m, 8, forward: true));   // 末尾で止まる
        Assert.Equal(4, CommitListFind.Step(m, 8, forward: false));
        Assert.Equal(4, CommitListFind.Start(Find("a03091"), 8));       // 下に無ければ上の一致
        Assert.Equal(-1, CommitListFind.Start(Find("zzz"), 6));
        Assert.Equal(["0 matches", "1 match", "4 matches"], new[] { 0, 1, 4 }.Select(CommitListFind.CountText));
    }
}

using GitKnife.Core.Git.Model;
using GitKnife.Core.Graph;

namespace GitKnife.Core.Tests;

/// <summary>マージの折りたたみ。</summary>
public class MergeCollapseTests
{
    private static CommitInfo C(string sha, params string[] parents)
        => new(sha, parents, "a", "a@x", DateTimeOffset.UnixEpoch, "a", "a@x", DateTimeOffset.UnixEpoch, sha, "");

    // merged = main + feature/login のマージ。bugfix はどこにもマージされていない
    private static readonly CommitInfo[] Commits =
    [
        C("merged", "main", "login2"),
        C("main", "usage"),
        C("bugfix", "usage"),
        C("usage", "print"),
        C("login2", "login1"),
        C("login1", "print"),
        C("print", "init"),
        C("init"),
    ];

    private static string[] Shas(IReadOnlyList<CommitInfo> list) => list.Select(c => c.Sha).ToArray();

    [Fact]
    public void CollapsedMergeHidesMergedSideAndKeepsOtherBranches()
    {
        var r = MergeCollapse.Apply(Commits, sha => sha == "merged");
        Assert.Equal(["merged", "main", "bugfix", "usage", "print", "init"], Shas(r));
        var m = r[0];
        Assert.True(m.IsCollapsedMerge);
        Assert.Equal(["main"], m.Parents);
        Assert.Equal(["main", "login2"], m.DisplayParents);   // 詳細の PARENTS は 2 つのまま
    }

    [Fact]
    public void NothingCollapsedKeepsList()
    {
        Assert.Same(Commits, MergeCollapse.Apply(Commits, _ => false));
    }

    [Fact]
    public void AllModeAndToggledShas()
    {
        var toggled = new HashSet<string> { "merged" };
        Assert.True(MergeCollapse.IsCollapsed("merged", allMode: false, toggled));
        Assert.False(MergeCollapse.IsCollapsed("merged", allMode: true, toggled));   // 全部たたむときは toggled だけ開く
        Assert.True(MergeCollapse.IsCollapsed("other", allMode: true, toggled));
    }

    [Fact]
    public void BranchFromMergedSideStaysVisible()
    {
        // login1 から分かれた別のブランチの先は残る（そこから login1 も見える）
        var list = Commits.Append(C("side", "login1")).ToArray();
        var r = MergeCollapse.Apply(list, sha => sha == "merged");
        Assert.Contains("side", Shas(r));
        Assert.Contains("login1", Shas(r));
        Assert.DoesNotContain("login2", Shas(r));
    }
}

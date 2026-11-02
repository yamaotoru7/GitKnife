using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;
using GitKnife.Core.Graph;

namespace GitKnife.Core.Tests;

[Collection("sandbox")]
public class GraphLayoutTests(SandboxFixture sandbox)
{
    private static CommitInfo C(string sha, params string[] parents)
        => new(sha, parents, "a", "a@x", DateTimeOffset.UnixEpoch, "a", "a@x", DateTimeOffset.UnixEpoch, sha, "");

    [Fact]
    public void LinearHistoryStaysInOneLane()
    {
        var rows = GraphLayout.Compute([C("c", "b"), C("b", "a"), C("a")]);
        Assert.All(rows, r => Assert.Equal(0, r.NodeLane));
        Assert.All(rows, r => Assert.Equal(rows[0].NodeColor, r.NodeColor));
        Assert.Equal(1, rows.Max(r => r.Width));
        Assert.DoesNotContain(rows[2].Segments, s => !s.TopHalf);   // ルートから下へは線が出ない
    }

    [Fact]
    public void BranchAndMerge()
    {
        //   m      (merge of b2 into a2)
        //   |\
        //   a2 b2
        //   |  b1
        //   | /
        //   a1
        var rows = GraphLayout.Compute([C("m", "a2", "b2"), C("a2", "a1"), C("b2", "b1"), C("b1", "a1"), C("a1")]);
        Assert.True(rows[0].IsMerge);
        Assert.Equal(0, rows[0].NodeLane);
        Assert.Contains(rows[0].Segments, s => !s.TopHalf && s.From == 0 && s.To == 1);   // 2 つ目の親は右の列へ
        Assert.Equal(0, rows[1].NodeLane);
        Assert.Equal(1, rows[2].NodeLane);
        Assert.Equal(1, rows[3].NodeLane);
        Assert.Equal(0, rows[4].NodeLane);
        Assert.Contains(rows[4].Segments, s => s.TopHalf && s.From == 1 && s.To == 0);    // 合流
        Assert.NotEqual(rows[0].NodeColor, rows[2].NodeColor);                            // 別ブランチは別の色
        Assert.Equal(rows[0].NodeColor, rows[1].NodeColor);                               // 最初の親は色を引き継ぐ
    }

    [Fact]
    public void TwoTipsOnSameParentBothConverge()
    {
        var rows = GraphLayout.Compute([C("x", "base"), C("y", "base"), C("base", "root"), C("root")]);
        Assert.Equal(0, rows[0].NodeLane);
        Assert.Equal(1, rows[1].NodeLane);
        Assert.Equal(0, rows[2].NodeLane);
        Assert.Equal(2, rows[2].Segments.Count(s => s.TopHalf));   // 親の行で 2 本が合流
        Assert.Equal(2, rows[2].Width);
        Assert.Equal(1, rows[3].Width);   // 合流した後の空き列は詰める
    }

    [Fact]
    public void MissingParentKeepsLaneOpen()
    {
        var rows = GraphLayout.Compute([C("b", "a"), C("z", "y")]);   // a も y も一覧にない
        Assert.Contains(rows[1].Segments, s => s.From == 0 && s.To == 0 && s.TopHalf);
        Assert.Equal(1, rows[1].NodeLane);
    }

    [Fact]
    public async Task SandboxGraphShape()
    {
        var reader = (await RepositoryReader.OpenAsync(sandbox.Git, sandbox.Basic))!;
        var refs = await reader.ReadReferencesAsync();
        var commits = await reader.ReadCommitsAsync(new CommitQuery { Order = RevisionSortOrder.Date }, refs);
        var rows = GraphLayout.Compute(commits);
        // stash と main は同じ列、Add changelog / Capitalize greeting / Require a password は右の列に分かれ、Initial commit で 1 列に戻る
        var lane = commits.Select((c, i) => (c.Subject, rows[i].NodeLane)).ToDictionary(x => x.Subject, x => x.NodeLane);
        Assert.Equal(lane["Document options"], lane["Greet the world"]);
        Assert.NotEqual(lane["Greet the world"], lane["Add changelog"]);
        Assert.NotEqual(lane["Greet the world"], lane["Capitalize greeting"]);
        Assert.NotEqual(lane["Print greeting"], lane["Require a password"]);
        Assert.Equal(1, rows[^1].Width);
        Assert.True(rows.Max(r => r.Width) <= 4);
    }
}

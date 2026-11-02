using GitKnife.Core.Diff;

namespace GitKnife.Core.Tests;

public class DiffLayoutTests
{
    private static FileDiff Sample() => UnifiedDiffParser.Parse("""
        diff --git gkSrcPrefix/a.txt gkDstPrefix/a.txt
        index 1111111111111111111111111111111111111111..2222222222222222222222222222222222222222 100644
        --- gkSrcPrefix/a.txt
        +++ gkDstPrefix/a.txt
        @@ -1,4 +1,5 @@
         one
        -two
        +TWO
        +two and a half
         three
        -four
        """)[0];

    [Fact]
    public void SideBySidePairsRemovedAndAddedLines()
    {
        var rows = DiffLayout.SideBySide(Sample());
        Assert.Equal([DiffRowKind.HunkHeader, DiffRowKind.Context, DiffRowKind.Change, DiffRowKind.Change, DiffRowKind.Context, DiffRowKind.Change], rows.Select(r => r.Kind));
        Assert.Equal(("two", "TWO"), (rows[2].Left!.Text, rows[2].Right!.Text));
        Assert.Null(rows[3].Left);                       // 左は詰め物
        Assert.Equal(3, rows[3].Right!.Number);
        Assert.Equal(rows[2].Block, rows[3].Block);
        Assert.Equal(rows[2].Block, rows[5].Block);      // 間の文脈行が 1 行なので 1 つのまとまり
        Assert.Equal(rows[2].Block, rows[1].Block);      // 前の文脈行も含む
        Assert.Equal(-1, rows[0].Block);
        Assert.Null(rows[5].Right);
    }

    [Fact]
    public void DistantChangesAreSeparateBlocks()
    {
        // 30 行のうち 2 行目と 28 行目の変更
        var text = "diff --git gkSrcPrefix/n.txt gkDstPrefix/n.txt\n--- gkSrcPrefix/n.txt\n+++ gkDstPrefix/n.txt\n@@ -1,30 +1,30 @@\n"
            + string.Concat(Enumerable.Range(1, 30).Select(i => i is 2 or 28 ? $"-line {i}\n+line {i} changed\n" : $" line {i}\n"));
        var rows = DiffLayout.SideBySide(UnifiedDiffParser.Parse(text)[0]);
        var b2 = rows[2].Block;
        Assert.Equal([1, 2, 3, 4], rows.Select((r, i) => (r, i)).Where(x => x.r.Block == b2).Select(x => x.i));   // 枠：1〜4 行目
        Assert.NotEqual(b2, rows[28].Block);
    }

    [Fact]
    public void InlineListsRemovedBeforeAdded()
    {
        var rows = DiffLayout.Inline(Sample());
        Assert.Equal(7, rows.Count);
        Assert.Equal("two", rows[2].Left!.Text);
        Assert.Null(rows[2].Right);
        Assert.Equal("TWO", rows[3].Right!.Text);
    }

    [Fact]
    public void SelectedRowsMapToHunkLineIndexes()
    {
        var rows = DiffLayout.SideBySide(Sample());
        var sel = DiffLayout.SelectedLines(rows, [1, 2, 3]);
        Assert.Equal([1, 2, 3], sel[0].Order());   // "-two" "+TWO" "+two and a half"（文脈行は含めない）
    }

    /// <summary>見出しは行数を省かずに出す（git の "@@ -0,0 +1 @@" を "@@ -0,0 +1,1 @@" に）。部分ステージのパッチは元の行のまま。</summary>
    [Fact]
    public void HunkHeaderShowsCounts()
    {
        var diff = UnifiedDiffParser.Parse("""
            diff --git a/notes.txt b/notes.txt
            new file mode 100644
            --- /dev/null
            +++ b/notes.txt
            @@ -0,0 +1 @@ section
            +hello
            """)[0];
        Assert.Equal("@@ -0,0 +1,1 @@ section", DiffLayout.Inline(diff)[0].HeaderText);
        Assert.Equal("@@ -0,0 +1 @@ section", diff.Hunks[0].Header);
    }
}

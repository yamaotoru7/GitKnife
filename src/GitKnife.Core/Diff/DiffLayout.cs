namespace GitKnife.Core.Diff;

public enum DiffRowKind { HunkHeader, Context, Change }

/// <summary>diff 表示の片側の 1 行。LineIndex は hunk.Lines の添字（部分ステージで使う）。</summary>
public sealed record DiffSideLine(int Number, string Text, DiffLineKind Kind, int LineIndex);

/// <summary>
/// diff 表示の 1 行。Side by side なら Left（旧）と Right（新）を並べ、片側が無ければ詰め物。
/// Inline なら Left だけ（削除行）、Right だけ（追加行）、両方（文脈行）のどれか。
/// Block はクリックで選ぶまとまりの番号。変更行と、その前後 <see cref="DiffLayout.BlockMargin"/> 行の文脈行が持つ。
/// 間の文脈行が 2×BlockMargin 以下の変更どうしは 1 つにまとめる。見出しとまとまりの外は -1。
/// </summary>
public sealed record DiffRow(DiffRowKind Kind, int Hunk, int Block, DiffSideLine? Left, DiffSideLine? Right, string HeaderText = "");

/// <summary>FileDiff を画面の行に並べる。</summary>
public static class DiffLayout
{
    /// <summary>まとまりの前後に含める文脈行の数（選択枠は変更の 2 行上から始まる）。</summary>
    public const int BlockMargin = 2;

    public static IReadOnlyList<DiffRow> SideBySide(FileDiff diff) => Build(diff, sideBySide: true);
    public static IReadOnlyList<DiffRow> Inline(FileDiff diff) => Build(diff, sideBySide: false);

    private static List<DiffRow> Build(FileDiff diff, bool sideBySide)
    {
        var rows = new List<DiffRow>();
        var block = 0;
        for (var h = 0; h < diff.Hunks.Count; h++)
        {
            var hunk = diff.Hunks[h];
            rows.Add(new DiffRow(DiffRowKind.HunkHeader, h, -1, null, null, hunk.DisplayHeader));
            var i = 0;
            while (i < hunk.Lines.Count)
            {
                var line = hunk.Lines[i];
                if (line.Kind == DiffLineKind.NoNewlineAtEnd) { i++; continue; }
                if (line.Kind == DiffLineKind.Context)
                {
                    rows.Add(new DiffRow(DiffRowKind.Context, h, -1,
                        new DiffSideLine(line.OldNumber, line.DisplayText, line.Kind, i),
                        new DiffSideLine(line.NewNumber, line.DisplayText, line.Kind, i)));
                    i++;
                    continue;
                }
                // 変更のまとまり：連続する - 行と + 行
                var removed = new List<DiffSideLine>();
                var added = new List<DiffSideLine>();
                while (i < hunk.Lines.Count && hunk.Lines[i].Kind != DiffLineKind.Context)
                {
                    var l = hunk.Lines[i];
                    if (l.Kind == DiffLineKind.Removed) removed.Add(new DiffSideLine(l.OldNumber, l.DisplayText, l.Kind, i));
                    else if (l.Kind == DiffLineKind.Added) added.Add(new DiffSideLine(l.NewNumber, l.DisplayText, l.Kind, i));
                    i++;
                }
                if (sideBySide)
                {
                    for (var k = 0; k < Math.Max(removed.Count, added.Count); k++)
                        rows.Add(new DiffRow(DiffRowKind.Change, h, block, k < removed.Count ? removed[k] : null, k < added.Count ? added[k] : null));
                }
                else
                {
                    foreach (var r in removed) rows.Add(new DiffRow(DiffRowKind.Change, h, block, r, null));
                    foreach (var a in added) rows.Add(new DiffRow(DiffRowKind.Change, h, block, null, a));
                }
                block++;
            }
        }
        return AssignBlocks(rows);
    }

    /// <summary>連続する変更を、近いものどうしまとめて Block を振り直す（前後の文脈行も含める）。</summary>
    private static List<DiffRow> AssignBlocks(List<DiffRow> rows)
    {
        var block = 0;
        var i = 0;
        while (i < rows.Count)
        {
            if (rows[i].Kind != DiffRowKind.Change) { i++; continue; }
            var hunk = rows[i].Hunk;
            var first = i;
            var last = i;
            // 同じハンクで、間の文脈行が 2×BlockMargin 以下なら続ける
            var j = i + 1;
            while (j < rows.Count && rows[j].Hunk == hunk && rows[j].Kind != DiffRowKind.HunkHeader)
            {
                if (rows[j].Kind == DiffRowKind.Change)
                {
                    if (j - last - 1 > 2 * BlockMargin) break;
                    last = j;
                }
                j++;
            }
            var from = first;
            for (var k = 0; k < BlockMargin && from - 1 >= 0 && rows[from - 1].Kind == DiffRowKind.Context && rows[from - 1].Hunk == hunk; k++) from--;
            var to = last;
            for (var k = 0; k < BlockMargin && to + 1 < rows.Count && rows[to + 1].Kind == DiffRowKind.Context && rows[to + 1].Hunk == hunk; k++) to++;
            for (var k = from; k <= to; k++) rows[k] = rows[k] with { Block = block };
            block++;
            i = to + 1;
        }
        return rows;
    }

    /// <summary>選んだ行（rows の添字）に含まれる変更行を、ハンクごとの hunk.Lines の添字にまとめる。</summary>
    public static Dictionary<int, HashSet<int>> SelectedLines(IReadOnlyList<DiffRow> rows, IEnumerable<int> rowIndexes)
    {
        var result = new Dictionary<int, HashSet<int>>();
        foreach (var ri in rowIndexes)
        {
            var row = rows[ri];
            if (row.Kind != DiffRowKind.Change) continue;
            if (!result.TryGetValue(row.Hunk, out var set)) result[row.Hunk] = set = [];
            if (row.Left is { } l) set.Add(l.LineIndex);
            if (row.Right is { } r) set.Add(r.LineIndex);
        }
        return result;
    }
}

using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Graph;

/// <summary>グラフの線分。行の上半分（上端 → ノードの高さ）か下半分（ノードの高さ → 下端）を、列 From から列 To へ結ぶ。</summary>
public readonly record struct GraphSegment(int From, int To, int Color, bool TopHalf);

/// <summary>コミット一覧 1 行ぶんのグラフ。</summary>
public sealed class GraphRow
{
    public required int NodeLane { get; init; }
    public required int NodeColor { get; init; }
    public required IReadOnlyList<GraphSegment> Segments { get; init; }
    /// <summary>この行で使っている列の数（描画幅）。</summary>
    public required int Width { get; init; }
    /// <summary>マージコミット（親が 2 つ以上）。ノードの描き方を変える。</summary>
    public bool IsMerge { get; init; }
    /// <summary>たたんだマージ（ノードの印が "›"。開いているマージは "⌄"）。</summary>
    public bool IsCollapsedMerge { get; init; }
}

/// <summary>
/// コミット一覧の列割り当て。表示順のコミット列を上から 1 回なめて、各列が「次に来るはずのコミット」を覚えておく古典的な方式。
/// - 最初の親はノードと同じ列・同じ色を引き継ぐ（ブランチの線がまっすぐ続く）
/// - 最初の親は、他の列が同じ親を待っていても自分の列で待つ（線は親の行で合流する）
/// - 2 つ目以降の親は、すでにその親を待っている列があればそこへ、なければ空いている列を新しく使う
/// - 同じコミットを待っている列が複数あれば、ノードに合流させて空ける
/// - 一覧にない親（件数上限やフィルタで切れた）を待つ列は最後まで伸びる
/// 色は列の位置で決める（0 列目、1 列目…で色が固定。ブランチが列を移っても、最初の親は元の色のまま続く）。
/// </summary>
public static class GraphLayout
{
    public static IReadOnlyList<GraphRow> Compute(IReadOnlyList<CommitInfo> commits, int paletteSize = 12)
    {
        var rows = new GraphRow[commits.Count];
        var lanes = new List<string?>();        // 各列が待っているコミット
        var laneColors = new List<int>();

        int Allocate(string sha, int minLane = 0)
        {
            for (var i = minLane; i < lanes.Count; i++)
                if (lanes[i] is null) { lanes[i] = sha; laneColors[i] = i % paletteSize; return i; }
            lanes.Add(sha);
            laneColors.Add((lanes.Count - 1) % paletteSize);
            return lanes.Count - 1;
        }

        for (var r = 0; r < commits.Count; r++)
        {
            var c = commits[r];
            var segments = new List<GraphSegment>();

            // --- 上半分：この行に入ってくる線 ---
            var node = lanes.IndexOf(c.Sha);
            var isTip = node < 0;
            if (isTip) node = Allocate(c.Sha);
            var nodeColor = laneColors[node];
            for (var i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] is null || (isTip && i == node)) continue;
                if (lanes[i] == c.Sha)
                {
                    segments.Add(new GraphSegment(i, node, laneColors[i], TopHalf: true));   // 合流（node 自身は縦線）
                    if (i != node) lanes[i] = null;
                }
                else segments.Add(new GraphSegment(i, i, laneColors[i], TopHalf: true));    // 通過
            }

            // --- 親の割り当て ---
            lanes[node] = null;
            var parentLanes = new List<int>();
            for (var p = 0; p < c.Parents.Count; p++)
            {
                var parent = c.Parents[p];
                // 最初の親は、他の列がすでに同じ親を待っていても自分の列で待つ（合流は親の行で描く）
                if (p == 0) { lanes[node] = parent; laneColors[node] = nodeColor; parentLanes.Add(node); continue; }
                var existing = lanes.IndexOf(parent);
                if (existing >= 0) { parentLanes.Add(existing); continue; }
                parentLanes.Add(Allocate(parent, minLane: node + 1));
            }

            // 空いた列は途中でも左へ詰める（線は下半分で斜めに寄る）。新しいブランチの先端は空きを使わず右端に足すことになる
            // （上のブランチの線を詰めて並べ、先端をその右に置く）
            var map = new int[lanes.Count];
            var packed = new List<string?>();
            var packedColors = new List<int>();
            for (var i = 0; i < lanes.Count; i++)
            {
                map[i] = packed.Count;
                if (lanes[i] is null) continue;
                packed.Add(lanes[i]);
                packedColors.Add(laneColors[i]);
            }

            // --- 下半分：この行から出ていく線 ---
            for (var i = 0; i < lanes.Count; i++)
            {
                if (lanes[i] is null || parentLanes.Contains(i)) continue;
                segments.Add(new GraphSegment(i, map[i], laneColors[i], TopHalf: false));    // 通過（詰めるときは斜め）
            }
            foreach (var pl in parentLanes.Distinct())
                segments.Add(new GraphSegment(node, map[pl], pl == node ? nodeColor : laneColors[pl], TopHalf: false));

            lanes.Clear(); lanes.AddRange(packed);
            laneColors.Clear(); laneColors.AddRange(packedColors);

            var width = 1 + Math.Max(node, segments.Count == 0 ? 0 : segments.Max(s => Math.Max(s.From, s.To)));
            rows[r] = new GraphRow { NodeLane = node, NodeColor = nodeColor, Segments = segments, Width = width, IsMerge = c.Parents.Count > 1 || c.IsCollapsedMerge, IsCollapsedMerge = c.IsCollapsedMerge };
        }
        return rows;
    }

    /// <summary>テストとデバッグ用：グラフを文字で描く（"*" ノード、"|" 通過、"/" "\" 斜め）。</summary>
    public static string Render(IReadOnlyList<GraphRow> rows)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var row in rows)
        {
            var w = Math.Max(row.Width, 1);
            var line = new char[w * 2];
            Array.Fill(line, ' ');
            foreach (var s in row.Segments.Where(s => s.TopHalf && s.From == s.To)) line[s.From * 2] = '|';
            line[row.NodeLane * 2] = row.IsMerge ? 'M' : '*';
            sb.AppendLine(new string(line).TrimEnd());
        }
        return sb.ToString();
    }
}

namespace GitKnife.Core.Diff;

/// <summary>競合ファイルの 1 区間：競合していない部分（Context）か、競合（Ours / Base / Theirs）。</summary>
public abstract record ConflictSection;

public sealed record ContextSection(IReadOnlyList<string> Lines) : ConflictSection;

public sealed record ConflictBlock(
    string OursLabel,
    IReadOnlyList<string> Ours,
    string? BaseLabel,
    IReadOnlyList<string>? Base,
    string TheirsLabel,
    IReadOnlyList<string> Theirs) : ConflictSection;

/// <summary>
/// 競合マーカー（`<<<<<<<`、diff3 の `|||||||`、`=======`、`>>>>>>>`）のある作業ツリーのファイルを区間に分ける。
/// Merge Conflict Resolver の表示モデルの元になる。マーカーの長さは 7 文字（git の既定。conflict-marker-size 属性で変わる場合は markerSize を渡す）。
/// </summary>
public static class ConflictParser
{
    public static IReadOnlyList<ConflictSection> Parse(string text, int markerSize = 7)
    {
        var start = new string('<', markerSize);
        var baseMark = new string('|', markerSize);
        var sep = new string('=', markerSize);
        var end = new string('>', markerSize);

        var sections = new List<ConflictSection>();
        var context = new List<string>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 0 && lines[^1].Length == 0) lines = lines[..^1];

        bool IsMarker(string line, string mark) => line.StartsWith(mark, StringComparison.Ordinal) && (line.Length == markerSize || line[markerSize] == ' ');
        string Label(string line) => line.Length > markerSize ? line[(markerSize + 1)..] : "";

        var i = 0;
        while (i < lines.Length)
        {
            if (!IsMarker(lines[i], start)) { context.Add(lines[i++]); continue; }

            // 閉じマーカーがなければ普通の行として扱う
            var j = i + 1;
            var baseAt = -1; var sepAt = -1; var endAt = -1;
            for (; j < lines.Length; j++)
            {
                if (sepAt < 0 && baseAt < 0 && IsMarker(lines[j], baseMark)) baseAt = j;
                else if (sepAt < 0 && IsMarker(lines[j], sep) && lines[j].Length == markerSize) sepAt = j;
                else if (sepAt >= 0 && IsMarker(lines[j], end)) { endAt = j; break; }
                else if (IsMarker(lines[j], start)) break;   // 入れ子は扱わない
            }
            if (sepAt < 0 || endAt < 0) { context.Add(lines[i++]); continue; }

            if (context.Count > 0) { sections.Add(new ContextSection(context.ToArray())); context.Clear(); }
            var oursEnd = baseAt >= 0 ? baseAt : sepAt;
            sections.Add(new ConflictBlock(
                Label(lines[i]),
                lines[(i + 1)..oursEnd],
                baseAt >= 0 ? Label(lines[baseAt]) : null,
                baseAt >= 0 ? lines[(baseAt + 1)..sepAt] : null,
                Label(lines[endAt]),
                lines[(sepAt + 1)..endAt]));
            i = endAt + 1;
        }
        if (context.Count > 0) sections.Add(new ContextSection(context.ToArray()));
        return sections;
    }

    public enum Choice { Ours, Theirs, OursThenTheirs, TheirsThenOurs, Base }

    /// <summary>競合ごとの選択から結果のテキストを組み立てる（Resolver の「左を採用／右を採用／両方」）。</summary>
    public static string Resolve(IReadOnlyList<ConflictSection> sections, Func<int, ConflictBlock, Choice> choose, string newline = "\n")
    {
        var output = new List<string>();
        var n = 0;
        foreach (var s in sections)
        {
            switch (s)
            {
                case ContextSection c: output.AddRange(c.Lines); break;
                case ConflictBlock b:
                    switch (choose(n++, b))
                    {
                        case Choice.Ours: output.AddRange(b.Ours); break;
                        case Choice.Theirs: output.AddRange(b.Theirs); break;
                        case Choice.OursThenTheirs: output.AddRange(b.Ours); output.AddRange(b.Theirs); break;
                        case Choice.TheirsThenOurs: output.AddRange(b.Theirs); output.AddRange(b.Ours); break;
                        case Choice.Base: output.AddRange(b.Base ?? []); break;
                    }
                    break;
            }
        }
        return string.Join(newline, output) + newline;
    }
}

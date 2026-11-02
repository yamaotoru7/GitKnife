namespace GitKnife.Core.Diff;

/// <summary>
/// Merge Conflict Resolver の中身。競合ごとに採用する側（theirs / ours。押した順）を持ち、
/// 左（theirs）・右（ours）・結果の 3 つのテキストと、競合がどの行にあるかを作る。
/// 結果で未解決の競合は "--- Merge Conflict ---" の 1 行で表す。
/// </summary>
public sealed class MergeResolution
{
    public const string Placeholder = "--- Merge Conflict ---";

    public enum Side { Theirs, Ours }

    /// <summary>テキスト上の行の範囲（0 始まりの開始行と行数）。</summary>
    public readonly record struct LineRange(int Start, int Count)
    {
        public bool Contains(int line) => line >= Start && line < Start + Math.Max(1, Count);
    }

    private readonly IReadOnlyList<ConflictSection> _sections;
    private readonly List<List<Side>> _choices = [];

    public MergeResolution(string conflictedText)
    {
        _sections = ConflictParser.Parse(conflictedText.Replace("\r\n", "\n"));
        foreach (var _ in _sections.OfType<ConflictBlock>()) _choices.Add([]);
    }

    public int ConflictCount => _choices.Count;
    public int ResolvedCount => _choices.Count(c => c.Count > 0);
    public bool IsResolved => ResolvedCount == ConflictCount;
    public IReadOnlyList<Side> ChoicesOf(int conflict) => _choices[conflict];

    /// <summary>競合 n の side を入れる／外す（入れるときは後ろに足す＝押した順に並ぶ）。</summary>
    public void Toggle(int conflict, Side side)
    {
        var list = _choices[conflict];
        if (!list.Remove(side)) list.Add(side);
    }

    /// <summary>全部の競合で side だけを採る（AllRemoteCheckBox / AllLocalCheckBox）。on = false なら全部から外す。</summary>
    public void SetAll(Side side, bool on)
    {
        foreach (var list in _choices)
        {
            list.Remove(side);
            if (on) list.Add(side);
        }
    }

    public bool AllHave(Side side) => _choices.Count > 0 && _choices.All(c => c.Contains(side));

    /// <summary>片側の全文と、競合の行の範囲。</summary>
    public (string text, IReadOnlyList<LineRange> conflicts) Build(Side side)
    {
        var lines = new List<string>();
        var ranges = new List<LineRange>();
        foreach (var s in _sections)
        {
            switch (s)
            {
                case ContextSection c: lines.AddRange(c.Lines); break;
                case ConflictBlock b:
                    var part = side == Side.Theirs ? b.Theirs : b.Ours;
                    ranges.Add(new LineRange(lines.Count, part.Count));
                    lines.AddRange(part);
                    break;
            }
        }
        return (string.Join("\n", lines), ranges);
    }

    /// <summary>結果の全文と、競合の行の範囲（未解決なら Placeholder の 1 行）。</summary>
    public (string text, IReadOnlyList<LineRange> conflicts) BuildMerged()
    {
        var lines = new List<string>();
        var ranges = new List<LineRange>();
        var n = 0;
        foreach (var s in _sections)
        {
            switch (s)
            {
                case ContextSection c: lines.AddRange(c.Lines); break;
                case ConflictBlock b:
                    var start = lines.Count;
                    var chosen = _choices[n++];
                    if (chosen.Count == 0) lines.Add(Placeholder);
                    foreach (var side in chosen) lines.AddRange(side == Side.Theirs ? b.Theirs : b.Ours);
                    ranges.Add(new LineRange(start, lines.Count - start));
                    break;
            }
        }
        return (string.Join("\n", lines), ranges);
    }

    /// <summary>ファイルに書く内容（末尾の改行つき）。</summary>
    public string MergedFileText() => BuildMerged().text + "\n";
}

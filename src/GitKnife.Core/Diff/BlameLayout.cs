using GitKnife.Core.Git;

namespace GitKnife.Core.Diff;

/// <summary>Blame の表示の 1 行（ファイル全体の diff の行と、その行を最後に変えたコミット）。</summary>
public sealed record BlameRow(DiffLineKind Kind, int OldNumber, int NewNumber, string Text, BlameLine? Blame)
{
    /// <summary>左の列でまとまりの先頭か（直前の行と別のコミット）。先頭の行にだけコミットの件名などを出す。</summary>
    public bool StartsGroup { get; init; }
}

/// <summary>
/// Blame の表示：選んだリビジョンでのファイル全体を、そのリビジョンの変更を挟んで並べる。
/// 残った行と足した行はそのリビジョンの blame（新しい行番号）、消した行は親の blame（古い行番号）で、誰が書いたかを出す。
/// </summary>
public static class BlameLayout
{
    public static IReadOnlyList<BlameRow> Build(FileDiff? fullDiff, IReadOnlyList<BlameLine> current, IReadOnlyList<BlameLine> parent)
    {
        var rows = new List<BlameRow>();
        var byNew = current.ToDictionary(b => b.FinalLine);
        var byOld = parent.ToDictionary(b => b.FinalLine);
        if (fullDiff is null || fullDiff.Hunks.Count == 0)
        {
            // そのリビジョンでは変わっていない：blame の行をそのまま
            foreach (var b in current) rows.Add(new BlameRow(DiffLineKind.Context, b.FinalLine, b.FinalLine, b.Text, b));
        }
        else
        {
            foreach (var line in fullDiff.Hunks.SelectMany(h => h.Lines))
            {
                if (line.Kind == DiffLineKind.NoNewlineAtEnd) continue;
                var blame = line.Kind == DiffLineKind.Removed
                    ? byOld.GetValueOrDefault(line.OldNumber)
                    : byNew.GetValueOrDefault(line.NewNumber);
                rows.Add(new BlameRow(line.Kind, line.OldNumber, line.NewNumber, line.DisplayText, blame));
            }
        }
        for (var i = 0; i < rows.Count; i++)
            rows[i] = rows[i] with { StartsGroup = i == 0 || rows[i - 1].Blame?.Sha != rows[i].Blame?.Sha };
        return rows;
    }
}

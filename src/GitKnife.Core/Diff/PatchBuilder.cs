using System.Text;

namespace GitKnife.Core.Diff;

/// <summary>diff の上で選んだ行に対する操作。</summary>
public enum PatchTarget
{
    /// <summary>unstaged の diff から選んだ行をステージ（`apply --cached`）。</summary>
    Stage,
    /// <summary>staged の diff から選んだ行を戻す（`apply --cached --reverse`）。</summary>
    Unstage,
    /// <summary>unstaged の diff から選んだ行を捨てる。逆向きのパッチを自分で作って `--reverse` なしで当てる。</summary>
    Discard,
}

/// <summary>
/// チャンク・選択行のステージ／アンステージ／破棄に使うパッチを組み立てる。git apply に stdin で渡す。
///
/// - 順方向（Stage）：選ばなかった + 行は捨て、選ばなかった - 行は文脈行にする
/// - 逆方向（Unstage、Discard）：選ばなかった + 行は文脈行にし、選ばなかった - 行は捨てる
/// - Discard はさらに +/- と旧/新を入れ替えて、そのまま当てられる向きにする
/// </summary>
public static class PatchBuilder
{
    /// <summary>
    /// hunk の lineIndexes（hunk.Lines の添字）だけを含むパッチ。変更行が 1 つも残らなければ null。
    /// lineIndexes が null ならハンク全体。
    /// </summary>
    public static string? Build(FileDiff file, DiffHunk hunk, IReadOnlySet<int>? lineIndexes, PatchTarget target)
    {
        var reverse = target != PatchTarget.Stage;
        var invert = target == PatchTarget.Discard;
        var lines = new List<(char prefix, string text)>();
        int oldCount = 0, newCount = 0, changes = 0;
        var lastEmitted = false;
        for (var i = 0; i < hunk.Lines.Count; i++)
        {
            var line = hunk.Lines[i];
            var selected = lineIndexes is null || lineIndexes.Contains(i);
            switch (line.Kind)
            {
                case DiffLineKind.Context:
                    Emit(' ', line.Text); oldCount++; newCount++;
                    break;
                case DiffLineKind.Added when selected:
                    Emit('+', line.Text); newCount++; changes++;
                    break;
                case DiffLineKind.Added when reverse:
                    Emit(' ', line.Text); oldCount++; newCount++;
                    break;
                case DiffLineKind.Removed when selected:
                    Emit('-', line.Text); oldCount++; changes++;
                    break;
                case DiffLineKind.Removed when !reverse:
                    Emit(' ', line.Text); oldCount++; newCount++;
                    break;
                case DiffLineKind.NoNewlineAtEnd:
                    // 直前の行を出したときだけ付ける
                    if (lastEmitted) lines.Add(('\\', line.Text));
                    break;
                default:
                    lastEmitted = false;
                    break;
            }
        }
        if (changes == 0) return null;

        // 当てる相手側（順方向は旧、逆方向は新）の開始行を基準にする。もう一方の開始行は 1 ハンクだけ当てる前提で合わせる
        int oldStart, newStart;
        if (!reverse)
        {
            oldStart = hunk.OldStart;
            newStart = OtherStart(oldStart, oldCount, newCount);
        }
        else
        {
            newStart = hunk.NewStart;
            oldStart = OtherStart(newStart, newCount, oldCount);
        }
        if (invert)
        {
            (oldStart, newStart) = (newStart, oldStart);
            (oldCount, newCount) = (newCount, oldCount);
        }

        var sb = new StringBuilder();
        foreach (var h in file.HeaderLines) sb.Append(h).Append('\n');
        sb.Append($"@@ -{oldStart},{oldCount} +{newStart},{newCount} @@\n");
        foreach (var (prefix, text) in lines)
        {
            if (prefix == '\\') { sb.Append(text).Append('\n'); continue; }
            var p = invert ? prefix switch { '+' => '-', '-' => '+', _ => prefix } : prefix;
            sb.Append(p).Append(text).Append('\n');
        }
        return sb.ToString();

        void Emit(char prefix, string text)
        {
            lines.Add((prefix, text));
            lastEmitted = true;
        }
    }

    /// <summary>ファイルの diff をそのままパッチに戻したもの（全ハンク）。ハンクが無ければ null。</summary>
    public static string? BuildAll(FileDiff file)
    {
        if (file.Hunks.Count == 0) return null;
        var sb = new StringBuilder();
        foreach (var h in file.HeaderLines) sb.Append(h).Append('\n');
        foreach (var hunk in file.Hunks)
        {
            sb.Append(hunk.Header).Append('\n');
            foreach (var l in hunk.Lines)
            {
                if (l.Kind == DiffLineKind.NoNewlineAtEnd) { sb.Append(l.Text).Append('\n'); continue; }
                sb.Append(l.Kind switch { DiffLineKind.Added => '+', DiffLineKind.Removed => '-', _ => ' ' }).Append(l.Text).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// unified diff の開始行の決まり：行数 0 の側は「その直前の行」を指す。
    /// fixedStart/fixedCount（基準側）から、もう一方の開始行を求める。
    /// </summary>
    private static int OtherStart(int fixedStart, int fixedCount, int otherCount)
    {
        if (fixedCount == 0 && otherCount > 0) return fixedStart + 1;
        if (fixedCount > 0 && otherCount == 0) return fixedStart - 1;
        return fixedStart;
    }
}

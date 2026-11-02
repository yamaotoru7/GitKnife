namespace GitKnife.Core.Text;

/// <summary>
/// Quick Launch の絞り込み。打った文字が順に含まれていれば一致（飛び飛びでよい、大文字小文字は無視）。
/// 点数は「先頭から続けて一致」「単語の頭」を高くする。"fe" の並びの例：Fetch、Fetch...、Open In File Explorer（F と E が単語の頭）、
/// File History...、Git Flow: Initialize...。同点は元の順。
/// </summary>
public static class FuzzyMatch
{
    /// <summary>一致すれば点数（大きいほど良い）と、太字にする文字の位置。一致しなければ null。</summary>
    public static (int score, int[] positions)? Match(string query, string text)
    {
        if (query.Length == 0) return (0, []);
        var best = Search(query.ToLowerInvariant(), text, text.ToLowerInvariant(), 0, 0, -2, new int[query.Length]);
        return best is { } b ? (b.score, b.positions) : null;
    }

    // 文字列は短い（コマンド名とリポジトリ名）ので、取りうる位置を全部試して一番点の高いものを選ぶ。
    // 長い文字列で爆発しないよう、1 文字ごとの候補は先頭から 8 個まで
    private static (int score, int[] positions)? Search(string q, string text, string lower, int qi, int from, int previous, int[] picked)
    {
        if (qi == q.Length) return (0, (int[])picked.Clone());
        (int score, int[] positions)? best = null;
        var tried = 0;
        for (var i = from; i < lower.Length && tried < 8; i++)
        {
            if (lower[i] != q[qi]) continue;
            tried++;
            picked[qi] = i;
            var rest = Search(q, text, lower, qi + 1, i + 1, i, picked);
            if (rest is null) continue;
            var score = rest.Value.score + CharScore(text, i, previous);
            if (best is null || score > best.Value.score) best = (score, rest.Value.positions);
        }
        return best;
    }

    private static int CharScore(string text, int i, int previous)
    {
        var score = 1;
        if (i == 0) score += 10;
        else if (IsWordStart(text, i)) score += 6;
        if (i == previous + 1) score += 4;
        return score;
    }

    private static bool IsWordStart(string text, int i)
    {
        var p = text[i - 1];
        return !char.IsLetterOrDigit(p) || (char.IsLower(p) && char.IsUpper(text[i]));
    }
}

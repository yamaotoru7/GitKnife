namespace GitKnife.Core.Diff;

/// <summary>行の中で変わった範囲（文字位置と長さ）。</summary>
public readonly record struct TextRange(int Start, int Length);

/// <summary>
/// 削除行と追加行の組から、行内で変わった部分を求める（インラインのトークン単位ハイライト）。
/// トークン（英数字の並び、空白の並び、記号 1 文字）に分けて LCS をとり、一致しなかったトークンをまとめて返す。
/// </summary>
public static class InlineDiff
{
    public static (IReadOnlyList<TextRange> removed, IReadOnlyList<TextRange> added) Compute(string oldLine, string newLine)
    {
        var a = Tokenize(oldLine);
        var b = Tokenize(newLine);
        // 長すぎる行は比較しない（行全体が変わったとみなす）
        if ((long)a.Count * b.Count > 4_000_000) return ([new TextRange(0, oldLine.Length)], [new TextRange(0, newLine.Length)]);

        var lcs = new int[a.Count + 1, b.Count + 1];
        for (var i = a.Count - 1; i >= 0; i--)
            for (var j = b.Count - 1; j >= 0; j--)
                lcs[i, j] = a[i].text == b[j].text ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var keepA = new bool[a.Count];
        var keepB = new bool[b.Count];
        {
            int i = 0, j = 0;
            while (i < a.Count && j < b.Count)
            {
                if (a[i].text == b[j].text) { keepA[i++] = true; keepB[j++] = true; }
                else if (lcs[i + 1, j] >= lcs[i, j + 1]) i++;
                else j++;
            }
        }
        return (Ranges(a, keepA), Ranges(b, keepB));
    }

    private static List<TextRange> Ranges(List<(int start, string text)> tokens, bool[] keep)
    {
        var result = new List<TextRange>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (keep[i]) continue;
            var start = tokens[i].start;
            var end = start + tokens[i].text.Length;
            while (i + 1 < tokens.Count && !keep[i + 1]) { i++; end = tokens[i].start + tokens[i].text.Length; }
            result.Add(new TextRange(start, end - start));
        }
        return result;
    }

    public static List<(int start, string text)> Tokenize(string s)
    {
        var tokens = new List<(int, string)>();
        var i = 0;
        while (i < s.Length)
        {
            var start = i;
            if (char.IsLetterOrDigit(s[i]) || s[i] == '_')
                while (i < s.Length && (char.IsLetterOrDigit(s[i]) || s[i] == '_')) i++;
            else if (char.IsWhiteSpace(s[i]))
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            else i++;
            tokens.Add((start, s[start..i]));
        }
        return tokens;
    }
}

using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Git;

/// <summary>
/// コミット一覧の Ctrl+F。読み込んだ一覧の中だけを探し、git は呼ばない。
/// 大文字小文字を区別せず、件名・本文・作者名・メール・ブランチやタグの名前に含まれるか、SHA がその文字で始まるか。件数は行の数。
/// </summary>
public static class CommitListFind
{
    public static bool Matches(CommitInfo commit, IEnumerable<string> refNames, string query)
    {
        if (query.Length == 0) return false;
        return commit.Sha.StartsWith(query, StringComparison.OrdinalIgnoreCase)
            || Has(commit.Subject) || Has(commit.Body) || Has(commit.AuthorName) || Has(commit.AuthorEmail)
            || refNames.Any(Has);

        bool Has(string s) => s.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 文字を打ったときに選ぶ行：今の行かそれより下で最初の一致。下に無ければ上で一番近い一致。一致が無ければ -1（選択はそのまま）。
    /// <paramref name="matches"/> は一致した行の番号（昇順）。
    /// </summary>
    public static int Start(IReadOnlyList<int> matches, int current)
    {
        if (matches.Count == 0) return -1;
        foreach (var m in matches)
            if (m >= current) return m;
        return matches[^1];
    }

    /// <summary>Enter（次）と Shift+Enter（前）。端では先頭や末尾へ回らず -1（そのまま）。</summary>
    public static int Step(IReadOnlyList<int> matches, int current, bool forward)
    {
        if (forward)
        {
            foreach (var m in matches)
                if (m > current) return m;
        }
        else
        {
            for (var i = matches.Count - 1; i >= 0; i--)
                if (matches[i] < current) return matches[i];
        }
        return -1;
    }

    /// <summary>"0 matches"、"1 match"、"4 matches"（MatchesTextBlock）。</summary>
    public static string CountText(int count) => count == 1 ? "1 match" : $"{count} matches";
}

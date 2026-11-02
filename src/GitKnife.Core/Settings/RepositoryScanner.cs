namespace GitKnife.Core.Settings;

/// <summary>
/// ソースフォルダのスキャン（Repository Manager の "Rescan Repositories..."）。
/// `SourceDirectories` の下を `ScanDepth` 段まで見て、.git（フォルダかファイル）のあるフォルダをリポジトリとし、その下には入らない。
/// 途中のフォルダ名も返す（Repository Manager の木は RepositoryTree がパスから作る。D:\Proj\Apps\libs\Utility → カテゴリ Apps > libs の中に Utility）。
/// 一覧に足すのは RepositoryList.AddScanned（ignore のものは足さない）。
/// </summary>
public static class RepositoryScanner
{
    /// <summary>見つかったリポジトリ（ソースフォルダからの途中のフォルダ名と、リポジトリのパス）。</summary>
    public sealed record Found(IReadOnlyList<string> Folders, string Path);

    public static IReadOnlyList<Found> Scan(string sourceDirectory, int depth)
    {
        var list = new List<Found>();
        Walk(sourceDirectory, [], depth, list);
        return list;
    }

    private static void Walk(string dir, List<string> folders, int depth, List<Found> list)
    {
        IEnumerable<string> children;
        try { children = Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }
        foreach (var child in children)
        {
            var name = System.IO.Path.GetFileName(child);
            if (name.StartsWith('.')) continue;
            var git = System.IO.Path.Combine(child, ".git");
            if (Directory.Exists(git) || File.Exists(git))
            {
                list.Add(new Found([.. folders], child));
                continue;
            }
            if (depth > 1) Walk(child, [.. folders, name], depth - 1, list);
        }
    }
}

namespace GitKnife.Core.Settings;

/// <summary>Repository Manager の木のフォルダ（Key はソースフォルダからの相対パス。根は空）。</summary>
public sealed class RepositoryFolder(string name, string key)
{
    public string Name { get; } = name;
    public string Key { get; } = key;
    public List<RepositoryFolder> Folders { get; } = [];
    public List<RepositoryListEntry> Repositories { get; } = [];

    /// <summary>この下（入れ子も）のリポジトリ全部（名前順）。</summary>
    public IEnumerable<RepositoryListEntry> AllRepositories => Folders.SelectMany(f => f.AllRepositories).Concat(Repositories)
        .OrderBy(r => r.Name, StringComparer.InvariantCultureIgnoreCase);
}

/// <summary>
/// Repository Manager の節 "Repositories" の木を repositories.toml のパスから作る：
/// ソースフォルダの下の途中のフォルダがフォルダになり（D:\Proj\Apps\libs\Utility → Apps > libs）、
/// ソースフォルダの直下と外のリポジトリはフォルダに入らない。フォルダが先、どちらも大文字小文字を区別しない名前順。
/// </summary>
public static class RepositoryTree
{
    public static RepositoryFolder Build(IEnumerable<RepositoryListEntry> repositories, IEnumerable<string> sourceDirectories)
    {
        var sources = sourceDirectories.Select(d => RepositoryList.WithSeparator(d.Replace('/', '\\')))
            .OrderByDescending(d => d.Length).ToList();
        var root = new RepositoryFolder("", "");
        foreach (var r in repositories)
        {
            var path = r.Path.Replace('/', '\\').TrimEnd('\\');
            var source = sources.FirstOrDefault(s => path.StartsWith(s, StringComparison.OrdinalIgnoreCase) && path.Length > s.Length);
            var folder = root;
            if (source is not null)
            {
                var segments = path[source.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries);
                var key = "";
                foreach (var name in segments[..^1])
                {
                    key = key.Length == 0 ? name : key + "\\" + name;
                    var next = folder.Folders.FirstOrDefault(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (next is null) folder.Folders.Add(next = new RepositoryFolder(name, key));
                    folder = next;
                }
            }
            folder.Repositories.Add(r);
        }
        Sort(root);
        return root;
    }

    private static void Sort(RepositoryFolder f)
    {
        f.Folders.Sort((a, b) => StringComparer.InvariantCultureIgnoreCase.Compare(a.Name, b.Name));
        f.Repositories.Sort((a, b) => StringComparer.InvariantCultureIgnoreCase.Compare(a.Name, b.Name));
        foreach (var c in f.Folders) Sort(c);
    }
}

namespace GitKnife.Core.Git;

/// <summary>.gitmodules の 1 項目。</summary>
public sealed record SubmoduleInfo(string Name, string Path, string Url);

/// <summary>
/// .gitmodules を読む（サイドバーの Submodules）。
/// 書式は git config と同じ：[submodule "name"] の下に path = / url =。path の無い項目は飛ばす。
/// </summary>
public static class GitModules
{
    public static IReadOnlyList<SubmoduleInfo> Read(string workTree)
    {
        var file = System.IO.Path.Combine(workTree, ".gitmodules");
        return HostFile.Exists(file) ? Parse(HostFile.ReadAllText(file)) : [];
    }

    public static IReadOnlyList<SubmoduleInfo> Parse(string text)
    {
        var list = new List<SubmoduleInfo>();
        string? name = null, path = null, url = null;
        void Flush()
        {
            if (name is not null && !string.IsNullOrEmpty(path)) list.Add(new SubmoduleInfo(name, path, url ?? ""));
            name = path = url = null;
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';') continue;
            if (line[0] == '[')
            {
                Flush();
                var q1 = line.IndexOf('"');
                var q2 = line.LastIndexOf('"');
                if (line.StartsWith("[submodule", StringComparison.OrdinalIgnoreCase) && q1 >= 0 && q2 > q1) name = line[(q1 + 1)..q2];
                continue;
            }
            if (name is null) continue;
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            if (key.Equals("path", StringComparison.OrdinalIgnoreCase)) path = value;
            else if (key.Equals("url", StringComparison.OrdinalIgnoreCase)) url = value;
        }
        Flush();
        return list;
    }
}

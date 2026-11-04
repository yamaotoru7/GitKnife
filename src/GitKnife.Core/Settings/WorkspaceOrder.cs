namespace GitKnife.Core.Settings;

/// <summary>
/// ワークスペースの並べ替え（ツールバーの Workspaces のメニューでのドラッグ）。
/// 名前の '/' はフォルダで、メニューと同じく同じフォルダの中の項目（ワークスペースかフォルダ）どうしで動かす。
/// 項目のキーはワークスペースならその名前、フォルダなら <see cref="FolderKey"/>（"a/b/" のように末尾に '/'）。
/// </summary>
public static class WorkspaceOrder
{
    /// <summary>名前を区切った部分（空の部分は除く。全部空なら名前そのもの）。メニューのフォルダ分けと同じ。</summary>
    public static string[] Parts(string name)
    {
        var parts = name.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? [name] : parts;
    }

    /// <summary>フォルダのキー（先頭から <paramref name="depth"/> 個の部分を '/' でつなぎ、末尾に '/'）。</summary>
    public static string FolderKey(string[] parts, int depth) => string.Join('/', parts[..depth]) + "/";

    /// <summary>
    /// <paramref name="key"/> の項目を、同じフォルダの中の <paramref name="target"/> の位置へ動かし、<paramref name="all"/> をその順に並べ直す
    /// （フォルダの中身はまとめて並ぶ）。フォルダが違う、見つからない、同じ項目なら何もしないで false。
    /// </summary>
    public static bool Move(List<Workspace> all, string key, string target)
    {
        if (key == target) return false;
        var root = Build(all);
        var from = Find(root, key);
        var to = Find(root, target);
        if (from is null || to is null || from.Value.Parent != to.Value.Parent) return false;
        var siblings = from.Value.Parent.Children;
        var item = siblings[from.Value.Index];
        siblings.RemoveAt(from.Value.Index);
        siblings.Insert(to.Value.Index, item);
        var order = new List<Workspace>(all.Count);
        Flatten(root, order);
        all.Clear();
        all.AddRange(order);
        return true;
    }

    private sealed class Node(string key, Workspace? workspace)
    {
        public string Key { get; } = key;
        public Workspace? Workspace { get; } = workspace;
        public List<Node> Children { get; } = [];
    }

    private static Node Build(List<Workspace> all)
    {
        var root = new Node("", null);
        var folders = new Dictionary<string, Node>(StringComparer.Ordinal);
        foreach (var w in all)
        {
            var parts = Parts(w.Name);
            var host = root;
            for (var i = 1; i < parts.Length; i++)
            {
                var k = FolderKey(parts, i);
                if (!folders.TryGetValue(k, out var folder))
                {
                    folder = new Node(k, null);
                    host.Children.Add(folder);
                    folders[k] = folder;
                }
                host = folder;
            }
            host.Children.Add(new Node(w.Name, w));
        }
        return root;
    }

    private static (Node Parent, int Index)? Find(Node parent, string key)
    {
        for (var i = 0; i < parent.Children.Count; i++)
        {
            var c = parent.Children[i];
            if (c.Key == key) return (parent, i);
            if (c.Workspace is null && Find(c, key) is { } found) return found;
        }
        return null;
    }

    private static void Flatten(Node node, List<Workspace> order)
    {
        foreach (var c in node.Children)
        {
            if (c.Workspace is not null) order.Add(c.Workspace);
            else Flatten(c, order);
        }
    }
}

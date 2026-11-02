using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git.Model;

namespace GitKnife.ViewModels;

/// <summary>競合の案内（"Merge conflict / Select the changes or merge them manually" とカード 2 枚）。</summary>
public sealed partial class ConflictInfo(string path, string theirsName, string oursName, IReadOnlyList<ConflictCommit> ours, IReadOnlyList<ConflictCommit> theirs) : ObservableObject
{
    public string Path { get; } = path;
    public string TheirsName { get; } = theirsName;
    public string OursName { get; } = oursName;
    public IReadOnlyList<ConflictCommit> Ours { get; } = ours;
    public IReadOnlyList<ConflictCommit> Theirs { get; } = theirs;
    /// <summary>
    /// 作業ツリーのファイルに競合の印（&lt;&lt;&lt;&lt;&lt;&lt;&lt; / ======= / &gt;&gt;&gt;&gt;&gt;&gt;&gt;）がもう無い。
    /// "File conflicts are resolved." / "You can stage the file now." と StageButton を出す。
    /// </summary>
    public bool IsResolved { get; init; }
    public bool IsUnresolved => !IsResolved;
    /// <summary>カードのチェック（両方なら Merge、片方だけならその側を使う。片方だけのときの動きは推定）。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResolveText))] private bool _useTheirs = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ResolveText))] private bool _useOurs = true;
    public string ResolveText => (UseTheirs, UseOurs) switch
    {
        (true, false) => $"Resolve Using '{TheirsName}'",
        (false, true) => $"Resolve Using '{OursName}'",
        _ => "Merge",
    };
}

/// <summary>Local Changes のファイル一覧の表示（設定 FileListMode。1 = Tree が既定、0 = List、2 = Combined）。</summary>
public enum FileListMode { List = 0, Tree = 1, Combined = 2 }

/// <summary>
/// ファイル一覧の 1 行。Tree 表示ではフォルダ行も持ち、展開状態に応じて平らな行の並びに展開する。
/// </summary>
public sealed partial class FileListItem : ObservableObject
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public int Level { get; init; }
    public bool IsFolder { get; init; }
    /// <summary>ファイル行の status。フォルダ行は null。</summary>
    public StatusEntry? Entry { get; init; }
    /// <summary>この一覧での変化（Unstaged なら作業ツリー側、Staged なら index 側）。</summary>
    public FileChange Change { get; init; }
    public List<FileListItem> Children { get; } = [];

    /// <summary>UI Automation の行の名前（ListViewItem は DataContext の ToString を名前にする）。パスにして、UI Automation からファイルを選べるようにする。</summary>
    public override string ToString() => Path;

    [ObservableProperty] private bool _isExpanded = true;

    /// <summary>変化がある（File Tree の変わっていないファイルは状態アイコンを出さない）。</summary>
    public bool HasChange => Change != FileChange.Unmodified;

    /// <summary>行に出すアイコンの文字（新規 +、変更 M、削除 D、名前変更 R、競合 !）。</summary>
    public string ChangeLetter => Change switch
    {
        FileChange.Added or FileChange.Untracked or FileChange.Copied => "+",
        FileChange.Deleted => "−",
        FileChange.Renamed => "R",
        FileChange.Conflicted => "!",
        FileChange.TypeChanged => "T",
        FileChange.Ignored => "I",
        _ => "M",
    };

    /// <summary>名前変更なら "旧 → 新" のように元の名前も出す。</summary>
    public string DisplayName => Entry?.OriginalPath is { } orig && !IsFolder ? $"{System.IO.Path.GetFileName(orig)} → {Name}" : Name;

    /// <summary>この行（フォルダなら配下すべて）の status。</summary>
    public IEnumerable<StatusEntry> Entries => IsFolder ? Children.SelectMany(c => c.Entries) : Entry is null ? [] : [Entry];

    /// <summary>
    /// status の一覧から行を作る。staged = true なら index 側の変化を出す。
    /// 並びはフォルダを先に、名前は大文字小文字を区別しない（notes.txt が README.md より前）。
    /// </summary>
    public static List<FileListItem> Build(IEnumerable<StatusEntry> entries, FileListMode mode, bool staged)
    {
        var files = entries.Select(e => (entry: e, change: staged ? e.Index : e.IsConflicted ? FileChange.Conflicted : e.WorkTree)).ToList();
        if (mode != FileListMode.Tree)
        {
            return files.OrderBy(f => f.entry.Path, LowerOrdinal.Instance)
                .Select(f => new FileListItem { Path = f.entry.Path, Name = mode == FileListMode.List ? f.entry.Path : System.IO.Path.GetFileName(f.entry.Path), Entry = f.entry, Change = f.change })
                .ToList();
        }

        var roots = new List<FileListItem>();
        var folders = new Dictionary<string, FileListItem>(StringComparer.Ordinal);
        foreach (var (entry, change) in files)
        {
            var parts = entry.Path.Split('/');
            var siblings = roots;
            var prefix = "";
            for (var i = 0; i < parts.Length - 1; i++)
            {
                prefix = prefix.Length == 0 ? parts[i] : prefix + "/" + parts[i];
                if (!folders.TryGetValue(prefix, out var folder))
                {
                    folder = new FileListItem { Path = prefix, Name = parts[i], Level = i, IsFolder = true };
                    folders[prefix] = folder;
                    siblings.Add(folder);
                }
                siblings = folder.Children;
            }
            siblings.Add(new FileListItem { Path = entry.Path, Name = parts[^1], Level = parts.Length - 1, Entry = entry, Change = change });
        }
        Sort(roots);
        return roots;

        static void Sort(List<FileListItem> list)
        {
            list.Sort((a, b) => a.IsFolder != b.IsFolder ? (a.IsFolder ? -1 : 1) : LowerOrdinal.Instance.Compare(a.Name, b.Name));
            foreach (var f in list) if (f.IsFolder) Sort(f.Children);
        }
    }

    /// <summary>展開されている行だけを上から順に並べる。</summary>
    public static IEnumerable<FileListItem> Flatten(IEnumerable<FileListItem> roots)
    {
        foreach (var r in roots)
        {
            yield return r;
            if (r.IsFolder && r.IsExpanded)
                foreach (var c in Flatten(r.Children)) yield return c;
        }
    }
}

/// <summary>
/// ファイル名の並び：Windows のエクスプローラーと同じ（StrCmpLogicalW：大文字小文字を区別せず、"-" は無視して比べ、数字は数として比べる）。
/// "__astcache" が "forms" より前、"package.json" が "package-lock.json" より前。
/// </summary>
public sealed class LowerOrdinal : IComparer<string>
{
    public static readonly LowerOrdinal Instance = new();
    public int Compare(string? x, string? y) => StrCmpLogicalW(x ?? "", y ?? "");

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string psz1, string psz2);
}

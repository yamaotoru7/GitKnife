namespace GitKnife.Core.Settings;

/// <summary>repositories.toml の `[[repository]]` 1 つ。Name は alias、無ければフォルダ名。</summary>
public sealed record RepositoryListEntry(string Path, string? Alias, long? Opened, string? Color)
{
    public string Name => Alias is { Length: > 0 } a ? a : FolderName(Path);

    public static string FolderName(string path) => System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
}

/// <summary>
/// Repository Manager の一覧とリポジトリごとの情報（%LocalAppData%\GitKnife\repositories.toml）。
/// 先頭に source_dirs / scan_depth / ignore、続いて `[[repository]]` の表（path、opened、alias、color）。どの操作もすぐ書く。
/// 一覧の実体はこちらで、settings.json の RepositoryManager は古い版の名残（T110）。
/// - color：タブの色 "Red" / "Orange" / "Yellow" / "Green" / "Blue" / "Violet"
/// - alias：Repository Manager の Rename
/// - Remove：表を消してパスを ignore の末尾に足す。ignore のものは Rescan で足し直さない
/// - opened：開いた時刻（Unix 秒）。Quick Launch の Recent の順
/// パスはリテラル文字列（'…'）で書く。
/// </summary>
public sealed class RepositoryList
{
    public static readonly string[] Colors = ["Red", "Orange", "Yellow", "Green", "Blue", "Violet"];

    private readonly string _path;
    private readonly TomlDocument _doc;

    private RepositoryList(string path, TomlDocument doc) { _path = path; _doc = doc; }

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductInfo.Name, "repositories.toml");

    public static bool Exists(string? path = null) => File.Exists(path ?? DefaultPath);

    public static RepositoryList Load(string? path = null)
    {
        path ??= DefaultPath;
        return new RepositoryList(path, File.Exists(path) ? TomlDocument.Load(path) : TomlDocument.Parse(""));
    }

    /// <summary>
    /// repositories.toml がまだ無いとき、settings.json の RepositoryManager（GitKnife の前の版が使っていた形）から作る。
    /// ソースフォルダは末尾に "\" を付ける（settings.json の "D:\Proj" は toml では 'D:\Proj\'）。
    /// </summary>
    public static void CreateFrom(RepositoryManagerSettings settings, string? path = null)
    {
        path ??= DefaultPath;
        if (File.Exists(path)) return;
        var list = new RepositoryList(path, TomlDocument.Parse(""));
        list._doc.Set("source_dirs", new TomlLiteralList(settings.SourceDirectories.Select(WithSeparator)));
        list._doc.Set("scan_depth", (long)(settings.ScanDepth > 0 ? settings.ScanDepth : 5));
        list._doc.Set("ignore", new List<string>());
        var tables = settings.Repositories.Where(r => r.Path.Length > 0).Select(r =>
        {
            var t = new TomlTable().Set("path", TomlLiteral.Of(r.Path));
            if (r.Name.Length > 0 && r.Name != RepositoryListEntry.FolderName(r.Path)) t.Set("alias", r.Name);
            return t;
        }).ToList();
        list._doc.Set("repository", tables);
        list.Save();
    }

    // ------------------------------------------------------------------ 先頭のキー

    /// <summary>Preferences の Source Code Folder（source_dirs。末尾に "\" を付けて書き、そのまま出す）。</summary>
    public IReadOnlyList<string> SourceDirectories
    {
        get => _doc.GetStringArray("source_dirs");
        set { SetHead("source_dirs", new TomlLiteralList(value.Select(WithSeparator))); Save(); }
    }

    public int ScanDepth => (int)_doc.GetInt("scan_depth", 5);

    public IReadOnlyList<string> Ignore => _doc.GetStringArray("ignore");

    // 先頭のキーは source_dirs、scan_depth、ignore の順。無ければ表の前に足す
    private void SetHead(string key, object value)
    {
        if (_doc.Has(key)) { _doc.Set(key, value); return; }
        var tables = _doc.GetTables("repository");
        _doc.Remove("repository");
        _doc.Set(key, value);
        if (!_doc.Has("scan_depth")) _doc.Set("scan_depth", 5L);
        if (!_doc.Has("ignore")) _doc.Set("ignore", new List<string>());
        _doc.Set("repository", tables.ToList());
    }

    public static string WithSeparator(string dir) => dir.EndsWith('\\') || dir.EndsWith('/') ? dir : dir + (dir.Contains('/') && !dir.Contains('\\') ? "/" : "\\");

    // ------------------------------------------------------------------ リポジトリ

    public IReadOnlyList<RepositoryListEntry> Repositories => _doc.GetTables("repository")
        .Where(t => t.GetString("path") is { Length: > 0 })
        .Select(t => new RepositoryListEntry(t.GetString("path")!, t.GetString("alias"), t.GetInt("opened"), t.GetString("color")))
        .ToList();

    public RepositoryListEntry? Get(string repositoryPath) => Find(repositoryPath) is { } t
        ? new RepositoryListEntry(t.GetString("path")!, t.GetString("alias"), t.GetInt("opened"), t.GetString("color")) : null;

    public string? GetColor(string repositoryPath) => Find(repositoryPath)?.GetString("color");

    /// <summary>一覧に足す（あれば何もしない）。ignore からは外さない（ignore にあっても開けば表を作る）。足したら true。</summary>
    public bool Add(string repositoryPath)
    {
        if (Find(repositoryPath) is not null) return false;
        Edit(repositoryPath, _ => { });
        return true;
    }

    /// <summary>色を付ける（null で消す）。</summary>
    public void SetColor(string repositoryPath, string? color)
    {
        if (color is null && Find(repositoryPath) is null) return;
        Edit(repositoryPath, t => { if (color is null) t.Remove("color"); else t.Set("color", color); });
    }

    /// <summary>名前を変える（alias。フォルダ名と同じか空なら消す）。</summary>
    public void SetAlias(string repositoryPath, string? alias)
    {
        var name = alias?.Trim();
        var clear = string.IsNullOrEmpty(name) || name == RepositoryListEntry.FolderName(repositoryPath);
        if (clear && Find(repositoryPath) is null) return;
        // 並びは path、alias、opened
        Edit(repositoryPath, t => { if (clear) t.Remove("alias"); else t.SetAfter("alias", name!, "path"); });
    }

    /// <summary>Remove：表を消し、パスを ignore の末尾に足す（ディスクのフォルダはそのまま）。</summary>
    public void Remove(IEnumerable<string> repositoryPaths)
    {
        var keys = repositoryPaths.Select(Normalize).ToHashSet();
        if (keys.Count == 0) return;
        var tables = _doc.GetTables("repository").ToList();
        var removed = tables.Where(t => t.GetString("path") is { } p && keys.Contains(Normalize(p))).ToList();
        tables.RemoveAll(removed.Contains);
        var ignore = Ignore.ToList();
        foreach (var p in removed.Select(t => t.GetString("path")!))
            if (!ignore.Any(i => Normalize(i) == Normalize(p))) ignore.Add(p);
        SetHead("ignore", new TomlLiteralList(ignore));
        _doc.Set("repository", tables);
        Save();
    }

    /// <summary>
    /// Rescan の結果を足す：ignore に無く、まだ一覧に無いものを末尾に。resetDeleted（"Reset previously deleted repository entries"）なら先に ignore を空にする。
    /// 足した数を返す。
    /// </summary>
    public int AddScanned(IEnumerable<string> found, bool resetDeleted)
    {
        if (resetDeleted) SetHead("ignore", new List<string>());
        var ignore = Ignore.Select(Normalize).ToHashSet();
        var tables = _doc.GetTables("repository").ToList();
        var known = tables.Select(t => t.GetString("path")).Where(p => p is not null).Select(p => Normalize(p!)).ToHashSet();
        var added = 0;
        foreach (var p in found)
        {
            var key = Normalize(p);
            if (ignore.Contains(key) || !known.Add(key)) continue;
            tables.Add(new TomlTable().Set("path", TomlLiteral.Of(p)));
            added++;
        }
        _doc.Set("repository", tables);
        Save();
        return added;
    }

    // このプロセスで記録した順（opened は秒単位なので、同じ秒のものの順を覚えておく）
    private static readonly Dictionary<string, long> Touched = [];
    private static long _touchCount;

    /// <summary>
    /// 開いた（起動時のタブの復元も）：opened を今（Unix 秒）にする。Quick Launch の Recent Repositories はこの新しい順
    /// （タブを選ぶだけでは変わらない）。同じリポジトリを記録し直すと、同じ秒の中での順は後ろになる。
    /// 落ちても残るようにすぐ書く。
    /// </summary>
    public void Touch(string repositoryPath, DateTimeOffset? now = null)
    {
        Edit(repositoryPath, t => t.SetAfter("opened", (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(), "path", "alias"));
        lock (Touched) Touched[Normalize(repositoryPath)] = ++_touchCount;
    }

    /// <summary>
    /// opened の新しい順のパス。同じ秒ならこのプロセスで先に記録した方が先（再起動直後は同じ秒のものが復元した順に並ぶ）、
    /// 記録していないものはその後ろにファイルの順。
    /// </summary>
    public IReadOnlyList<string> Recent(int count)
    {
        var rows = _doc.GetTables("repository")
            .Select((t, i) => (path: t.GetString("path"), opened: t.GetInt("opened"), index: i))
            .Where(r => r.path is not null && r.opened is not null)
            .ToList();
        long Order(string p) { lock (Touched) return Touched.TryGetValue(Normalize(p), out var n) ? n : long.MaxValue; }
        return rows.OrderByDescending(r => r.opened).ThenBy(r => Order(r.path!)).ThenBy(r => r.index)
            .Select(r => r.path!).Take(count).ToList();
    }

    // ------------------------------------------------------------------

    /// <summary>表を探して（無ければ末尾に作って）change し、書く。</summary>
    private void Edit(string repositoryPath, Action<TomlTable> change)
    {
        var tables = _doc.GetTables("repository").ToList();
        var t = Find(repositoryPath);
        if (t is null)
        {
            t = new TomlTable().Set("path", TomlLiteral.Of(repositoryPath));
            tables.Add(t);
        }
        change(t);
        _doc.Set("repository", tables);
        Save();
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _doc.Save(_path);
    }

    private TomlTable? Find(string repositoryPath)
    {
        var key = Normalize(repositoryPath);
        return _doc.GetTables("repository").FirstOrDefault(t => t.GetString("path") is { } p && Normalize(p) == key);
    }

    public static string Normalize(string p) => p.Replace('/', '\\').TrimEnd('\\').ToUpperInvariant();
}

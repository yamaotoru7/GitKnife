using System.Globalization;
using System.Text;

namespace GitKnife.Core.Settings;

/// <summary>
/// `.git/gitknife/*.toml` 用の小さな TOML（`key = value` と `[[name]]` のテーブルの配列。値は文字列、真偽値、整数、文字列配列）。
/// 普通のテーブルや入れ子は扱わない。
/// 知らないキーも順序どおりに保持して書き戻す。
/// </summary>
public sealed class TomlDocument
{
    private readonly List<(string key, object value)> _entries = [];

    public IReadOnlyList<(string key, object value)> Entries => _entries;

    public bool Has(string key) => _entries.Any(e => e.key == key);

    public string? GetString(string key) => TomlLiteral.AsString(Get(key));
    public bool GetBool(string key, bool fallback) => Get(key) is bool b ? b : fallback;
    public long GetInt(string key, long fallback) => Get(key) is long l ? l : fallback;
    public IReadOnlyList<string> GetStringArray(string key) => Get(key) as IReadOnlyList<string> ?? [];

    /// <summary>キーを消す（無ければ何もしない）。</summary>
    public void Remove(string key) => _entries.RemoveAll(e => e.key == key);

    /// <summary>キーを after の直後に足す（既にあれば値だけ替える。after が無ければ末尾）。キーの順序を決まった形にするため。</summary>
    public void SetAfter(string key, object value, string after)
    {
        if (Has(key)) { Set(key, value); return; }
        var i = _entries.FindIndex(e => e.key == after);
        if (i < 0) Set(key, value); else _entries.Insert(i + 1, (key, value));
    }

    public void Set(string key, object value)
    {
        if (value is string[] arr) value = (IReadOnlyList<string>)arr.ToList();
        var i = _entries.FindIndex(e => e.key == key);
        if (i >= 0) _entries[i] = (key, value); else _entries.Add((key, value));
    }

    private object? Get(string key)
    {
        var i = _entries.FindIndex(e => e.key == key);
        return i >= 0 ? _entries[i].value : null;
    }

    /// <summary>テーブルの配列（`[[recent_revision_search_queries]]`）。空なら `key = []` と書く。</summary>
    public IReadOnlyList<TomlTable> GetTables(string key) => Get(key) as IReadOnlyList<TomlTable> ?? [];

    public static TomlDocument Parse(string text)
    {
        var doc = new TomlDocument();
        TomlTable? table = null;   // [[name]] の後はそのテーブルのキー
        var lines = text.Split('\n');
        for (var n = 0; n < lines.Length; n++)
        {
            var line = lines[n].Trim();
            // 複数行の文字列（key = """ … """）。開きの直後の改行は捨て、閉じまでの行をそのままつなぐ
            var multi = line.IndexOf("= \"\"\"", StringComparison.Ordinal) is var mi and >= 0 ? mi : line.IndexOf("=\"\"\"", StringComparison.Ordinal);
            if (multi >= 0 && line[(line.IndexOf("\"\"\"", StringComparison.Ordinal) + 3)..] is var rest && !rest.Contains("\"\"\"", StringComparison.Ordinal))
            {
                var mkey = line[..line.IndexOf('=')].Trim();
                var sb = new StringBuilder(rest);
                var first = rest.Length == 0;
                while (++n < lines.Length)
                {
                    var l = lines[n].TrimEnd('\r');
                    var close = l.IndexOf("\"\"\"", StringComparison.Ordinal);
                    if (!first || sb.Length > 0) sb.Append('\n');
                    first = false;
                    if (close >= 0) { sb.Append(l[..close]); break; }
                    sb.Append(l);
                }
                var mvalue = Unescape(sb.ToString());
                if (table is not null) table.Set(mkey, mvalue); else doc._entries.Add((mkey, mvalue));
                continue;
            }
            if (line.Length == 0 || line[0] == '#') continue;
            if (line.StartsWith("[[", StringComparison.Ordinal) && line.EndsWith("]]", StringComparison.Ordinal))
            {
                var name = line[2..^2].Trim();
                var list = doc.Get(name) as List<TomlTable>;
                if (list is null) { list = []; doc.Set(name, list); }
                list.Add(table = new TomlTable());
                continue;
            }
            if (line[0] == '[') { table = null; continue; }   // 普通のテーブルは扱わない
            var eq = line.IndexOf('=');
            if (eq < 0) continue;
            var key = line[..eq].Trim();
            var value = ParseValue(line[(eq + 1)..].Trim());
            if (table is not null) table.Set(key, value); else doc._entries.Add((key, value));
        }
        return doc;
    }

    public static TomlDocument Load(string path)
        => Git.HostFile.Exists(path) ? Parse(Git.HostFile.ReadAllText(path, Encoding.UTF8)) : new TomlDocument();

    public void Save(string path)
    {
        Git.HostDirectory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        Git.HostFile.WriteAllText(tmp, ToString(), new UTF8Encoding(false));
        Git.HostFile.Move(tmp, path, overwrite: true);
    }

    public override string ToString()
    {
        // 平らなキーを先に、空でないテーブルの配列は最後に（空行で区切る）
        var sb = new StringBuilder();
        foreach (var (key, value) in _entries)
            if (value is not IReadOnlyList<TomlTable> { Count: > 0 }) sb.Append(key).Append(" = ").Append(value is IReadOnlyList<TomlTable> ? "[]" : Format(value)).Append('\n');
        foreach (var (key, value) in _entries)
        {
            if (value is not IReadOnlyList<TomlTable> { Count: > 0 } tables) continue;
            foreach (var t in tables)
            {
                sb.Append('\n').Append("[[").Append(key).Append("]]\n");
                foreach (var (k, v) in t.Entries) sb.Append(k).Append(" = ").Append(Format(v)).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static object ParseValue(string v)
    {
        if (v == "true") return true;
        if (v == "false") return false;
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"') return Unescape(v[1..^1]);
        // リテラル文字列（'D:\Proj\'。repositories.toml のパス）。エスケープは無い。書き戻すときも '…' にする
        if (v.Length >= 2 && v[0] == '\'' && v[^1] == '\'') return new TomlLiteral(v[1..^1]);
        if (v.Length >= 2 && v[0] == '[' && v[^1] == ']')
        {
            var inner = v[1..^1].Trim();
            if (inner.Length == 0) return (IReadOnlyList<string>)new List<string>();
            var items = new List<string>();
            var literal = true;   // 全部 '…' なら書き戻すときも '…'（repositories.toml の source_dirs / ignore）
            var sb = new StringBuilder();
            var quote = '\0';   // 中にいる文字列の引用符（'"' か '\''。'\0' は外）
            for (var i = 0; i < inner.Length; i++)
            {
                var c = inner[i];
                if (quote == '"')
                {
                    if (c == '\\' && i + 1 < inner.Length) { sb.Append(c).Append(inner[++i]); continue; }
                    if (c == '"') { quote = '\0'; items.Add(Unescape(sb.ToString())); sb.Clear(); continue; }
                    sb.Append(c);
                }
                else if (quote == '\'')
                {
                    if (c == '\'') { quote = '\0'; items.Add(sb.ToString()); sb.Clear(); continue; }
                    sb.Append(c);
                }
                else if (c is '"' or '\'') { quote = c; literal &= c == '\''; }
            }
            return literal && items.Count > 0 ? new TomlLiteralList(items) : (IReadOnlyList<string>)items;
        }
        if (long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
        return v;   // 知らない形はそのまま文字列として保持（書き戻しも生のまま）
    }

    private static string Format(object value) => value switch
    {
        bool b => b ? "true" : "false",
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        // 改行を含む文字列は複数行の文字列（draft_message など：`"""` の直後で改行し、中の改行はそのまま）
        string s when s.Contains('\n') => "\"\"\"\n" + s.Replace("\r", "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\t", "\\t") + "\"\"\"",
        string s => '"' + Escape(s) + '"',
        TomlLiteral lit => "'" + lit.Value + "'",
        TomlLiteralList list => "[" + string.Join(", ", list.Select(x => "'" + x + "'")) + "]",
        IReadOnlyList<string> list => "[" + string.Join(", ", list.Select(x => '"' + Escape(x) + '"')) + "]",
        _ => value.ToString() ?? "",
    };

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");

    private static string Unescape(string s)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
            i++;
            sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', 'r' => '\r', '"' => '"', '\\' => '\\', var c => c });
        }
        return sb.ToString();
    }
}

/// <summary>テーブルの配列の 1 つ（キーの順序を保つ）。</summary>
public sealed class TomlTable
{
    private readonly List<(string key, object value)> _entries = [];
    public IReadOnlyList<(string key, object value)> Entries => _entries;
    public string? GetString(string key) => TomlLiteral.AsString(_entries.FirstOrDefault(e => e.key == key).value);
    public long? GetInt(string key) => _entries.FirstOrDefault(e => e.key == key).value as long?;

    public TomlTable Set(string key, object value)
    {
        var i = _entries.FindIndex(e => e.key == key);
        if (i >= 0) _entries[i] = (key, value); else _entries.Add((key, value));
        return this;
    }

    /// <summary>あれば値だけ替え、無ければ after のキーのうちいちばん後ろにあるものの直後に足す（どれも無ければ末尾）。キーの順序を決まった形にするため。</summary>
    public TomlTable SetAfter(string key, object value, params string[] after)
    {
        if (_entries.Any(e => e.key == key)) return Set(key, value);
        var i = after.Select(a => _entries.FindIndex(e => e.key == a)).DefaultIfEmpty(-1).Max();
        if (i < 0) _entries.Add((key, value)); else _entries.Insert(i + 1, (key, value));
        return this;
    }

    public TomlTable Remove(string key)
    {
        _entries.RemoveAll(e => e.key == key);
        return this;
    }
}

/// <summary>
/// リテラル文字列（'…'）。中身はエスケープしない。repositories.toml はパスをこの形で書くので、読んだものは同じ形で書き戻し、
/// GitKnife が書くパスもこの形にする（' を含むパスは書けないので、そのときは呼ぶ側が普通の文字列にする）。
/// </summary>
public sealed record TomlLiteral(string Value)
{
    public static string? AsString(object? value) => value switch { string s => s, TomlLiteral l => l.Value, _ => null };

    /// <summary>パスを書く形：' を含まなければリテラル、含めば普通の文字列。</summary>
    public static object Of(string value) => value.Contains('\'') ? value : new TomlLiteral(value);
}

/// <summary>全部リテラル文字列の配列（['D:\Proj\']）。</summary>
public sealed class TomlLiteralList(IEnumerable<string> items) : List<string>(items);

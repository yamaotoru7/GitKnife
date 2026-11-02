using System.Text.RegularExpressions;

namespace GitKnife.Core.Diff;

public enum DiffLineKind { Context, Added, Removed, NoNewlineAtEnd }

/// <summary>diff の 1 行。旧／新の行番号（無い側は 0）。Text は CRLF のファイルなら末尾に \r が残る（表示には <see cref="DisplayText"/>）。</summary>
public sealed record DiffLine(DiffLineKind Kind, string Text, int OldNumber, int NewNumber)
{
    public string DisplayText => Text.EndsWith('\r') ? Text[..^1] : Text;
}

/// <summary>ハンク（@@ -a,b +c,d @@ 見出し）。</summary>
public sealed class DiffHunk
{
    public required int OldStart { get; init; }
    public required int OldCount { get; init; }
    public required int NewStart { get; init; }
    public required int NewCount { get; init; }
    /// <summary>@@ の後ろに続く関数名などの文字列。</summary>
    public string Section { get; init; } = "";
    /// <summary>元のヘッダ行（部分ステージのパッチを組み立てるときに使う）。</summary>
    public required string Header { get; init; }
    /// <summary>
    /// 画面に出す見出し。行数を省かずに書く（git は 1 行のとき "+1" と省くが、"@@ -0,0 +1,1 @@" と出す）。
    /// 後ろの関数名などはそのまま付ける。
    /// </summary>
    public string DisplayHeader => $"@@ -{OldStart},{OldCount} +{NewStart},{NewCount} @@" + (Section.Length > 0 ? " " + Section : "");
    public List<DiffLine> Lines { get; } = [];
}

/// <summary>1 ファイルぶんの diff。</summary>
public sealed class FileDiff
{
    public string? OldPath { get; set; }
    public string? NewPath { get; set; }
    public string? OldMode { get; set; }
    public string? NewMode { get; set; }
    public bool IsNew { get; set; }
    public bool IsDeleted { get; set; }
    public bool IsRename { get; set; }
    public int? Similarity { get; set; }
    public bool IsBinary { get; set; }
    public string? OldIndex { get; set; }
    public string? NewIndex { get; set; }
    /// <summary>diff --git から hunk の前までの行（部分ステージ用）。</summary>
    public List<string> HeaderLines { get; } = [];
    public List<DiffHunk> Hunks { get; } = [];
    public string Path => NewPath ?? OldPath ?? "";
    public bool ModeChanged => OldMode is not null && NewMode is not null && OldMode != NewMode;
    public int Additions => Hunks.Sum(h => h.Lines.Count(l => l.Kind == DiffLineKind.Added));
    public int Deletions => Hunks.Sum(h => h.Lines.Count(l => l.Kind == DiffLineKind.Removed));
}

/// <summary>
/// `git diff` の unified diff を読む。`--src-prefix=gkSrcPrefix/ --dst-prefix=gkDstPrefix/` も、既定の a/ b/ も受け付ける。
/// パスにクォート（"..."、\ エスケープ）があっても戻す。
/// </summary>
public static partial class UnifiedDiffParser
{
    [GeneratedRegex(@"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@ ?(.*)$")]
    private static partial Regex HunkHeader();

    private static readonly string[] Prefixes = ["gkSrcPrefix/", "gkDstPrefix/", "a/", "b/"];

    public static IReadOnlyList<FileDiff> Parse(string text)
    {
        var files = new List<FileDiff>();
        FileDiff? file = null;
        DiffHunk? hunk = null;
        int oldLine = 0, newLine = 0;
        // 本文の行末の \r は残す（CRLF のファイルを部分ステージするとき、パッチを元のバイトどおりに組み立てるため）。見出し行だけ落とす
        var lines = text.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = raw.TrimEnd('\r');
            if (line.StartsWith("diff --git ", StringComparison.Ordinal))
            {
                file = new FileDiff();
                files.Add(file);
                hunk = null;
                file.HeaderLines.Add(line);
                ParseDiffGitPaths(line["diff --git ".Length..], file);
                continue;
            }
            if (file is null) continue;

            if (hunk is null || !(line.Length > 0 && line[0] is ' ' or '+' or '-' or '\\'))
            {
                var m = HunkHeader().Match(line);
                if (m.Success)
                {
                    hunk = new DiffHunk
                    {
                        OldStart = int.Parse(m.Groups[1].Value),
                        OldCount = m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 1,
                        NewStart = int.Parse(m.Groups[3].Value),
                        NewCount = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 1,
                        Section = m.Groups[5].Value,
                        Header = line,
                    };
                    file.Hunks.Add(hunk);
                    oldLine = hunk.OldStart;
                    newLine = hunk.NewStart;
                    continue;
                }
                if (hunk is null) { ParseHeaderLine(line, file); continue; }
                // ハンクの後の空行（ファイル末尾）
                if (line.Length == 0) continue;
                hunk = null;
                ParseHeaderLine(line, file);
                continue;
            }

            switch (line[0])
            {
                case ' ': hunk.Lines.Add(new DiffLine(DiffLineKind.Context, raw[1..], oldLine++, newLine++)); break;
                case '+': hunk.Lines.Add(new DiffLine(DiffLineKind.Added, raw[1..], 0, newLine++)); break;
                case '-': hunk.Lines.Add(new DiffLine(DiffLineKind.Removed, raw[1..], oldLine++, 0)); break;
                case '\\': hunk.Lines.Add(new DiffLine(DiffLineKind.NoNewlineAtEnd, line, 0, 0)); break;
            }
        }
        return files;
    }

    private static void ParseHeaderLine(string line, FileDiff file)
    {
        file.HeaderLines.Add(line);
        if (line.StartsWith("new file mode ", StringComparison.Ordinal)) { file.IsNew = true; file.NewMode = line[14..]; }
        else if (line.StartsWith("deleted file mode ", StringComparison.Ordinal)) { file.IsDeleted = true; file.OldMode = line[18..]; }
        else if (line.StartsWith("old mode ", StringComparison.Ordinal)) file.OldMode = line[9..];
        else if (line.StartsWith("new mode ", StringComparison.Ordinal)) file.NewMode = line[9..];
        else if (line.StartsWith("similarity index ", StringComparison.Ordinal)) file.Similarity = int.Parse(line[17..].TrimEnd('%'));
        else if (line.StartsWith("rename from ", StringComparison.Ordinal)) { file.IsRename = true; file.OldPath = Unquote(line[12..]); }
        else if (line.StartsWith("rename to ", StringComparison.Ordinal)) { file.IsRename = true; file.NewPath = Unquote(line[10..]); }
        else if (line.StartsWith("Binary files ", StringComparison.Ordinal) || line == "GIT binary patch") file.IsBinary = true;
        else if (line.StartsWith("index ", StringComparison.Ordinal))
        {
            var parts = line[6..].Split(' ');
            var ids = parts[0].Split("..");
            if (ids.Length == 2) { file.OldIndex = ids[0]; file.NewIndex = ids[1]; }
            if (parts.Length > 1) file.OldMode ??= parts[1];
            if (parts.Length > 1) file.NewMode ??= parts[1];
        }
        else if (line.StartsWith("--- ", StringComparison.Ordinal))
        {
            var p = line[4..];
            if (p == "/dev/null") file.IsNew = true; else file.OldPath = StripPrefix(Unquote(p));
        }
        else if (line.StartsWith("+++ ", StringComparison.Ordinal))
        {
            var p = line[4..];
            if (p == "/dev/null") file.IsDeleted = true; else file.NewPath = StripPrefix(Unquote(p));
        }
    }

    private static void ParseDiffGitPaths(string rest, FileDiff file)
    {
        // "gkSrcPrefix/a b.txt gkDstPrefix/a b.txt" や "\"a/x\" \"b/x\""。--- / +++ / rename 行で上書きされるので、ここは目安
        if (rest.StartsWith('"'))
        {
            var end = FindClosingQuote(rest, 0);
            file.OldPath = StripPrefix(Unquote(rest[..(end + 1)]));
            var second = rest[(end + 1)..].Trim();
            file.NewPath = StripPrefix(Unquote(second));
            return;
        }
        foreach (var dst in new[] { " gkDstPrefix/", " b/" })
        {
            var idx = rest.IndexOf(dst, StringComparison.Ordinal);
            if (idx < 0) continue;
            file.OldPath = StripPrefix(rest[..idx]);
            file.NewPath = StripPrefix(rest[(idx + 1)..]);
            return;
        }
    }

    private static int FindClosingQuote(string s, int start)
    {
        for (var i = start + 1; i < s.Length; i++)
        {
            if (s[i] == '\\') { i++; continue; }
            if (s[i] == '"') return i;
        }
        return s.Length - 1;
    }

    private static string StripPrefix(string p)
    {
        foreach (var pre in Prefixes) if (p.StartsWith(pre, StringComparison.Ordinal)) return p[pre.Length..];
        return p;
    }

    /// <summary>git のクォート（"..." と \t \n \" \\ \ooo の 8 進 UTF-8 バイト）を戻す。</summary>
    public static string Unquote(string s)
    {
        s = s.TrimEnd('\t');   // "--- a/x\t" の末尾タブ
        if (s.Length < 2 || s[0] != '"' || s[^1] != '"') return s;
        var bytes = new List<byte>();
        for (var i = 1; i < s.Length - 1; i++)
        {
            var c = s[i];
            if (c != '\\') { bytes.AddRange(System.Text.Encoding.UTF8.GetBytes(c.ToString())); continue; }
            var n = s[++i];
            switch (n)
            {
                case 't': bytes.Add((byte)'\t'); break;
                case 'n': bytes.Add((byte)'\n'); break;
                case 'r': bytes.Add((byte)'\r'); break;
                case '"': bytes.Add((byte)'"'); break;
                case '\\': bytes.Add((byte)'\\'); break;
                case 'a': bytes.Add(7); break;
                case 'b': bytes.Add(8); break;
                case 'f': bytes.Add(12); break;
                case 'v': bytes.Add(11); break;
                default:
                    if (n is >= '0' and <= '7' && i + 2 < s.Length)
                    {
                        bytes.Add(Convert.ToByte(s.Substring(i, 3), 8));
                        i += 2;
                    }
                    else bytes.Add((byte)n);
                    break;
            }
        }
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }
}

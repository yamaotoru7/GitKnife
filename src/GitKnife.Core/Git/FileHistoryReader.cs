using GitKnife.Core.Diff;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Git;

/// <summary>File History の 1 行（コミットと、そのコミットでのファイルの変化。名前を変えたなら古い名前も）。</summary>
public sealed record FileHistoryEntry(string Sha, string Author, string Email, DateTimeOffset Date, string Subject, FileChange Change, string Path, string? OldPath)
{
    public string ShortSha => Sha[..Math.Min(7, Sha.Length)];
}

/// <summary>フォルダの History の 1 コミット（そのフォルダの中で変わったファイル全部）。</summary>
public sealed record FolderHistoryCommit(string Sha, string Author, string Email, DateTimeOffset Date, string Subject, IReadOnlyList<FileHistoryEntry> Files)
{
    public string ShortSha => Sha[..Math.Min(7, Sha.Length)];
}

/// <summary>Blame の 1 行（その行を最後に変えたコミット）。</summary>
public sealed record BlameLine(string Sha, string Author, string Email, DateTimeOffset Time, string Summary, int FinalLine, string Text)
{
    public string ShortSha => Sha[..Math.Min(7, Sha.Length)];
}

/// <summary>
/// File History と Blame の読み取り。
/// コマンド：`log --oneline --name-status --follow --pretty=format:%H%n%aN%n%aE%n%ct%n%s -- path`、
/// コミットごとに `diff &lt;sha&gt;~1 &lt;sha&gt; --unified=19 -- path`、Blame は `blame --porcelain &lt;rev&gt; -- path`。
/// </summary>
public sealed class FileHistoryReader(GitProcess git, string workingDirectory)
{
    private Task<GitResult> Run(string[] args, CancellationToken ct) => git.RunAsync(new GitRequest(workingDirectory, args), ct);

    public async Task<IReadOnlyList<FileHistoryEntry>> ReadHistoryAsync(string path, CancellationToken ct = default)
    {
        // 今のブランチの upstream があればそこからも探す（upstream だけにある削除のコミットも出す）
        var upstream = await Run(["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], ct).ConfigureAwait(false);
        var r = await Run(GitArgs.FileHistory(path, upstream.Succeeded ? upstream.StandardOutput.Trim() : null), ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return ParseHistory(r.StandardOutput);
    }

    /// <summary>
    /// フォルダの History：そのフォルダの中を変えたコミットごとに、変わったファイル全部（src を開くと "Greet the world" の下に src/app.cs）。起点はファイルと同じく HEAD と upstream。
    /// </summary>
    public async Task<IReadOnlyList<FolderHistoryCommit>> ReadFolderHistoryAsync(string path, CancellationToken ct = default)
    {
        var upstream = await Run(["rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}"], ct).ConfigureAwait(false);
        var r = await Run(GitArgs.FolderHistory(path, upstream.Succeeded ? upstream.StandardOutput.Trim() : null), ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return ParseFolderHistory(r.StandardOutput);
    }

    /// <summary>ParseHistory と同じ形の出力を、コミットごとにファイルをまとめて読む（name-status の行が複数）。</summary>
    public static IReadOnlyList<FolderHistoryCommit> ParseFolderHistory(string output)
    {
        var list = new List<FolderHistoryCommit>();
        var lines = output.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            if (lines[i].Length == 0) { i++; continue; }
            if (i + 4 >= lines.Length) break;
            var (sha, author, email, subject) = (lines[i], lines[i + 1], lines[i + 2], lines[i + 4]);
            var date = long.TryParse(lines[i + 3], out var t) ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime() : default;
            i += 5;
            var files = new List<FileHistoryEntry>();
            while (i < lines.Length && lines[i].Length > 0 && lines[i].Contains('\t'))
            {
                var parts = lines[i].Split('\t');
                var change = ChangeOf(parts[0]);
                var (path, oldPath) = parts.Length >= 3 ? (parts[2], parts[1]) : (parts[^1], null);
                files.Add(new FileHistoryEntry(sha, author, email, date, subject, change, path, change is FileChange.Renamed or FileChange.Copied ? oldPath : null));
                i++;
            }
            list.Add(new FolderHistoryCommit(sha, author, email, date, subject, files));
        }
        return list;
    }

    private static FileChange ChangeOf(string status) => status.Length == 0 ? FileChange.Modified : status[0] switch
    {
        'A' => FileChange.Added,
        'D' => FileChange.Deleted,
        'R' => FileChange.Renamed,
        'C' => FileChange.Copied,
        'T' => FileChange.TypeChanged,
        _ => FileChange.Modified,
    };

    /// <summary>
    /// `log --name-status --pretty=format:%H%n%aN%n%aE%n%ct%n%s` の出力を読む。コミットごとに 5 行、続いて "M\tpath"（名前の変更は "R100\told\tnew"）、空行で区切り。
    /// </summary>
    public static IReadOnlyList<FileHistoryEntry> ParseHistory(string output)
    {
        var list = new List<FileHistoryEntry>();
        var lines = output.Replace("\r\n", "\n").Split('\n');
        var i = 0;
        while (i < lines.Length)
        {
            if (lines[i].Length == 0) { i++; continue; }
            if (i + 4 >= lines.Length) break;
            var sha = lines[i];
            var author = lines[i + 1];
            var email = lines[i + 2];
            var date = long.TryParse(lines[i + 3], out var t) ? DateTimeOffset.FromUnixTimeSeconds(t).ToLocalTime() : default;
            var subject = lines[i + 4];
            i += 5;
            var change = FileChange.Modified;
            string path = "", oldPath = "";
            // name-status の行（--follow なので 1 行）
            while (i < lines.Length && lines[i].Length > 0 && lines[i].Contains('\t'))
            {
                var parts = lines[i].Split('\t');
                change = parts[0][0] switch
                {
                    'A' => FileChange.Added,
                    'D' => FileChange.Deleted,
                    'R' => FileChange.Renamed,
                    'C' => FileChange.Copied,
                    'T' => FileChange.TypeChanged,
                    _ => FileChange.Modified,
                };
                if (parts.Length >= 3) { oldPath = parts[1]; path = parts[2]; }
                else path = parts[^1];
                i++;
            }
            list.Add(new FileHistoryEntry(sha, author, email, date, subject, change, path, change is FileChange.Renamed or FileChange.Copied ? oldPath : null));
        }
        return list;
    }

    /// <summary>そのコミットでのファイルの diff（ルートコミットは空ツリーと）。fullFile なら全体（Blame の表示）。</summary>
    public async Task<FileDiff?> ReadDiffAsync(FileHistoryEntry entry, bool hasParent, bool fullFile, CancellationToken ct = default)
    {
        var paths = entry.OldPath is { } old ? new[] { old, entry.Path } : [entry.Path];
        var r = await Run(GitArgs.FileHistoryDiff(entry.Sha, hasParent ? entry.Sha + "~1" : GitArgs.EmptyTree, paths, fullFile), ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return UnifiedDiffParser.Parse(r.StandardOutput).FirstOrDefault();
    }

    /// <summary>
    /// 2 つ選んだときの diff（古い方 → 新しい方。`diff ... &lt;古い SHA&gt; &lt;新しい SHA&gt; --unified=19 -- path`）。
    /// パスは新しい方のもの（名前が変わっていれば古い名前も渡す）。
    /// </summary>
    public async Task<FileDiff?> ReadCompareDiffAsync(FileHistoryEntry older, FileHistoryEntry newer, bool fullFile, CancellationToken ct = default)
    {
        var paths = older.Path != newer.Path ? new[] { older.Path, newer.Path } : [newer.Path];
        var r = await Run(GitArgs.FileHistoryDiff(newer.Sha, older.Sha, paths, fullFile), ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return UnifiedDiffParser.Parse(r.StandardOutput).FirstOrDefault();
    }

    /// <summary>ファイルを最後に変えたコミット（無ければ null）。</summary>
    public async Task<string?> ReadLastRevisionAsync(string path, CancellationToken ct = default)
    {
        var r = await Run(GitArgs.LastRevisionOfFile(path), ct).ConfigureAwait(false);
        var sha = r.StandardOutput.Trim();
        return r.Succeeded && sha.Length > 0 ? sha : null;
    }

    public async Task<IReadOnlyList<BlameLine>> BlameAsync(string revision, string path, CancellationToken ct = default)
    {
        var r = await Run(GitArgs.BlamePorcelain(revision, path), ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return ParseBlame(r.StandardOutput);
    }

    /// <summary>
    /// `blame --porcelain` を読む。行ごとに "&lt;sha&gt; &lt;元の行&gt; &lt;今の行&gt; [&lt;まとまりの行数&gt;]"、初めて出るコミットのあとにだけ author などのヘッダ、最後にタブで始まる本文。
    /// </summary>
    public static IReadOnlyList<BlameLine> ParseBlame(string output)
    {
        var list = new List<BlameLine>();
        var commits = new Dictionary<string, (string author, string email, DateTimeOffset time, string summary)>();
        var lines = output.Replace("\r\n", "\n").Split('\n');
        string? sha = null;
        var finalLine = 0;
        string author = "", email = "", summary = "";
        long time = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith('\t'))
            {
                if (sha is null) continue;
                if (!commits.TryGetValue(sha, out var c))
                {
                    c = (author, email, DateTimeOffset.FromUnixTimeSeconds(time).ToLocalTime(), summary);
                    commits[sha] = c;
                }
                list.Add(new BlameLine(sha, c.author, c.email, c.time, c.summary, finalLine, line[1..]));
                sha = null;
                continue;
            }
            var space = line.IndexOf(' ');
            if (sha is null && space == 40)
            {
                sha = line[..40];
                var parts = line.Split(' ');
                finalLine = parts.Length > 2 && int.TryParse(parts[2], out var f) ? f : 0;
                continue;
            }
            if (space < 0) continue;
            var key = line[..space];
            var value = line[(space + 1)..];
            switch (key)
            {
                case "author": author = value; break;
                case "author-mail": email = value.Trim('<', '>'); break;
                case "author-time": long.TryParse(value, out time); break;
                case "summary": summary = value; break;
            }
        }
        return list;
    }
}

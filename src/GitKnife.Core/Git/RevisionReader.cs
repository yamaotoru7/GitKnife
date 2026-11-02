using GitKnife.Core.Diff;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Git;

/// <summary>
/// コミット詳細の読み取り：変更ファイル一覧、ファイルごとの diff、その時点のファイル一覧と中身。
/// 比較は最初の親と（マージも stash も）。
/// </summary>
public sealed class RevisionReader(GitProcess git, string workingDirectory)
{
    private Task<GitResult> Run(string[] args, CancellationToken ct) => git.RunAsync(new GitRequest(workingDirectory, args), ct);

    /// <summary>比較の相手（最初の親。親が無いルートコミットは空ツリー）。</summary>
    public static string BaseOf(CommitInfo commit) => commit.Parents.Count > 0 ? commit.Parents[0] : GitArgs.EmptyTree;

    /// <summary>変更ファイル。StatusEntry の Index 側に変化を入れる（Local Changes の Staged と同じ部品で並べるため）。</summary>
    public async Task<IReadOnlyList<StatusEntry>> ReadChangesAsync(CommitInfo commit, CancellationToken ct = default)
    {
        var r = await Run(GitArgs.DiffTreeNameStatus(BaseOf(commit), commit.Sha), ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return ParseNameStatus(r.StandardOutput);
    }

    /// <summary>`diff-tree -z --name-status` の出力（"M\0path\0"、"R100\0old\0new\0"）を読む。</summary>
    public static IReadOnlyList<StatusEntry> ParseNameStatus(string output)
    {
        var list = new List<StatusEntry>();
        var parts = output.Split('\0');
        for (var i = 0; i + 1 < parts.Length; i++)
        {
            var code = parts[i];
            if (code.Length == 0) continue;
            var change = code[0] switch
            {
                'A' => FileChange.Added,
                'D' => FileChange.Deleted,
                'R' => FileChange.Renamed,
                'C' => FileChange.Copied,
                'T' => FileChange.TypeChanged,
                'U' => FileChange.Conflicted,
                _ => FileChange.Modified,
            };
            if (change is FileChange.Renamed or FileChange.Copied && i + 2 < parts.Length)
            {
                list.Add(new StatusEntry(parts[i + 2], change, FileChange.Unmodified) { OriginalPath = parts[i + 1] });
                i += 2;
            }
            else
            {
                list.Add(new StatusEntry(parts[i + 1], change, FileChange.Unmodified));
                i += 1;
            }
        }
        return list;
    }

    /// <summary>2 つのコミットの間の変更ファイル（一覧で 2 つ選んだとき）。</summary>
    public async Task<IReadOnlyList<StatusEntry>> ReadChangesBetweenAsync(string from, string to, CancellationToken ct = default)
    {
        var r = await Run(GitArgs.DiffTreeNameStatus(from, to), ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return ParseNameStatus(r.StandardOutput);
    }

    /// <summary>
    /// コミットと作業ツリー（Compare to Local Changes。`diff-index -z -M &lt;sha&gt;`）。
    /// untracked は入らない（git の diff-index と同じ）。
    /// </summary>
    public async Task<IReadOnlyList<StatusEntry>> ReadChangesToWorkTreeAsync(string sha, CancellationToken ct = default)
    {
        var r = await Run(["diff-index", "-z", "-M", sha], ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return ParseRawZ(r.StandardOutput);
    }

    /// <summary>`diff-index -z`（raw 形式）を読む：":100644 100644 &lt;a&gt; &lt;b&gt; M\0path\0"、名前の変更は "R100\0old\0new\0"。</summary>
    public static IReadOnlyList<StatusEntry> ParseRawZ(string output)
    {
        var list = new List<StatusEntry>();
        var parts = output.Split('\0');
        for (var i = 0; i + 1 < parts.Length; i++)
        {
            var head = parts[i];
            if (!head.StartsWith(':')) continue;
            var status = head.Split(' ').Last();
            var change = status[0] switch
            {
                'A' => FileChange.Added,
                'D' => FileChange.Deleted,
                'R' => FileChange.Renamed,
                'C' => FileChange.Copied,
                'T' => FileChange.TypeChanged,
                'U' => FileChange.Conflicted,
                _ => FileChange.Modified,
            };
            if (change is FileChange.Renamed or FileChange.Copied && i + 2 < parts.Length)
            {
                list.Add(new StatusEntry(parts[i + 2], change, FileChange.Unmodified) { OriginalPath = parts[i + 1] });
                i += 2;
            }
            else
            {
                list.Add(new StatusEntry(parts[i + 1], change, FileChange.Unmodified));
                i += 1;
            }
        }
        return list;
    }

    /// <summary>コミットと作業ツリーのファイルの diff（`diff-index ... --patch &lt;sha&gt; --unified=19 -- path`）。</summary>
    public async Task<FileDiff?> ReadDiffToWorkTreeAsync(string sha, StatusEntry entry, DiffContext context, CancellationToken ct = default)
    {
        var paths = entry.OriginalPath is null ? new[] { entry.Path } : [entry.OriginalPath, entry.Path];
        string[] args = ["-c", "core.quotepath=false", "--no-pager", "diff-index", "--no-ext-diff", "--no-color", "--src-prefix=gkSrcPrefix/", "--dst-prefix=gkDstPrefix/",
            "--full-index", "--patch", sha, .. context.Args, "--", .. paths];
        var r = await Run(args, ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return UnifiedDiffParser.Parse(r.StandardOutput).FirstOrDefault();
    }

    public async Task<FileDiff?> ReadDiffBetweenAsync(string from, string to, StatusEntry entry, DiffContext context, CancellationToken ct = default)
    {
        var paths = entry.OriginalPath is null ? new[] { entry.Path } : [entry.OriginalPath, entry.Path];
        var r = await Run(GitArgs.DiffRevisions(from, to, paths, context), ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return UnifiedDiffParser.Parse(r.StandardOutput).FirstOrDefault();
    }

    public async Task<FileDiff?> ReadDiffAsync(CommitInfo commit, StatusEntry entry, DiffContext context, CancellationToken ct = default)
    {
        var paths = entry.OriginalPath is null ? new[] { entry.Path } : [entry.OriginalPath, entry.Path];
        var r = await Run(GitArgs.DiffRevisions(BaseOf(commit), commit.Sha, paths, context), ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return UnifiedDiffParser.Parse(r.StandardOutput).FirstOrDefault();
    }

    /// <summary>そのコミット時点の全ファイル（File Tree モード）。</summary>
    public async Task<IReadOnlyList<string>> ReadFilesAsync(string sha, CancellationToken ct = default)
    {
        var r = await Run(["ls-tree", "-r", "-z", "--name-only", sha], ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return r.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>そのコミット時点のファイルの中身（File Tree のプレビュー）。NUL を含むならバイナリとして null。</summary>
    public async Task<string?> ReadFileTextAsync(string sha, string path, CancellationToken ct = default)
    {
        var r = await Run(["show", $"{sha}:{path}"], ct).ConfigureAwait(false);
        if (!r.Succeeded) return null;
        return r.StandardOutput.Contains('\0') ? null : r.StandardOutput;
    }
}

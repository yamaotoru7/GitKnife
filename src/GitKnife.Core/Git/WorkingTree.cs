using System.Text;
using GitKnife.Core.Diff;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Git;

/// <summary>
/// Local Changes の操作：選択ファイルの diff、ステージ、アンステージ、破棄、部分ステージ、コミット。
/// ローカル変更とコミットのコマンド列。
/// </summary>
public sealed class WorkingTree(GitProcess git, string workingDirectory, string gitDirectory)
{
    public string WorkingDirectory { get; } = workingDirectory;
    public string GitDirectory { get; } = gitDirectory;

    private Task<GitResult> Run(string[] args, byte[]? stdin = null, CancellationToken ct = default)
        => git.RunAsync(new GitRequest(WorkingDirectory, args) { StandardInput = stdin }, ct);

    // ------------------------------------------------------------------ diff

    /// <summary>
    /// 1 ファイルの diff。staged = true なら index と HEAD、false なら作業ツリーと index（untracked は /dev/null と比較）。
    /// 変更が無ければ null。
    /// </summary>
    public async Task<FileDiff?> ReadDiffAsync(StatusEntry entry, bool staged, DiffContext context, CancellationToken ct = default)
    {
        string[] args;
        if (staged) args = GitArgs.DiffStaged(entry.Path, context);
        else if (entry.IsUntracked) args = GitArgs.DiffUntracked(entry.Path, context);
        else args = GitArgs.DiffWorkingTree(entry.Path, context);
        var r = await Run(args, ct: ct).ConfigureAwait(false);
        // --no-index は差があると exit 1
        if (!r.Succeeded && !(entry.IsUntracked && !staged && r.ExitCode == 1)) r.ThrowIfFailed();
        return UnifiedDiffParser.Parse(r.StandardOutput).FirstOrDefault();
    }

    /// <summary>Amend 中の Staged（`diff-index -z -M --cached HEAD^`。最後のコミットの変更も入る）。</summary>
    public async Task<IReadOnlyList<StatusEntry>> ReadAmendStagedAsync(CancellationToken ct = default)
    {
        var r = await Run(["diff-index", "-z", "-M", "--cached", "HEAD^"], ct: ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return RevisionReader.ParseRawZ(r.StandardOutput);
    }

    /// <summary>Amend 中の Staged のファイルの diff（`diff-index ... --patch HEAD^ --cached --unified=19 -- path`）。</summary>
    public async Task<FileDiff?> ReadAmendDiffAsync(StatusEntry entry, DiffContext context, CancellationToken ct = default)
    {
        var paths = entry.OriginalPath is null ? new[] { entry.Path } : [entry.OriginalPath, entry.Path];
        string[] args = ["-c", "core.quotepath=false", "--no-pager", "diff-index", "--no-ext-diff", "--no-color", "--src-prefix=gkSrcPrefix/", "--dst-prefix=gkDstPrefix/",
            "--full-index", "--patch", "HEAD^", "--cached", .. context.Args, "--", .. paths];
        var r = await Run(args, ct: ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return UnifiedDiffParser.Parse(r.StandardOutput).FirstOrDefault();
    }

    // ------------------------------------------------------------------ ステージ

    public async Task StageAsync(IEnumerable<string> paths, CancellationToken ct = default)
    {
        var list = paths.ToList();
        if (list.Count == 0) return;
        (await Run(GitArgs.Add(), GitArgs.NulSeparated(list), ct).ConfigureAwait(false)).ThrowIfFailed();
    }

    /// <summary>新規ファイル（index に追加しただけのもの）は update-index で外し、残りは reset HEAD で戻す（2 段）。</summary>
    public async Task UnstageAsync(IEnumerable<StatusEntry> entries, CancellationToken ct = default)
    {
        var list = entries.ToList();
        var added = list.Where(e => e.Index == FileChange.Added).Select(e => e.Path).ToList();
        var existing = list.Where(e => e.Index != FileChange.Added)
            .SelectMany(e => e.OriginalPath is null ? [e.Path] : new[] { e.Path, e.OriginalPath }).ToList();
        if (added.Count > 0) (await Run(GitArgs.UnstageNewFiles(), GitArgs.NulSeparated(added), ct).ConfigureAwait(false)).ThrowIfFailed();
        if (existing.Count > 0) (await Run(GitArgs.UnstageExistingFiles(), GitArgs.NulSeparated(existing), ct).ConfigureAwait(false)).ThrowIfFailed();
    }

    /// <summary>
    /// 作業ツリーの変更を捨てる。tracked は index の内容に戻し（checkout-index）、untracked はファイルを消す
    /// （git を使わずに消す）。
    /// </summary>
    public async Task DiscardAsync(IEnumerable<StatusEntry> entries, CancellationToken ct = default)
    {
        var list = entries.ToList();
        var tracked = list.Where(e => !e.IsUntracked).Select(e => e.Path).ToList();
        if (tracked.Count > 0) (await Run(GitArgs.DiscardFiles(), GitArgs.NulSeparated(tracked), ct).ConfigureAwait(false)).ThrowIfFailed();
        foreach (var e in list.Where(e => e.IsUntracked))
        {
            var full = Path.Combine(WorkingDirectory, e.Path.Replace('/', Path.DirectorySeparatorChar));
            if (HostFile.Exists(full)) HostFile.Delete(full);
            else if (HostDirectory.Exists(full)) HostDirectory.Delete(full, recursive: true);
        }
    }

    /// <summary><see cref="PatchBuilder"/> で同じ target 用に作ったパッチを当てる。</summary>
    public async Task ApplyPatchAsync(string patch, PatchTarget target, CancellationToken ct = default)
        => (await Run(GitArgs.ApplyPatch(target), Encoding.UTF8.GetBytes(patch), ct).ConfigureAwait(false)).ThrowIfFailed();

    // ------------------------------------------------------------------ 競合

    /// <summary>
    /// Resolve Using 'main' / 'bugfix/x'（`checkout-index -f --stage=2 -- path` → `add -- path`。stage 2 = ours、3 = theirs）。
    /// </summary>
    public async Task ResolveUsingAsync(string path, bool ours, CancellationToken ct = default)
    {
        (await Run(["checkout-index", "-f", $"--stage={(ours ? 2 : 3)}", "--", path], ct: ct).ConfigureAwait(false)).ThrowIfFailed();
        (await Run(["add", "--", path], ct: ct).ConfigureAwait(false)).ThrowIfFailed();
    }

    /// <summary>Resolver で作った結果を書いて解決済みにする（add）。</summary>
    public async Task SaveResolvedAsync(string path, string content, CancellationToken ct = default)
    {
        var full = Path.Combine(WorkingDirectory, path.Replace('/', Path.DirectorySeparatorChar));
        await HostFile.WriteAllTextAsync(full, content, new UTF8Encoding(false), ct).ConfigureAwait(false);
        (await Run(["add", "--", path], ct: ct).ConfigureAwait(false)).ThrowIfFailed();
    }

    /// <summary>競合のファイルの中身（マーカーつき）。</summary>
    public Task<string> ReadWorkingFileAsync(string path, CancellationToken ct = default)
        => HostFile.ReadAllTextAsync(Path.Combine(WorkingDirectory, path.Replace('/', Path.DirectorySeparatorChar)), ct);

    /// <summary>
    /// 競合の両側で、そのファイルを変えたコミット（競合の案内のカード。コマンド：
    /// `merge-base <ours> <theirs>` → `log --no-show-signature --date-order --pretty=format:%H%n%aN%n%aE%n%ct%n%s <base>..<side> -- <file>`）。
    /// theirs は MERGE_HEAD（cherry-pick / revert なら CHERRY_PICK_HEAD / REVERT_HEAD）、ours は HEAD。
    /// </summary>
    public async Task<(IReadOnlyList<ConflictCommit> ours, IReadOnlyList<ConflictCommit> theirs)> ReadConflictCommitsAsync(string path, CancellationToken ct = default)
    {
        var theirsRef = new[] { "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "REBASE_HEAD" }.FirstOrDefault(n => HostFile.Exists(Path.Combine(GitDirectory, n)));
        if (theirsRef is null) return ([], []);
        var oursSha = (await Run(["rev-parse", "HEAD"], ct: ct).ConfigureAwait(false)).StandardOutput.Trim();
        var theirsSha = (await Run(["rev-parse", theirsRef], ct: ct).ConfigureAwait(false)).StandardOutput.Trim();
        var mb = await Run(["merge-base", oursSha, theirsSha], ct: ct).ConfigureAwait(false);
        if (!mb.Succeeded) return ([], []);
        var @base = mb.StandardOutput.Trim();
        async Task<IReadOnlyList<ConflictCommit>> Side(string tip)
        {
            var r = await Run(["log", "--no-show-signature", "--date-order", "--pretty=format:%H%n%aN%n%aE%n%ct%n%s", $"{@base}..{tip}", "--", path], ct: ct).ConfigureAwait(false);
            var l = r.StandardOutput.Split('\n');
            var list = new List<ConflictCommit>();
            for (var i = 0; i + 4 < l.Length; i += 5)
            {
                list.Add(new ConflictCommit(l[i], l[i + 1], l[i + 2], DateTimeOffset.FromUnixTimeSeconds(long.Parse(l[i + 3])), l[i + 4]));
            }
            return list;
        }
        return (await Side(oursSha).ConfigureAwait(false), await Side(theirsSha).ConfigureAwait(false));
    }

    // ------------------------------------------------------------------ コミット

    /// <summary>コミットメッセージのファイル（.git/COMMITMESSAGE に書いて --file で渡す）。</summary>
    public string CommitMessagePath => Path.Combine(GitDirectory, "COMMITMESSAGE");

    /// <summary>件名と説明からメッセージを作る（説明があれば空行で区切る）。</summary>
    public static string ComposeMessage(string subject, string? description)
    {
        subject = subject.Trim();
        var body = description?.Replace("\r\n", "\n").Trim('\n') ?? "";
        return body.Length == 0 ? subject + "\n" : subject + "\n\n" + body + "\n";
    }

    public async Task<GitResult> CommitAsync(string message, bool amend, bool signOff = false, CancellationToken ct = default)
    {
        // コミットのたびに commit.template を読む（未設定なら exit 1 で何もしない）
        await Run(GitArgs.ConfigCommitTemplate(), ct: ct).ConfigureAwait(false);
        await HostFile.WriteAllTextAsync(CommitMessagePath, message, new UTF8Encoding(false), ct).ConfigureAwait(false);
        return (await Run(GitArgs.Commit(CommitMessagePath, amend, signOff), ct: ct).ConfigureAwait(false)).ThrowIfFailed();
    }

    /// <summary>最後のコミットのメッセージ（Amend をオンにしたとき欄に入れる）。</summary>
    public async Task<string> ReadLastCommitMessageAsync(CancellationToken ct = default)
    {
        var r = await Run(GitArgs.LastCommitMessage(), ct: ct).ConfigureAwait(false);
        return r.Succeeded ? r.StandardOutput.TrimEnd('\n') : "";
    }

    /// <summary>commit.template の内容（未設定なら null）。</summary>
    public async Task<string?> ReadCommitTemplateAsync(CancellationToken ct = default)
    {
        var r = await Run(GitArgs.ConfigCommitTemplate(), ct: ct).ConfigureAwait(false);
        if (!r.Succeeded) return null;
        var path = r.StandardOutput.Trim();
        if (path.StartsWith("~/", StringComparison.Ordinal)) path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
        if (!Path.IsPathRooted(path)) path = Path.Combine(WorkingDirectory, path);
        return HostFile.Exists(path) ? await HostFile.ReadAllTextAsync(path, ct).ConfigureAwait(false) : null;
    }
}

using System.Globalization;
using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Git;

/// <summary>Preferences の "Sort commits"。値は settings.json の RevisionSortOrder と同じ（0 = Topologically、1 = By date）。</summary>
public enum RevisionSortOrder { Topological = 0, Date = 1 }

public sealed record CommitQuery
{
    public RevisionSortOrder Order { get; init; } = RevisionSortOrder.Topological;
    public int MaxCount { get; init; } = 50000;
    public bool IncludeStashes { get; init; } = true;
    public bool IncludeTags { get; init; } = true;
    public bool IncludeRemoteBranches { get; init; } = true;
    /// <summary>Collapse All Merges（Show First Parent）。</summary>
    public bool FirstParent { get; init; }
    /// <summary>参照フィルタ（空なら全部）。完全名。</summary>
    public IReadOnlyList<string> OnlyReferences { get; init; } = [];
    /// <summary>
    /// OnlyReferences を指定しても stash をすべて差し込む（Hide のとき）。false で OnlyReferences があるとき（Filter）は、
    /// 元のコミットが一覧にある stash だけ差し込む（Filter by Active Branch では出て、feature の Filter では出ない）。
    /// </summary>
    public bool StashesWithReferences { get; init; }
    /// <summary>View > Show Lost Commits (Reflog)：`--reflog` で reflog のコミットも辿る。</summary>
    public bool IncludeReflog { get; init; }
}

/// <summary>
/// リポジトリの読み取り。git CLI を使う：並び順と判定が git と完全に一致する。
/// どのメソッドも書き込みロックを取らない（GIT_OPTIONAL_LOCKS=0）。
/// </summary>
public sealed class RepositoryReader
{
    private readonly GitProcess _git;
    public string WorkingDirectory { get; }
    public string GitDirectory { get; }
    public string CommonGitDirectory { get; }

    private RepositoryReader(GitProcess git, string workingDirectory, string gitDirectory, string commonGitDirectory)
    {
        _git = git;
        WorkingDirectory = workingDirectory;
        GitDirectory = gitDirectory;
        CommonGitDirectory = commonGitDirectory;
    }

    /// <summary>path（リポジトリ内の任意の場所）からリポジトリを開く。リポジトリでなければ null。</summary>
    public static async Task<RepositoryReader?> OpenAsync(GitProcess git, string path, CancellationToken ct = default)
    {
        if (!HostDirectory.Exists(path)) return null;
        var r = await git.RunAsync(new GitRequest(path, ["rev-parse", "--show-toplevel", "--absolute-git-dir", "--git-common-dir"]), ct).ConfigureAwait(false);
        if (!r.Succeeded) return null;
        var lines = r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 3) return null;
        var top = Norm(git, lines[0]);
        var gitDir = Norm(git, lines[1]);
        var common = Path.IsPathRooted(lines[2]) ? Norm(git, lines[2]) : Norm(GitHost.Local, Path.Combine(top, lines[2]));
        return new RepositoryReader(git, top, gitDir, common);
    }

    /// <summary>git の出力のパス（WSL なら /home/...）を Windows の形にする。</summary>
    private static string Norm(GitProcess git, string p) => Norm(git.Host, p);

    private static string Norm(GitHost host, string p) => Path.GetFullPath(host.ToLocalPath(p).Replace('/', Path.DirectorySeparatorChar)).TrimEnd(Path.DirectorySeparatorChar);

    private Task<GitResult> Run(string[] args, CancellationToken ct) => _git.RunAsync(new GitRequest(WorkingDirectory, args), ct);

    /// <summary>同じリポジトリの Local Changes 操作。</summary>
    public WorkingTree CreateWorkingTree() => new(_git, WorkingDirectory, GitDirectory);

    /// <summary>同じリポジトリのコミット詳細の読み取り。</summary>
    public RevisionReader CreateRevisionReader() => new(_git, WorkingDirectory);

    // ------------------------------------------------------------------ 参照

    private const string RefFormat = "%(refname)%00%(objectname)%00%(objecttype)%00%(*objectname)%00%(upstream)%00%(upstream:track,nobracket)%00%(HEAD)%00%(contents:subject)%00%(worktreepath)";

    public async Task<RepositoryReferences> ReadReferencesAsync(CancellationToken ct = default)
    {
        var refsTask = Run(["for-each-ref", $"--format={RefFormat}", "refs/heads", "refs/remotes", "refs/tags", "refs/stash"], ct);
        var headTask = Run(["rev-parse", "--verify", "-q", "HEAD"], ct);
        var symTask = Run(["symbolic-ref", "-q", "--short", "HEAD"], ct);
        // リモートの URL と Git Flow の設定を 1 回で読む（git を増やさない）
        var remotesTask = Run(["config", "--get-regexp", @"^(remote\..*\.(url|pushurl)|gitflow\.(branch\.(master|develop)|prefix\..*))$"], ct);
        await Task.WhenAll(refsTask, headTask, symTask, remotesTask).ConfigureAwait(false);

        var refs = new List<ReferenceInfo>();
        var hasStash = false;
        foreach (var line in refsTask.Result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\0');
            if (f.Length < 9) continue;
            var name = f[0];
            if (name == "refs/stash") { hasStash = true; continue; }
            if (name.EndsWith("/HEAD", StringComparison.Ordinal) && name.StartsWith("refs/remotes/", StringComparison.Ordinal)) continue;   // origin/HEAD は出さない
            var kind = name.StartsWith("refs/heads/", StringComparison.Ordinal) ? ReferenceKind.LocalBranch
                : name.StartsWith("refs/remotes/", StringComparison.Ordinal) ? ReferenceKind.RemoteBranch
                : name.StartsWith("refs/tags/", StringComparison.Ordinal) ? ReferenceKind.Tag
                : ReferenceKind.Other;
            var annotated = kind == ReferenceKind.Tag && f[2] == "tag";
            var target = annotated && f[3].Length > 0 ? f[3] : f[1];   // annotated tag は指しているコミット
            var (ahead, behind, gone) = ParseTrack(f[5]);
            refs.Add(new ReferenceInfo(name, target, kind)
            {
                IsHead = f[6] == "*",
                Upstream = f[4].Length > 0 ? f[4] : null,
                Ahead = ahead,
                Behind = behind,
                UpstreamGone = gone,
                IsAnnotated = annotated,
                Subject = f[7],
                WorktreePath = f[8].Length > 0 ? Norm(_git, f[8]) : null,
            });
        }

        var stashes = hasStash ? await ReadStashesAsync(ct).ConfigureAwait(false) : [];

        var remotes = new Dictionary<string, (string? fetch, string? push)>();
        var configLines = remotesTask.Result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in configLines)
        {
            if (!line.StartsWith("remote.", StringComparison.OrdinalIgnoreCase)) continue;
            var sp = line.IndexOf(' ');
            if (sp < 0) continue;
            var key = line[..sp];
            var value = line[(sp + 1)..].Trim();
            var lastDot = key.LastIndexOf('.');
            var remote = key["remote.".Length..lastDot];
            remotes.TryGetValue(remote, out var cur);
            remotes[remote] = key.EndsWith(".pushurl", StringComparison.Ordinal) ? (cur.fetch, value) : (value, cur.push);
        }

        return new RepositoryReferences
        {
            References = refs,
            Stashes = stashes,
            Remotes = remotes.Select(kv => new RemoteInfo(kv.Key, kv.Value.fetch ?? "", kv.Value.push)).ToList(),
            HeadSha = headTask.Result.Succeeded ? headTask.Result.StandardOutput.Trim() : null,
            HeadBranch = symTask.Result.Succeeded ? symTask.Result.StandardOutput.Trim() : null,
            Operation = DetectOperation(),
            GitFlow = GitFlowConfig.Parse(configLines),
        };
    }

    /// <summary>"ahead 1, behind 2" / "ahead 1" / "behind 2" / "gone" / "".</summary>
    public static (int ahead, int behind, bool gone) ParseTrack(string track)
    {
        if (track == "gone") return (0, 0, true);
        int ahead = 0, behind = 0;
        foreach (var part in track.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(' ');
            if (kv.Length != 2 || !int.TryParse(kv[1], CultureInfo.InvariantCulture, out var n)) continue;
            if (kv[0] == "ahead") ahead = n; else if (kv[0] == "behind") behind = n;
        }
        return (ahead, behind, false);
    }

    private async Task<IReadOnlyList<StashInfo>> ReadStashesAsync(CancellationToken ct)
    {
        // reflog の件名は 1 行なので改行区切りで読める
        var r = await Run(["log", "-g", "--no-show-signature", "--format=%H%x01%P%x01%ct%x01%gs", "refs/stash", "--"], ct).ConfigureAwait(false);
        var list = new List<StashInfo>();
        var i = 0;
        foreach (var line in r.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = line.Split('\x01');
            if (f.Length < 4) continue;
            var parents = f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Add(new StashInfo(i++, f[0], f[3], parents.FirstOrDefault() ?? "", DateTimeOffset.FromUnixTimeSeconds(long.Parse(f[2], CultureInfo.InvariantCulture))));
        }
        return list;
    }

    /// <summary>.git の目印ファイルから進行中の操作を判定する（NotificationBar の文言を選ぶため）。</summary>
    public RepositoryOperation DetectOperation()
    {
        // .git の直下の名前を 1 回で読む（SSH では 1 つ確かめるたびに ssh を張るので）
        var names = HostDirectory.EntryNames(GitDirectory);
        bool Has(string name) => names.Contains(name);
        if (Has("rebase-merge")) return HostFile.Exists(Path.Combine(GitDirectory, "rebase-merge", "interactive")) ? RepositoryOperation.RebaseInteractive : RepositoryOperation.Rebase;
        if (Has("rebase-apply")) return HostFile.Exists(Path.Combine(GitDirectory, "rebase-apply", "applying")) ? RepositoryOperation.ApplyMailbox : RepositoryOperation.Rebase;
        if (Has("MERGE_HEAD")) return RepositoryOperation.Merge;
        if (Has("CHERRY_PICK_HEAD")) return RepositoryOperation.CherryPick;
        if (Has("REVERT_HEAD")) return RepositoryOperation.Revert;
        if (Has("BISECT_LOG")) return RepositoryOperation.Bisect;
        return RepositoryOperation.None;
    }

    /// <summary>
    /// 進行中の操作の詳細（NotificationBar の文言用）。
    /// merge は MERGE_MSG の 1 行目から相手のブランチ名、rebase は rebase-merge／rebase-apply の head-name・onto・進み具合を読む。
    /// </summary>
    public OperationState ReadOperationState(RepositoryReferences refs)
    {
        var op = refs.Operation;
        string Read(params string[] parts)
        {
            var p = Path.Combine([GitDirectory, .. parts]);
            return HostFile.Exists(p) ? HostFile.ReadAllText(p).Trim() : "";
        }
        string NameOf(string sha) => refs.References.FirstOrDefault(r => r.TargetSha == sha && r.Kind == ReferenceKind.LocalBranch)?.ShortName
            ?? refs.References.FirstOrDefault(r => r.TargetSha == sha)?.ShortName ?? (sha.Length > 7 ? sha[..7] : sha);
        switch (op)
        {
            case RepositoryOperation.Merge:
            {
                var first = Read("MERGE_MSG").Split('\n')[0];
                var m = System.Text.RegularExpressions.Regex.Match(first, @"^Merge (?:remote-tracking )?(?:branch|tag|commit) '([^']+)'");
                return new OperationState(op, m.Success ? m.Groups[1].Value : NameOf(Read("MERGE_HEAD").Split('\n')[0]), refs.HeadBranch ?? "HEAD");
            }
            case RepositoryOperation.Rebase or RepositoryOperation.RebaseInteractive or RepositoryOperation.ApplyMailbox:
            {
                var dir = HostDirectory.Exists(Path.Combine(GitDirectory, "rebase-merge")) ? "rebase-merge" : "rebase-apply";
                var head = Read(dir, "head-name").Replace("refs/heads/", "");
                var onto = NameOf(Read(dir, "onto"));
                int.TryParse(Read(dir, dir == "rebase-merge" ? "msgnum" : "next"), out var step);
                int.TryParse(Read(dir, dir == "rebase-merge" ? "end" : "last"), out var total);
                var amend = dir == "rebase-merge" ? Read(dir, "amend") : "";
                return new OperationState(op, onto, head, step, total, amend.Length >= 7 ? amend[..7] : null);
            }
            case RepositoryOperation.CherryPick:
                return new OperationState(op, NameOf(Read("CHERRY_PICK_HEAD")), refs.HeadBranch ?? "HEAD");
            case RepositoryOperation.Revert:
                return new OperationState(op, NameOf(Read("REVERT_HEAD")), refs.HeadBranch ?? "HEAD");
            case RepositoryOperation.Bisect:
                // Other = 始めたブランチ（BISECT_START）、Target = bad と印を付けたコミット（refs/bisect/bad。まだなら始めたときのコミット）
                {
                    var start = Read("BISECT_START");
                    var bad = Read("refs", "bisect", "bad");
                    var startSha = refs.References.FirstOrDefault(r => r.Kind == ReferenceKind.LocalBranch && r.ShortName == start)?.TargetSha ?? start;
                    var bisectDir = Path.Combine(GitDirectory, "refs", "bisect");
                    var ready = bad.Length > 0 && HostDirectory.Exists(bisectDir) && HostDirectory.EnumerateFiles(bisectDir, "good-*").Any();
                    return new OperationState(op, start, bad.Length > 0 ? bad : startSha, BisectReady: ready);
                }
            default:
                return new OperationState(op, "", refs.HeadBranch ?? "");
        }
    }

    // ------------------------------------------------------------------ コミット

    /// <summary>
    /// コミット一覧。ブランチ・リモートブランチ・タグ・HEAD から辿る。stash は index／untracked の親を隠し、
    /// 先端だけを最初の親の上に差し込む（"index on main" の行は出さない）。
    /// </summary>
    public async Task<IReadOnlyList<CommitInfo>> ReadCommitsAsync(CommitQuery query, RepositoryReferences refs, CancellationToken ct = default)
    {
        var args = new List<string> { "log", "--no-show-signature", "-z", "--format=%H%x01%P%x01%an%x01%ae%x01%aI%x01%cn%x01%ce%x01%cI%x01%s%x01%b" };
        args.Add(query.Order == RevisionSortOrder.Topological ? "--topo-order" : "--date-order");
        if (query.FirstParent) args.Add("--first-parent");
        args.Add($"--max-count={query.MaxCount}");
        byte[]? stdin = null;
        // 参照が多いとコマンドラインの上限（32767 文字）を超えて起動できない（2026-10-30 llama.cpp で fetch の後に upstream-check/… が数百本）ので stdin で渡す
        if (query.OnlyReferences.Sum(r => r.Length + 3) > 8000)
        {
            args.Add("--stdin");
            stdin = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', query.OnlyReferences) + "\n");
        }
        else if (query.OnlyReferences.Count > 0) args.AddRange(query.OnlyReferences);
        else
        {
            args.Add("--branches");
            if (query.IncludeRemoteBranches) args.Add("--remotes");
            if (query.IncludeTags) args.Add("--tags");
            if (refs.HeadSha is not null) args.Add("HEAD");
            if (query.IncludeReflog) args.Add("--reflog");
        }
        if (refs.HeadSha is null && refs.References.Count == 0) return [];
        args.Add("--");
        var r = await _git.RunAsync(new GitRequest(WorkingDirectory, [.. args]) { StandardInput = stdin }, ct).ConfigureAwait(false);
        r.ThrowIfFailed();

        var commits = new List<CommitInfo>();
        foreach (var record in r.StandardOutput.Split('\0'))
        {
            var rec = record.TrimStart('\n');
            if (rec.Length == 0) continue;
            var c = ParseCommit(rec);
            if (c is not null) commits.Add(c);
        }

        if (query.IncludeStashes && refs.Stashes.Count > 0)
        {
            var stashes = query.OnlyReferences.Count == 0 || query.StashesWithReferences ? refs.Stashes
                : refs.Stashes.Where(s => commits.Any(c => c.Sha == s.BaseSha)).ToList();
            // reflog では stash のコミットが普通の行としても来る。同じ SHA を 2 行にしない（stash の行だけ残す）
            if (query.IncludeReflog) commits.RemoveAll(c => stashes.Any(s => s.Sha == c.Sha));
            if (stashes.Count > 0) await InsertStashesAsync(commits, stashes, query.Order, ct).ConfigureAwait(false);
        }
        return commits;
    }

    private static CommitInfo? ParseCommit(string rec)
    {
        var f = rec.Split('\x01');
        if (f.Length < 10) return null;
        return new CommitInfo(
            f[0],
            f[1].Split(' ', StringSplitOptions.RemoveEmptyEntries),
            f[2], f[3], FromIso(f[4]),
            f[5], f[6], FromIso(f[7]),
            f[8],
            f[9].TrimEnd('\n'));
    }

    /// <summary>%aI（strict ISO 8601、作者のタイムゾーン付き）。詳細の "5 1 2026 16:33:00 +09:00" に使う。</summary>
    private static DateTimeOffset FromIso(string s) => DateTimeOffset.Parse(s, CultureInfo.InvariantCulture);

    private async Task InsertStashesAsync(List<CommitInfo> commits, IReadOnlyList<StashInfo> stashes, RevisionSortOrder order, CancellationToken ct)
    {
        var index = new Dictionary<string, int>();
        for (var i = 0; i < commits.Count; i++) index[commits[i].Sha] = i;
        // 古い stash から入れると、新しい stash がより上に来る
        foreach (var s in stashes.Reverse())
        {
            var r = await Run(["log", "--no-show-signature", "-z", "-1", "--format=%H%x01%P%x01%an%x01%ae%x01%aI%x01%cn%x01%ce%x01%cI%x01%s%x01%b", s.Sha, "--"], ct).ConfigureAwait(false);
            var c = r.Succeeded ? ParseCommit(r.StandardOutput.Trim('\0', '\n')) : null;
            if (c is null) continue;
            c = c with { Parents = [s.BaseSha], IsStash = true, AllParents = c.Parents };
            // 元のコミットのすぐ上に入れる（日時ではない：2026-10-27 Frieve-Editor の古いコミットを元にした autostash）。
            // 同じコミットを元にした stash がもうあれば、その上（新しい stash が上）
            var pos = index.TryGetValue(s.BaseSha, out var pp) ? pp : commits.Count;
            while (pos > 0 && commits[pos - 1].IsStash && commits[pos - 1].Parents.FirstOrDefault() == s.BaseSha) pos--;
            commits.Insert(pos, c);
            index.Clear();
            for (var i = 0; i < commits.Count; i++) index[commits[i].Sha] = i;
        }
    }

    // ------------------------------------------------------------------ status

    public async Task<RepositoryStatus> ReadStatusAsync(bool includeIgnored = false, CancellationToken ct = default)
    {
        var args = new List<string> { "--no-optional-locks", "status", "--porcelain=v2", "-z", "--untracked-files=all" };
        if (includeIgnored) args.Add("--ignored=matching");
        var r = await Run([.. args], ct).ConfigureAwait(false);
        r.ThrowIfFailed();
        return new RepositoryStatus { Entries = ParsePorcelainV2(r.StandardOutput) };
    }

    /// <summary>`git status --porcelain=v2 -z` の出力を読む。</summary>
    public static IReadOnlyList<StatusEntry> ParsePorcelainV2(string output)
    {
        var list = new List<StatusEntry>();
        var parts = output.Split('\0');
        for (var i = 0; i < parts.Length; i++)
        {
            var p = parts[i];
            if (p.Length < 2) continue;
            switch (p[0])
            {
                case '1':
                {
                    // 1 XY sub mH mI mW hH hI path
                    var f = p.Split(' ', 9);
                    list.Add(new StatusEntry(f[8], Code(f[1][0]), Code(f[1][1])) { IsSubmodule = f[2][0] == 'S' });
                    break;
                }
                case '2':
                {
                    // 2 XY sub mH mI mW hH hI Xscore path \0 origPath
                    var f = p.Split(' ', 10);
                    var orig = i + 1 < parts.Length ? parts[++i] : null;
                    list.Add(new StatusEntry(f[9], Code(f[1][0]), Code(f[1][1])) { OriginalPath = orig, IsSubmodule = f[2][0] == 'S' });
                    break;
                }
                case 'u':
                {
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path
                    var f = p.Split(' ', 11);
                    list.Add(new StatusEntry(f[10], FileChange.Conflicted, FileChange.Conflicted) { IsSubmodule = f[2][0] == 'S' });
                    break;
                }
                case '?':
                    list.Add(new StatusEntry(p[2..], FileChange.Untracked, FileChange.Untracked));
                    break;
                case '!':
                    list.Add(new StatusEntry(p[2..], FileChange.Ignored, FileChange.Ignored));
                    break;
            }
        }
        return list;
    }

    private static FileChange Code(char c) => c switch
    {
        '.' => FileChange.Unmodified,
        'M' => FileChange.Modified,
        'T' => FileChange.TypeChanged,
        'A' => FileChange.Added,
        'D' => FileChange.Deleted,
        'R' => FileChange.Renamed,
        'C' => FileChange.Copied,
        'U' => FileChange.Conflicted,
        _ => FileChange.Unmodified,
    };
}

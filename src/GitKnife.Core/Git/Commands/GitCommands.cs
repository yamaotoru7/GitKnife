using System.Text;
using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Git.Commands;

/// <summary>
/// 操作ごとの git コマンド列。1 操作 1 メソッドで引数を組み立てる。
/// ここは純粋に引数を作るだけなので単体テストで期待値と突き合わせる。実行は <see cref="GitProcess"/>。
/// 参照はできるだけ完全名（refs/heads/...）で渡す。
/// </summary>
public static class GitArgs
{
    // --- diff 表示（F05）---
    private static readonly string[] DiffCommon =
        ["-c", "core.quotepath=false", "--no-pager", "diff", "--find-renames", "--no-ext-diff", "--no-color",
         "--src-prefix=gkSrcPrefix/", "--dst-prefix=gkDstPrefix/", "--full-index", "--submodule=short"];

    /// <summary>
    /// Save as Patch…（`diff --find-renames --binary ... -- README.md`。文脈は git の既定）。
    /// staged は `--staged`、untracked は `--no-index -- /dev/null path`（どちらも推定）。
    /// </summary>
    public static string[] DiffForPatch(IEnumerable<string> paths, bool staged, bool untracked) =>
        ["-c", "core.quotepath=false", "--no-pager", "diff", "--find-renames", "--binary", "--no-ext-diff", "--no-color",
         "--src-prefix=gkSrcPrefix/", "--dst-prefix=gkDstPrefix/", "--full-index", "--submodule=short",
         .. staged ? new[] { "--staged" } : [], .. untracked ? new[] { "--no-index" } : [], "--", .. untracked ? new[] { "/dev/null" } : [], .. paths];

    /// <summary>2 つ選んだコミットの範囲をパッチに（`format-patch --stdout &lt;古い方&gt;~..&lt;新しい方&gt;`）。</summary>
    public static string[] FormatPatchRange(string older, string newer) => ["format-patch", "--stdout", $"{older}~..{newer}"];

    /// <summary>作業ツリーの変更（unstaged）の diff。</summary>
    public static string[] DiffWorkingTree(string path, DiffContext context) => [.. DiffCommon, .. context.Args, "--", path];
    /// <summary>ステージ済みの diff。</summary>
    public static string[] DiffStaged(string path, DiffContext context) => [.. DiffCommon, "--staged", .. context.Args, "--", path];
    /// <summary>untracked ファイルの diff（/dev/null と比較。exit 1 が正常）。</summary>
    public static string[] DiffUntracked(string path, DiffContext context) => [.. DiffCommon, .. context.Args, "--no-index", "--", "/dev/null", path];

    /// <summary>2 つのコミットの間の diff（コミット詳細。表示用の引数は Local Changes と同じ）。</summary>
    public static string[] DiffRevisions(string from, string to, IEnumerable<string> paths, DiffContext context) => [.. DiffCommon, .. context.Args, from, to, "--", .. paths];
    /// <summary>変更ファイルの一覧（名前変更を見つける）。</summary>
    public static string[] DiffTreeNameStatus(string from, string to) => ["diff-tree", "-r", "-z", "--no-commit-id", "--name-status", "--find-renames", from, to];
    /// <summary>空のツリー（ルートコミットの比較相手）。</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    // --- File History / Blame（J01、J02）---
    /// <summary>
    /// ファイルの履歴（名前の変更を追う）。今のブランチ（HEAD）と、その upstream があればそこから探す。
    /// 例：pytube の .coveragerc は、ローカルの master の 49d9c99 と、upstream の origin/master だけにある 5d2c1a4（消したコミット）の 2 件を出す。
    /// 全ブランチではない（basic の src/app.cs に bugfix/greeting の "Capitalize greeting" は出さない）。
    /// </summary>
    public static string[] FileHistory(string path, string? upstream = null) =>
        ["-c", "core.quotePath=false", "log", "--no-show-signature", "--oneline", "--name-status", "--follow", "--pretty=format:%H%n%aN%n%aE%n%ct%n%s",
         .. (upstream is { Length: > 0 } u ? new[] { "HEAD", u } : []), "--", path];
    /// <summary>フォルダの History（名前の変更は追わない。--follow はパスが 1 つのファイルのときだけ使える）。</summary>
    public static string[] FolderHistory(string path, string? upstream = null) =>
        ["-c", "core.quotePath=false", "log", "--no-show-signature", "--name-status", "--pretty=format:%H%n%aN%n%aE%n%ct%n%s",
         .. (upstream is { Length: > 0 } u ? new[] { "HEAD", u } : []), "--", path];
    /// <summary>履歴の 1 コミットでのファイルの変更（親と比べる）。fullFile なら全体を 1 つのハンクに（Blame の表示）。</summary>
    public static string[] FileHistoryDiff(string sha, string parent, IEnumerable<string> paths, bool fullFile) =>
        ["-c", "core.quotepath=false", "--no-pager", "diff", "--no-ext-diff", "--no-color", "--src-prefix=gkSrcPrefix/", "--dst-prefix=gkDstPrefix/", "--full-index", "--submodule=short",
         parent, sha, .. fullFile ? new[] { "--inter-hunk-context=1000000", "--unified=1000000" } : ["--unified=19"], "--", .. paths];
    /// <summary>ファイルを最後に変えたコミット（Blame を開いたときに最初に出すリビジョン）。</summary>
    public static string[] LastRevisionOfFile(string path) => ["log", "--no-show-signature", "--follow", "-n", "1", "--pretty=format:%H", "HEAD", "--", path];
    public static string[] BlamePorcelain(string revision, string path) => ["blame", "--porcelain", revision, "--", path];

    // --- 状態の取得（G02）---
    public static string[] StatusPorcelain() => ["--no-optional-locks", "status", "--porcelain", "-z", "--untracked-files=all"];

    // --- リモート（H01〜H03）---
    public static string[] FetchAll() => ["fetch", "--prune", "--all", "--progress"];
    public static string[] Fetch(string remote) => ["fetch", "--prune", remote, "--progress"];
    /// <summary>Pull（upstream のブランチなら名前を渡さない）。別のブランチを選んだときは remote の後ろに名前を渡す（推定）。</summary>
    public static string[] Pull(string remote, bool rebase, string? branch = null) => branch is null
        ? ["pull", $"--rebase={(rebase ? "true" : "false")}", remote, "--prune", "--progress"]
        : ["pull", $"--rebase={(rebase ? "true" : "false")}", remote, branch, "--prune", "--progress"];
    /// <summary>Push。remoteBranch が localBranch と違うときは refs/heads/A:refs/heads/B（推定）。</summary>
    public static string[] Push(string remote, string localBranch, bool setUpstream, bool force = false, bool allTags = false, string? remoteBranch = null)
    {
        var refspec = remoteBranch is null || remoteBranch == localBranch ? $"refs/heads/{localBranch}" : $"refs/heads/{localBranch}:refs/heads/{remoteBranch}";
        var args = new List<string> { "-c", "push.default=upstream", "push", remote, refspec };
        if (setUpstream) args.Add("--set-upstream");
        if (force) args.Add("--force-with-lease");
        if (allTags) args.Add("--tags");
        args.Add("--verbose");
        args.Add("--progress");
        return [.. args];
    }

    // --- ブランチ・タグ（H04、H05、H10、H11）---
    public static string[] CreateBranchAndCheckout(string name, string startPoint) => ["checkout", "--no-track", "-b", name, $"refs/heads/{startPoint}"];
    public static string[] CreateBranch(string name, string startPoint) => ["branch", "--no-track", name, $"refs/heads/{startPoint}"];
    public static string[] Checkout(string branch) => ["checkout", "--progress", branch];
    public static string[] DeleteBranch(string name) => ["branch", "--delete", "--force", name];
    public static string[] RenameBranch(string oldName, string newName) => ["branch", "--move", oldName, newName];

    /// <summary>
    /// Rename の "Also rename origin/x"：リモートに新しい名前で置いて古い名前を消す
    /// （`push origin &lt;リモート側の SHA&gt;:refs/heads/&lt;新&gt; :refs/heads/&lt;旧&gt;`）。そのあと <see cref="SetUpstream"/>。
    /// </summary>
    public static string[] RenameRemoteBranch(string remote, string sha, string oldName, string newName)
        => ["push", remote, $"{sha}:refs/heads/{newName}", $":refs/heads/{oldName}"];

    /// <summary>`branch --set-upstream-to refs/remotes/&lt;remote&gt;/&lt;branch&gt; &lt;local&gt;`。</summary>
    public static string[] SetUpstream(string remoteRef, string localBranch) => ["branch", "--set-upstream-to", remoteRef, localBranch];
    public static string[] CreateAnnotatedTag(string name, string target, string messageFile) => ["tag", "--annotate", name, $"refs/heads/{target}", "-F", messageFile];
    public static string[] CreateLightweightTag(string name, string target) => ["tag", name, $"refs/heads/{target}"];
    public static string[] DeleteTag(string name) => ["tag", "--delete", name];
    /// <summary>リモートブランチからローカルを作ってチェックアウト（題名 "Track Remote Branch"）。</summary>
    public static string[] CheckoutTrack(string localName, string remoteBranch) => ["checkout", "--track", "-b", localName, remoteBranch];
    /// <summary>
    /// 起点を完全な参照か SHA で渡す New Branch（ブランチなら refs/heads/x、タグなら refs/tags/x、コミットなら SHA）。
    /// ブランチ起点は 文字列になる。
    /// </summary>
    public static string[] CreateBranchFrom(string name, string startRef, bool checkout) => checkout
        ? ["checkout", "--no-track", "-b", name, startRef]
        : ["branch", "--no-track", name, startRef];
    /// <summary>annotated tag（メッセージが空でも annotated）。target は完全な参照か SHA。</summary>
    public static string[] CreateTagAt(string name, string targetRef, string messageFile) => ["tag", "--annotate", name, targetRef, "-F", messageFile];
    // 以下はリモート側の削除・タグの push
    public static string[] DeleteRemoteBranch(string remote, string branch) => ["push", remote, "--delete", $"refs/heads/{branch}"];
    public static string[] PushTag(string remote, string tag) => ["push", remote, $"refs/tags/{tag}"];
    public static string[] DeleteRemoteTag(string remote, string tag) => ["push", remote, "--delete", $"refs/tags/{tag}"];
    public static string[] StatusUntracked() => ["--no-optional-locks", "status", "--porcelain", "-z", "--untracked-files=all"];

    // --- stash（H12）---
    public static string[] StashSave(string message) => ["stash", "save", "--message", message];
    /// <summary>選んだファイルだけ stash（メッセージ無しで `stash push -- README.md`。メッセージ付きの形は推定）。</summary>
    public static string[] StashPushPaths(string message, IEnumerable<string> paths)
        => message.Length == 0 ? ["stash", "push", "--", .. paths] : ["stash", "push", "--message", message, "--", .. paths];
    public static string[] StashPop(int index) => ["stash", "pop", "--index", $"refs/stash@{{{index}}}"];
    public static string[] StashApply(int index) => ["stash", "apply", "--index", $"refs/stash@{{{index}}}"];
    public static string[] StashDrop(int index) => ["stash", "drop", $"refs/stash@{{{index}}}"];

    // --- ステージ（G03）。パスは stdin に NUL 区切りで渡す ---
    public static string[] Add() => ["add", "--force", "--verbose", "--pathspec-from-file=-", "--pathspec-file-nul", "--"];
    public static string[] UnstageNewFiles() => ["update-index", "--force-remove", "-z", "--stdin"];
    /// <summary>既存ファイル（HEAD にあるもの）の unstage。新規と混在するときは UnstageNewFiles の後に実行する。</summary>
    public static string[] UnstageExistingFiles() => ["reset", "-q", "--no-refresh", "HEAD", "--pathspec-from-file=-", "--pathspec-file-nul", "--"];
    /// <summary>
    /// 選んだ行のステージ・アンステージ・破棄。パッチは stdin。
    /// Stage：`apply --cached --whitespace=nowarn --ignore-space-change -C1`、Unstage：その後ろに `--reverse`、
    /// Discard：`apply --whitespace=nowarn --ignore-whitespace`（パッチは逆向きに作ってある）。
    /// </summary>
    public static string[] ApplyPatch(Diff.PatchTarget target) => target switch
    {
        Diff.PatchTarget.Stage => ["apply", "--cached", "--whitespace=nowarn", "--ignore-space-change", "-C1"],
        Diff.PatchTarget.Unstage => ["apply", "--cached", "--whitespace=nowarn", "--ignore-space-change", "-C1", "--reverse"],
        _ => ["apply", "--whitespace=nowarn", "--ignore-whitespace"],
    };
    public static string[] DiscardFiles() => ["checkout-index", "--index", "--force", "--stdin", "-z"];
    public static byte[] NulSeparated(IEnumerable<string> paths) => Encoding.UTF8.GetBytes(string.Join('\0', paths) + "\0");

    // --- コミット（G06）---
    /// <summary>コミット。Repository Settings の sign_off なら --signoff（`commit --signoff --file <file>`。Amend との並びは推定）。</summary>
    public static string[] Commit(string messageFile, bool amend, bool signOff = false) =>
        ["commit", .. amend ? new[] { "--amend" } : [], .. signOff ? new[] { "--signoff" } : [], "--file", messageFile];
    public static string[] ConfigCommitTemplate() => ["config", "commit.template"];
    public static string[] LastCommitMessage() => ["log", "--no-show-signature", "-1", "--pretty=%B", "--"];

    // --- マージ・リベース（H06、H07）---
    public static string[] MergeBase(string a, string b) => ["merge-base", a, b];
    public static string[] MergeTree(string @base, string a, string b) => ["merge-tree", @base, a, b];
    public static string[] Merge(string branch, MergeOption option) => option switch
    {
        MergeOption.Default => ["merge", branch],
        MergeOption.NoFastForward => ["merge", branch, "--no-ff"],
        MergeOption.Squash => ["merge", branch, "--squash"],
        MergeOption.DontCommit => ["merge", branch, "--no-ff", "--no-commit"],
        _ => throw new ArgumentOutOfRangeException(nameof(option)),
    };
    public static string[] AbortMerge() => ["reset", "--merge"];
    /// <summary>
    /// Rebase の事前判定（`replay --onto refs/heads/&lt;onto&gt; &lt;base&gt;..&lt;tip&gt;`）。
    /// git 2.5x 以降の replay は既定で参照を書き換える（--ref-action=update）ので、<paramref name="printOnly"/> なら `--ref-action=print` を付けて書き換えさせない
    /// （付けない形は、オプションの無い古い git でやり直すときに使う）。
    /// </summary>
    public static string[] ReplayPreview(string onto, string @base, string tip, bool printOnly = true)
        => ["replay", .. printOnly ? new[] { "--ref-action=print" } : [], "--onto", onto.StartsWith("refs/", StringComparison.Ordinal) ? onto : $"refs/heads/{onto}", $"{@base}..{tip}"];
    public static string[] Rebase(string onto) => ["-c", "core.commentChar=^", "rebase", $"refs/heads/{onto}"];
    public static string AutostashMessage(string operation, DateTime time) => $"{operation} autostash {time:yyyy/MM/dd (ddd) HH:mm:ss}";

    // --- Git Flow（H18。git-commands.md「Git Flow」。git-flow は同梱の git の usr/bin にある）---

    /// <summary>Initialize の 1 段目：2 つのブランチを作る（既にあれば失敗するが、続ける）。</summary>
    public static string[][] GitFlowInitBranches(string master, string develop) => [["branch", "--no-track", master], ["branch", "--no-track", develop]];

    /// <summary>Initialize の 2 段目：設定を書く。bugfix / support の接頭辞は画面に無く、既定のまま書く。hooksPath は .git/hooks の / 区切りの絶対パス。</summary>
    public static string[][] GitFlowInitConfig(string master, string develop, string feature, string release, string hotfix, string versionTag, string hooksPath) =>
    [
        ["config", "gitflow.branch.develop", develop],
        ["config", "gitflow.branch.master", master],
        ["config", "gitflow.prefix.feature", feature],
        ["config", "gitflow.prefix.release", release],
        ["config", "gitflow.prefix.hotfix", hotfix],
        ["config", "gitflow.prefix.versiontag", versionTag],
        ["config", "gitflow.prefix.bugfix", "bugfix/"],
        ["config", "gitflow.prefix.support", "support/"],
        ["config", "gitflow.path.hooks", hooksPath],
    ];

    public static string[] GitFlowInit() => ["flow", "init", "-d"];

    public static string[] GitFlowStart(GitFlowKind kind, string name, string startAt) => ["flow", KindName(kind), "start", name, startAt];

    /// <summary>Finish Feature。Delete branches を外すと -k、Rebase before merging は -r、No fast-forward は --no-ff（並びは -r、-k、--no-ff）。</summary>
    public static string[] GitFlowFinishFeature(string name, bool deleteBranch, bool rebase, bool noFastForward) =>
        ["flow", "feature", "finish", .. rebase ? new[] { "-r" } : [], .. deleteBranch ? [] : new[] { "-k" }, .. noFastForward ? new[] { "--no-ff" } : [], name];

    /// <summary>Finish Release。タグのメッセージはファイルで渡す（空でも -f）。Delete branches を外すと -k、Back-merge を外すと -b。</summary>
    public static string[] GitFlowFinishRelease(string name, string tagMessageFile, bool deleteBranch, bool backMerge) =>
        ["flow", "release", "finish", "-f", tagMessageFile, .. deleteBranch ? [] : new[] { "-k" }, .. backMerge ? [] : new[] { "-b" }, name];

    /// <summary>Finish Hotfix（-k は Feature / Release と同じと推定）。</summary>
    public static string[] GitFlowFinishHotfix(string name, string tagMessageFile, bool deleteBranch) =>
        ["flow", "hotfix", "finish", "-f", tagMessageFile, .. deleteBranch ? [] : new[] { "-k" }, name];

    /// <summary>Deinitialize（確認なし）。`gitflow.branch.&lt;名前&gt;.base` の節は残る。</summary>
    public static string[][] GitFlowDeinit() =>
        [["config", "--remove-section", "gitflow.path"], ["config", "--remove-section", "gitflow.prefix"], ["config", "--remove-section", "gitflow.branch"]];

    private static string KindName(GitFlowKind kind) => kind switch { GitFlowKind.Feature => "feature", GitFlowKind.Release => "release", _ => "hotfix" };
}

/// <summary>
/// Merge ダイアログの Merge Option（設定 MergeType）。選択肢と git コマンド、MergeType の値：
/// Default（`merge b`）= 0、No Fast-Forward（`--no-ff`）= 1、Squash（`--squash`）= 2、Don't Commit（`--no-ff --no-commit`）= 3（3 は 0〜2 からの推定）。
/// </summary>
public enum MergeOption
{
    Default = 0,
    NoFastForward = 1,
    Squash = 2,
    DontCommit = 3,
}

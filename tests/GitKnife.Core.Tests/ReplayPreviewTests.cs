using GitKnife.Core.Git;
using GitKnife.Core.Git.Commands;

namespace GitKnife.Core.Tests;

/// <summary>
/// Rebase の事前判定（`git replay`）がリポジトリを書き換えないこと。git 2.5x の replay は既定で参照を更新するので、
/// --ref-action=print を付け忘れると Rebase ダイアログを開いただけで main が書き換わる（2026-10-25 に sandbox で起きた）。
/// </summary>
[Collection("sandbox")]
public class ReplayPreviewTests(SandboxFixture sandbox)
{
    private async Task<string> Sha(string rev)
        => (await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, ["rev-parse", rev]))).StandardOutput.Trim();

    [Fact]
    public async Task PreviewDoesNotMoveBranch()
    {
        var before = await Sha("refs/heads/main");
        var @baseSha = (await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, GitArgs.MergeBase("feature/login", "main")))).StandardOutput.Trim();
        var r = await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, GitArgs.ReplayPreview("feature/login", @baseSha, "main")));
        if (!r.Succeeded && r.StandardError.Contains("ref-action", StringComparison.Ordinal))
            r = await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, GitArgs.ReplayPreview("feature/login", @baseSha, "main", printOnly: false)));
        Assert.True(r.Succeeded, r.StandardError);
        Assert.Equal(before, await Sha("refs/heads/main"));
        Assert.Contains("update refs/heads/main", r.StandardOutput);   // 書き換える代わりに update-ref の指示を出す
    }

    /// <summary>コミット（SHA）の上への事前判定が通り、競合は終了コード 1 で返ること（#16）。</summary>
    [Fact]
    public async Task PreviewOntoCommitShaAndConflictExitCode()
    {
        var onto = await Sha("feature/login");
        var @baseSha = (await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, GitArgs.MergeBase(onto, "main")))).StandardOutput.Trim();
        var r = await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, GitArgs.ReplayPreview(onto, @baseSha, "main")));
        if (!r.Succeeded && r.StandardError.Contains("ref-action", StringComparison.Ordinal)) return;   // 古い git では確かめない
        Assert.True(r.Succeeded, r.StandardError);

        var conflictBase = (await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, GitArgs.MergeBase("main", "bugfix/greeting")))).StandardOutput.Trim();
        var c = await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, GitArgs.ReplayPreview(await Sha("main"), conflictBase, "bugfix/greeting")));
        Assert.Equal(1, c.ExitCode);
    }
}

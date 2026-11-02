using GitKnife.Core.Git.Commands;

namespace GitKnife.Core.Tests;

/// <summary>期待するコマンド列と一致すること。</summary>
public class GitArgsTests
{
    private static string Join(string[] a) => string.Join(' ', a);

    [Fact]
    public void FetchArgs()
    {
        Assert.Equal("fetch --prune --all --progress", Join(GitArgs.FetchAll()));
        Assert.Equal("fetch --prune origin --progress", Join(GitArgs.Fetch("origin")));
    }

    [Fact]
    public void PushArgs()
    {
        Assert.Equal("-c push.default=upstream push origin refs/heads/topic/auto --set-upstream --verbose --progress", Join(GitArgs.Push("origin", "topic/auto", setUpstream: true)));
        Assert.Equal("-c push.default=upstream push origin refs/heads/main --verbose --progress", Join(GitArgs.Push("origin", "main", setUpstream: false)));
    }

    [Fact]
    public void BranchTagStashArgs()
    {
        Assert.Equal("checkout --no-track -b topic/auto refs/heads/main", Join(GitArgs.CreateBranchAndCheckout("topic/auto", "main")));
        Assert.Equal("checkout --progress main", Join(GitArgs.Checkout("main")));
        Assert.Equal("branch --delete --force bugfix/greeting", Join(GitArgs.DeleteBranch("bugfix/greeting")));
        Assert.Equal("branch --move feature/login feature/login-renamed", Join(GitArgs.RenameBranch("feature/login", "feature/login-renamed")));
        Assert.Equal("tag --annotate v0.3-auto refs/heads/topic/auto -F C:\\tmp\\msg.txt", Join(GitArgs.CreateAnnotatedTag("v0.3-auto", "topic/auto", "C:\\tmp\\msg.txt")));
        Assert.Equal("tag --delete v0.2", Join(GitArgs.DeleteTag("v0.2")));
        Assert.Equal("stash save --message auto stash", Join(GitArgs.StashSave("auto stash")));
        Assert.Equal("stash pop --index refs/stash@{0}", Join(GitArgs.StashPop(0)));
    }

    [Fact]
    public void StagingAndCommitArgs()
    {
        Assert.Equal("add --force --verbose --pathspec-from-file=- --pathspec-file-nul --", Join(GitArgs.Add()));
        Assert.Equal("update-index --force-remove -z --stdin", Join(GitArgs.UnstageNewFiles()));
        Assert.Equal("checkout-index --index --force --stdin -z", Join(GitArgs.DiscardFiles()));
        Assert.Equal("a.txt\0docs/b.md\0", System.Text.Encoding.UTF8.GetString(GitArgs.NulSeparated(["a.txt", "docs/b.md"])));
        Assert.Equal("commit --file .git\\COMMITMESSAGE", Join(GitArgs.Commit(".git\\COMMITMESSAGE", amend: false)));
        Assert.Equal("commit --amend --file .git\\COMMITMESSAGE", Join(GitArgs.Commit(".git\\COMMITMESSAGE", amend: true)));
        Assert.Equal("commit --signoff --file .git\\COMMITMESSAGE", Join(GitArgs.Commit(".git\\COMMITMESSAGE", amend: false, signOff: true)));
    }

    [Fact]
    public void DiffArgs()
    {
        Assert.Equal("-c core.quotepath=false --no-pager diff --find-renames --no-ext-diff --no-color --src-prefix=gkSrcPrefix/ --dst-prefix=gkDstPrefix/ --full-index --submodule=short --unified=19 -- README.md",
            Join(GitArgs.DiffWorkingTree("README.md", 19)));
        Assert.Equal("-c core.quotepath=false --no-pager diff --find-renames --no-ext-diff --no-color --src-prefix=gkSrcPrefix/ --dst-prefix=gkDstPrefix/ --full-index --submodule=short --unified=19 --no-index -- /dev/null notes.txt",
            Join(GitArgs.DiffUntracked("notes.txt", 19)));
        Assert.Equal("--no-optional-locks status --porcelain -z --untracked-files=all", Join(GitArgs.StatusPorcelain()));
    }

    [Fact]
    public void MergeRebasePullArgs()
    {
        Assert.Equal("merge-base bugfix/greeting main", Join(GitArgs.MergeBase("bugfix/greeting", "main")));
        Assert.Equal("merge-tree 4f248f5 bugfix/greeting main", Join(GitArgs.MergeTree("4f248f5", "bugfix/greeting", "main")));
        Assert.Equal("merge bugfix/greeting --no-ff", Join(GitArgs.Merge("bugfix/greeting", MergeOption.NoFastForward)));
        // Default / Squash / Don't Commit の引数（Don't Commit は --no-ff が先）
        Assert.Equal("merge feature/login", Join(GitArgs.Merge("feature/login", MergeOption.Default)));
        Assert.Equal("merge feature/login --squash", Join(GitArgs.Merge("feature/login", MergeOption.Squash)));
        Assert.Equal("merge feature/login --no-ff --no-commit", Join(GitArgs.Merge("feature/login", MergeOption.DontCommit)));
        Assert.Equal("reset --merge", Join(GitArgs.AbortMerge()));
        // 新しい git の replay は既定で参照を書き換えるので、事前判定は --ref-action=print を付ける（付けないのは古い git でのやり直しだけ）
        Assert.Equal("replay --ref-action=print --onto refs/heads/feature/login 7450f74..3b121ca", Join(GitArgs.ReplayPreview("feature/login", "7450f74", "3b121ca")));
        Assert.Equal("replay --onto refs/heads/feature/login 7450f74..3b121ca", Join(GitArgs.ReplayPreview("feature/login", "7450f74", "3b121ca", printOnly: false)));
        Assert.Equal("-c core.commentChar=^ rebase refs/heads/feature/login", Join(GitArgs.Rebase("feature/login")));
        Assert.Equal("pull --rebase=true origin --prune --progress", Join(GitArgs.Pull("origin", rebase: true)));
        var msg = GitArgs.AutostashMessage("Rebase", new DateTime(2026, 10, 24, 23, 50, 11));
        Assert.StartsWith("Rebase autostash 2026/10/24 (", msg);
        Assert.EndsWith(") 23:50:11", msg);
    }

    /// <summary>git flow のコマンド。</summary>
    [Fact]
    public void GitFlowArgs()
    {
        Assert.Equal(["branch --no-track main", "branch --no-track develop"], GitArgs.GitFlowInitBranches("main", "develop").Select(Join));
        Assert.Equal(
        [
            "config gitflow.branch.develop develop", "config gitflow.branch.master main", "config gitflow.prefix.feature feature/",
            "config gitflow.prefix.release release/", "config gitflow.prefix.hotfix hotfix/", "config gitflow.prefix.versiontag ",
            "config gitflow.prefix.bugfix bugfix/", "config gitflow.prefix.support support/",
            "config gitflow.path.hooks D:/Proj/tools/GitKnife/.work/sandbox/basic/.git/hooks",
        ], GitArgs.GitFlowInitConfig("main", "develop", "feature/", "release/", "hotfix/", "", "D:/Proj/tools/GitKnife/.work/sandbox/basic/.git/hooks").Select(Join));
        Assert.Equal("flow init -d", Join(GitArgs.GitFlowInit()));

        Assert.Equal("flow feature start login2 develop", Join(GitArgs.GitFlowStart(Git.Model.GitFlowKind.Feature, "login2", "develop")));
        Assert.Equal("flow release start 1.0 develop", Join(GitArgs.GitFlowStart(Git.Model.GitFlowKind.Release, "1.0", "develop")));
        Assert.Equal("flow hotfix start 2.0.1 main", Join(GitArgs.GitFlowStart(Git.Model.GitFlowKind.Hotfix, "2.0.1", "main")));

        Assert.Equal("flow feature finish login2", Join(GitArgs.GitFlowFinishFeature("login2", deleteBranch: true, rebase: false, noFastForward: false)));
        Assert.Equal("flow feature finish -r -k --no-ff opt1", Join(GitArgs.GitFlowFinishFeature("opt1", deleteBranch: false, rebase: true, noFastForward: true)));
        Assert.Equal("flow release finish -f msg.txt 1.0", Join(GitArgs.GitFlowFinishRelease("1.0", "msg.txt", deleteBranch: true, backMerge: true)));
        Assert.Equal("flow release finish -f msg.txt -k -b 2.0", Join(GitArgs.GitFlowFinishRelease("2.0", "msg.txt", deleteBranch: false, backMerge: false)));
        Assert.Equal("flow hotfix finish -f msg.txt 2.0.1", Join(GitArgs.GitFlowFinishHotfix("2.0.1", "msg.txt", deleteBranch: true)));

        Assert.Equal(["config --remove-section gitflow.path", "config --remove-section gitflow.prefix", "config --remove-section gitflow.branch"], GitArgs.GitFlowDeinit().Select(Join));
    }

    [Fact]
    public void GitFlowConfigParsesConfigLines()
    {
        Assert.Null(Git.Model.GitFlowConfig.Parse(["remote.origin.url D:/x"]));
        var c = Git.Model.GitFlowConfig.Parse(
        [
            "remote.origin.url D:/x", "gitflow.branch.master main", "gitflow.branch.develop develop", "gitflow.prefix.feature feature/",
            "gitflow.prefix.release release/", "gitflow.prefix.hotfix hotfix/", "gitflow.prefix.versiontag",
        ])!;
        Assert.Equal(("main", "develop", "feature/", ""), (c.Master, c.Develop, c.FeaturePrefix, c.VersionTagPrefix));
        Assert.Equal((Git.Model.GitFlowKind.Release, "1.0"), c.Classify("release/1.0"));
        Assert.Null(c.Classify("develop"));
        Assert.Null(c.Classify("feature/"));
    }
}

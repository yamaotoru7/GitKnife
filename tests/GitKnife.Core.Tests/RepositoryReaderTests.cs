using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Tests;

/// <summary>sandbox/basic を読み、画面に出す情報が取れること。</summary>
[Collection("sandbox")]
public class RepositoryReaderTests(SandboxFixture sandbox)
{
    private async Task<RepositoryReader> Open() => (await RepositoryReader.OpenAsync(sandbox.Git, sandbox.Basic))!;

    [Fact]
    public async Task OpensFromSubdirectory()
    {
        var r = await RepositoryReader.OpenAsync(sandbox.Git, Path.Combine(sandbox.Basic, "src"));
        Assert.NotNull(r);
        Assert.Equal(Path.GetFullPath(sandbox.Basic).TrimEnd('\\'), r!.WorkingDirectory);
        Assert.EndsWith(".git", r.GitDirectory);
        Assert.Null(await RepositoryReader.OpenAsync(sandbox.Git, Path.GetTempPath()));
    }

    [Fact]
    public async Task ReferencesMatchSidebar()
    {
        var refs = await (await Open()).ReadReferencesAsync();
        Assert.Equal("main", refs.HeadBranch);
        var main = refs.Head!;
        Assert.Equal("main", main.ShortName);
        Assert.Equal("refs/remotes/origin/main", main.Upstream);
        Assert.Equal(1, main.Ahead);     // サイドバーの "1↑ 1↓"
        Assert.Equal(1, main.Behind);

        Assert.Equal(["bugfix/greeting", "feature/login", "main"], refs.LocalBranches.Select(b => b.ShortName).Order());
        Assert.Null(refs.LocalBranches.Single(b => b.ShortName == "feature/login").Upstream);
        Assert.Equal(["origin/bugfix/greeting", "origin/main"], refs.RemoteBranches.Select(b => b.ShortName).Order());   // origin/HEAD は出さない
        Assert.Equal("origin", refs.RemoteBranches.First().RemoteName);

        var v01 = refs.Tags.Single(t => t.ShortName == "v0.1");
        var v02 = refs.Tags.Single(t => t.ShortName == "v0.2");
        Assert.True(v01.IsAnnotated);
        Assert.False(v02.IsAnnotated);
        Assert.Equal("First tag", v01.Subject);

        var stash = Assert.Single(refs.Stashes);
        Assert.Equal("On main: WIP: greeting experiment", stash.Message);
        Assert.Equal(refs.HeadSha, stash.BaseSha);

        var origin = Assert.Single(refs.Remotes);
        Assert.Equal("origin", origin.Name);
        Assert.Equal(RepositoryOperation.None, refs.Operation);
    }

    [Fact]
    public async Task CommitsMatchExpectedList()
    {
        var reader = await Open();
        var refs = await reader.ReadReferencesAsync();
        // 設定 RevisionSortOrder=1 = "By date" の並び
        var commits = await reader.ReadCommitsAsync(new CommitQuery { Order = RevisionSortOrder.Date }, refs);
        Assert.Equal(
            ["On main: WIP: greeting experiment", "Document options", "Add changelog", "Capitalize greeting", "Greet the world",
             "Add usage doc", "Require a password", "Add login check", "Print greeting", "Add app skeleton", "Initial commit"],
            commits.Select(c => c.Subject));
        var stash = commits[0];
        Assert.True(stash.IsStash);
        Assert.Single(stash.Parents);                       // index の親は隠す
        Assert.DoesNotContain(commits, c => c.Subject.StartsWith("index on", StringComparison.Ordinal));
        Assert.True(commits.Single(c => c.Subject == "Print greeting").HasDescription);   // "↩"
        Assert.Equal("Other Dev", commits.Single(c => c.Subject == "Add changelog").AuthorName);
        Assert.Equal(7, commits[1].ShortSha.Length);
        Assert.Equal(9, commits[1].HeaderSha.Length);
        Assert.Equal(TimeSpan.FromHours(9), commits[1].AuthorTime.Offset);   // 作者のタイムゾーン
    }

    [Fact]
    public async Task TopologicalOrderKeepsBranchesTogether()
    {
        var reader = await Open();
        var refs = await reader.ReadReferencesAsync();
        var commits = await reader.ReadCommitsAsync(new CommitQuery { Order = RevisionSortOrder.Topological, IncludeStashes = false }, refs);
        // git log --topo-order と同じ：Greet the world が Capitalize greeting より先
        var subjects = commits.Select(c => c.Subject).ToList();
        Assert.True(subjects.IndexOf("Greet the world") < subjects.IndexOf("Capitalize greeting"));
    }

    [Fact]
    public async Task FirstParentAndFilter()
    {
        var reader = await Open();
        var refs = await reader.ReadReferencesAsync();
        var onlyMain = await reader.ReadCommitsAsync(new CommitQuery { OnlyReferences = ["refs/heads/main"] }, refs);
        // stash は元のコミット（Document options）が一覧にあるので出る（Filter by Active Branch と同じ）
        Assert.Equal(["On main: WIP: greeting experiment", "Document options", "Greet the world", "Add usage doc", "Print greeting", "Add app skeleton", "Initial commit"], onlyMain.Select(c => c.Subject));
        var onlyFeature = await reader.ReadCommitsAsync(new CommitQuery { OnlyReferences = ["refs/heads/feature/login"] }, refs);
        Assert.DoesNotContain(onlyFeature, c => c.IsStash);
    }

    /// <summary>参照が多くてもコマンドラインの上限で落ちない（stdin で渡す。2026-10-30 llama.cpp）。</summary>
    [Fact]
    public async Task ManyReferencesGoThroughStdin()
    {
        var reader = await Open();
        var refs = await reader.ReadReferencesAsync();
        var one = await reader.ReadCommitsAsync(new CommitQuery { OnlyReferences = ["refs/heads/main"] }, refs);
        var many = await reader.ReadCommitsAsync(new CommitQuery { OnlyReferences = Enumerable.Repeat("refs/heads/main", 3000).ToList() }, refs);
        Assert.Equal(one.Select(c => c.Sha), many.Select(c => c.Sha));
    }

    [Fact]
    public async Task StatusMatchesLocalChanges()
    {
        var status = await (await Open()).ReadStatusAsync();
        Assert.Equal(["notes.txt", "README.md"], status.Unstaged.Select(e => e.Path).Order(StringComparer.Ordinal).Reverse());
        Assert.Equal(["docs/todo.md"], status.Staged.Select(e => e.Path));
        Assert.True(status.Entries.Single(e => e.Path == "notes.txt").IsUntracked);
        Assert.Equal(FileChange.Modified, status.Entries.Single(e => e.Path == "README.md").WorkTree);
        Assert.Equal(FileChange.Added, status.Entries.Single(e => e.Path == "docs/todo.md").Index);
        Assert.Equal(3, status.ChangeCount);   // "Local Changes (3)"
    }

    [Fact]
    public void ParsesRenameAndConflict()
    {
        var output = "2 R. N... 100644 100644 100644 aaa bbb R100 new name.txt\0old.txt\0" +
                     "u UU N... 100644 100644 100644 100644 a b c src/app.cs\0" +
                     "1 .M S.M. 160000 160000 160000 x x sub\0";
        var entries = RepositoryReader.ParsePorcelainV2(output);
        Assert.Equal("new name.txt", entries[0].Path);
        Assert.Equal("old.txt", entries[0].OriginalPath);
        Assert.Equal(FileChange.Renamed, entries[0].Index);
        Assert.True(entries[1].IsConflicted);
        Assert.True(entries[1].HasUnstagedChange);
        Assert.False(entries[1].HasStagedChange);
        Assert.True(entries[2].IsSubmodule);
    }

    [Fact]
    public void ParsesTrack()
    {
        Assert.Equal((1, 2, false), RepositoryReader.ParseTrack("ahead 1, behind 2"));
        Assert.Equal((0, 3, false), RepositoryReader.ParseTrack("behind 3"));
        Assert.Equal((0, 0, true), RepositoryReader.ParseTrack("gone"));
        Assert.Equal((0, 0, false), RepositoryReader.ParseTrack(""));
    }

    [Fact]
    public async Task DetectsMergeInProgress()
    {
        var repo = SandboxFixture.CreateFresh(out var root);
        try
        {
            var git = sandbox.Git;
            await git.RunAsync(new GitRequest(repo, ["stash", "-u", "-q"]));
            await git.RunAsync(new GitRequest(repo, ["merge", "bugfix/greeting", "--no-ff"]));   // 競合で失敗する
            var reader = (await RepositoryReader.OpenAsync(git, repo))!;
            var refs = await reader.ReadReferencesAsync();
            Assert.Equal(RepositoryOperation.Merge, refs.Operation);
            var status = await reader.ReadStatusAsync();
            Assert.Contains(status.Entries, e => e.Path == "src/app.cs" && e.IsConflicted);
        }
        finally { Testing.SandboxBuilder.DeleteDirectory(root); }
    }
}

using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Tests;

/// <summary>コミット詳細（Commit / Changes / File Tree で見えるもの）。</summary>
[Collection("sandbox")]
public class RevisionReaderTests(SandboxFixture sandbox)
{
    private async Task<(RevisionReader rev, IReadOnlyList<CommitInfo> commits)> Open()
    {
        var reader = (await RepositoryReader.OpenAsync(sandbox.Git, sandbox.Basic))!;
        var refs = await reader.ReadReferencesAsync();
        return (reader.CreateRevisionReader(), await reader.ReadCommitsAsync(new CommitQuery { Order = RevisionSortOrder.Date }, refs));
    }

    [Fact]
    public async Task StashShowsChangeAgainstFirstParent()
    {
        var (rev, commits) = await Open();
        var stash = commits.Single(c => c.IsStash);
        // Changes モード：src/app.cs（M）だけ、"hello, world" → "hello, stash"
        var change = Assert.Single(await rev.ReadChangesAsync(stash));
        Assert.Equal(("src/app.cs", FileChange.Modified), (change.Path, change.Index));
        var diff = (await rev.ReadDiffAsync(stash, change, 19))!;
        Assert.Contains(diff.Hunks[0].Lines, l => l.DisplayText.Contains("hello, stash"));
    }

    [Fact]
    public async Task RootCommitListsAddedFilesAndTree()
    {
        var (rev, commits) = await Open();
        var root = commits.Single(c => c.Parents.Count == 0);
        Assert.All(await rev.ReadChangesAsync(root), e => Assert.Equal(FileChange.Added, e.Index));
        var head = commits.First(c => !c.IsStash).Sha;
        Assert.Contains("src/app.cs", await rev.ReadFilesAsync(head));
        Assert.Contains("class App", await rev.ReadFileTextAsync(head, "src/app.cs"));
    }

    [Fact]
    public void ParsesRenames()
    {
        var list = RevisionReader.ParseNameStatus("M\0a.txt\0R087\0old/b.txt\0new/b.txt\0D\0c.txt\0");
        Assert.Equal(3, list.Count);
        Assert.Equal(("new/b.txt", "old/b.txt", FileChange.Renamed), (list[1].Path, list[1].OriginalPath, list[1].Index));
        Assert.Equal(FileChange.Deleted, list[2].Index);
    }

    [Fact]
    public async Task CompareToLocalChangesListsStagedAndUnstagedButNotUntracked()
    {
        // `diff-index -z -M <sha>`。HEAD と作業ツリー：README.md（unstaged）と docs/todo.md（staged の新規）。notes.txt（untracked）は出ない
        var (rev, commits) = await Open();
        var head = commits.First(c => !c.IsStash).Sha;
        var changes = await rev.ReadChangesToWorkTreeAsync(head);
        Assert.Equal(["README.md", "docs/todo.md"], changes.Select(c => c.Path).OrderBy(p => p, StringComparer.Ordinal));
        Assert.Equal(FileChange.Added, changes.Single(c => c.Path == "docs/todo.md").Index);
        var diff = await rev.ReadDiffToWorkTreeAsync(head, changes.Single(c => c.Path == "README.md"), 19);
        Assert.NotNull(diff);
    }
}

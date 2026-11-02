using GitKnife.Core.Git;

namespace GitKnife.Core.Tests;

/// <summary>サイドバーの Filter / Hide。</summary>
[Collection("sandbox")]
public class ReferenceFilterTests(SandboxFixture sandbox)
{
    [Fact]
    public void FolderPatternsIncludeEveryRemote()
    {
        Assert.Equal(["refs/heads/feature/", "refs/remotes/origin/feature/"], ReferenceFilter.PatternsFor("refs/heads/feature", true, ["origin"]));
        Assert.Equal("'origin/feature/*'", ReferenceFilter.Display("refs/remotes/origin/feature/"));
        Assert.True(ReferenceFilter.Matches("refs/heads/feature/login", ["refs/heads/feature/"]));
        Assert.False(ReferenceFilter.Matches("refs/heads/featurex", ["refs/heads/feature/"]));
    }

    [Fact]
    public async Task FilterAndHideChangeTheVisibleCommits()
    {
        var reader = (await RepositoryReader.OpenAsync(sandbox.Git, sandbox.Basic))!;
        var refs = await reader.ReadReferencesAsync();
        async Task<IReadOnlyList<string>> Subjects(IReadOnlyList<string> filter, IReadOnlyList<string> hidden)
        {
            var start = ReferenceFilter.StartPoints(refs, filter, hidden);
            var commits = await reader.ReadCommitsAsync(new CommitQuery { Order = RevisionSortOrder.Date, OnlyReferences = start ?? [], StashesWithReferences = filter.Count == 0 }, refs);
            return commits.Select(c => c.Subject).ToList();
        }
        // feature フォルダで絞ると Require a password / Add login check / Print greeting / Add app skeleton / Initial commit の 5 つ
        Assert.Equal(["Require a password", "Add login check", "Print greeting", "Add app skeleton", "Initial commit"],
            await Subjects(["refs/heads/feature/", "refs/remotes/origin/feature/"], []));
        // bugfix を隠すと Capitalize greeting が消え、stash は残る
        var hidden = await Subjects([], ["refs/heads/bugfix/", "refs/remotes/origin/bugfix/"]);
        Assert.DoesNotContain("Capitalize greeting", hidden);
        Assert.Contains(hidden, s => s.Contains("WIP: greeting experiment"));
        // View > Filter by Active Branch（"HEAD"）は main と origin/main。stash は元のコミットが一覧にあるので出る
        Assert.Equal(["refs/heads/main", "refs/remotes/origin/main"], ReferenceFilter.Expand(refs, ["HEAD"]));
        var active = await Subjects(["HEAD"], []);
        Assert.Equal(["On main: WIP: greeting experiment", "Document options", "Add changelog", "Greet the world", "Add usage doc", "Print greeting", "Add app skeleton", "Initial commit"], active);
    }
}

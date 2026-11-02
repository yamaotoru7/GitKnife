using GitKnife.Core.Git;
using GitKnife.Core.Settings;

namespace GitKnife.Core.Tests;

/// <summary>Search タブ。</summary>
[Collection("sandbox")]
public class RevisionSearchTests(SandboxFixture sandbox)
{
    private async Task<IReadOnlyList<RevisionSearchResult>> Search(RevisionSearchType type, string q, RevisionSearchScope scope = RevisionSearchScope.Repository)
        => await RevisionSearch.RunAsync(sandbox.Git, sandbox.Basic, new RevisionSearchQuery(type, scope, q));

    [Fact]
    public void ArgsMatchExpected()
    {
        var a = RevisionSearch.Args(new RevisionSearchQuery(RevisionSearchType.Message, RevisionSearchScope.Repository, "greet"));
        Assert.Equal(["HEAD", "--branches", "--remotes", "--tags", "--grep=greet", "--regexp-ignore-case", "--"], a[3..]);
        var c = RevisionSearch.Args(new RevisionSearchQuery(RevisionSearchType.DiffContent, RevisionSearchScope.CurrentBranch, "hello"));
        Assert.Equal(["--name-status", "-G", "hello", "--"], c[3..]);
        var p = RevisionSearch.Args(new RevisionSearchQuery(RevisionSearchType.DiffPath, RevisionSearchScope.Repository, "login"));
        Assert.Equal(["--name-status", "--", ":(icase)login"], p[7..]);
    }

    [Fact]
    public async Task ResultsMatchExpected()
    {
        Assert.Equal(["Capitalize greeting", "Greet the world", "Print greeting"], (await Search(RevisionSearchType.Message, "greet")).Select(r => r.Subject));
        Assert.Equal(["Print greeting", "Greet the world"], (await Search(RevisionSearchType.Message, "greet", RevisionSearchScope.CurrentBranch)).Select(r => r.Subject).Order().Reverse());
        Assert.Equal(["Add changelog"], (await Search(RevisionSearchType.Author, "other")).Select(r => r.Subject));
        Assert.Empty(await Search(RevisionSearchType.DiffPath, "login"));
        var content = await Search(RevisionSearchType.DiffContent, "hello");
        Assert.Equal(["Capitalize greeting", "Greet the world", "Print greeting"], content.Select(r => r.Subject));
        Assert.All(content, r => Assert.Equal(["src/app.cs"], r.Files.Select(f => f.Path)));
    }

    [Fact]
    public void RecentQueriesRoundTripAsTableArray()
    {
        var text = "issues_recent_search_queries = []\n\n[[recent_revision_search_queries]]\ntype = \"message\"\nscope = \"currentBranch\"\nsearch_string = \"greet\"\n\n[[recent_revision_search_queries]]\ntype = \"author\"\nscope = \"repository\"\nsearch_string = \"other\"\n";
        var doc = TomlDocument.Parse(text);
        Assert.Equal(["message", "author"], doc.GetTables("recent_revision_search_queries").Select(t => t.GetString("type")));
        Assert.Equal(text, doc.ToString());
        doc.Set("recent_revision_search_queries", new List<TomlTable>());
        Assert.Equal("issues_recent_search_queries = []\nrecent_revision_search_queries = []\n", doc.ToString());
    }
}

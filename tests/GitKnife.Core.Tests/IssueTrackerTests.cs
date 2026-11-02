using GitKnife.Core.Settings;

namespace GitKnife.Core.Tests;

/// <summary>Issue Tracker のルール（.git/issuetracker の読み書き）。</summary>
public class IssueTrackerTests
{
    private static string SampleFile => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "sample-issuetracker"));

    [Fact]
    public void ReadsSampleFile()
    {
        var rules = IssueTracker.Parse(SampleFile, shared: false);
        Assert.Equal([IssueTracker.SampleGitHub, IssueTracker.SampleJira, IssueTracker.SampleJiraMultiproject], rules);
    }

    [Fact]
    public void WritesExactly()
    {
        var incomplete = new IssueTrackerRule(IssueTracker.NewRuleName, "", "", false);
        Assert.Equal(SampleFile, IssueTracker.Serialize([IssueTracker.SampleGitHub, IssueTracker.SampleJira, IssueTracker.SampleJiraMultiproject, incomplete]));
    }

    [Fact]
    public void FindsLinksWithGroups()
    {
        var links = IssueTracker.FindLinks(IssueTracker.SampleMessage + " PRJ2-7", [IssueTracker.SampleGitHub, IssueTracker.SampleJiraMultiproject]);
        Assert.Equal(2, links.Count);
        Assert.Equal(new IssueLink(42, 6, "https://github.com/username/repository/issues/12337"), links[0]);
        Assert.Equal("https://jira.yourcompany.com/browse/PRJ2-7", links[1].Url);
        Assert.False(new IssueTrackerRule("x", "(", "u", false).IsValid);
    }
}

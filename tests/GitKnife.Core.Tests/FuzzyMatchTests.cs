using GitKnife.Core.Text;

namespace GitKnife.Core.Tests;

public class FuzzyMatchTests
{
    [Fact]
    public void OrdersForFe()
    {
        // "fe"：Fetch、Fetch...、Open In File Explorer、File History...、Git Flow: Initialize...
        string[] commands = ["Apply Patch…", "File History...", "Fetch", "Fetch...", "Git Flow: Initialize...", "Open In File Explorer", "Zoom In"];
        var ordered = commands
            .Select((c, i) => (c, i, m: FuzzyMatch.Match("fe", c)))
            .Where(x => x.m is not null)
            .OrderByDescending(x => x.m!.Value.score).ThenBy(x => x.i)
            .Select(x => x.c).ToList();
        Assert.Equal(["Fetch", "Fetch...", "Open In File Explorer", "File History...", "Git Flow: Initialize..."], ordered);
    }

    [Fact]
    public void ReturnsMatchedPositions()
    {
        var m = FuzzyMatch.Match("fe", "Open In File Explorer");
        Assert.NotNull(m);
        Assert.Equal([8, 13], m.Value.positions);
        Assert.Null(FuzzyMatch.Match("xyz", "Fetch"));
    }
}

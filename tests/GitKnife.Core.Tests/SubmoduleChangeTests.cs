using GitKnife.Core.Diff;

namespace GitKnife.Core.Tests;

public class SubmoduleChangeTests
{
    private static FileDiff Parse(string text) => UnifiedDiffParser.Parse(text).Single();

    [Fact]
    public void ReadsOldAndNewCommitsAndDirtyMark()
    {
        var diff = Parse("""
            diff --git gkSrcPrefix/sublib gkDstPrefix/sublib
            index 1b01ab9ad0000000000000000000000000000000..6ab8da2a00000000000000000000000000000000 160000
            --- gkSrcPrefix/sublib
            +++ gkDstPrefix/sublib
            @@ -1 +1 @@
            -Subproject commit 1b01ab9ad0000000000000000000000000000000
            +Subproject commit 6ab8da2a00000000000000000000000000000000-dirty

            """);
        var change = SubmoduleChange.TryParse(diff);
        Assert.Equal(new SubmoduleChange("1b01ab9ad0000000000000000000000000000000", "6ab8da2a00000000000000000000000000000000", true), change);
    }

    [Fact]
    public void NewSubmoduleHasNoOldCommit()
    {
        var diff = Parse("""
            diff --git gkSrcPrefix/sublib gkDstPrefix/sublib
            new file mode 160000
            index 0000000000000000000000000000000000000000..1b01ab9ad0000000000000000000000000000000
            --- /dev/null
            +++ gkDstPrefix/sublib
            @@ -0,0 +1 @@
            +Subproject commit 1b01ab9ad0000000000000000000000000000000

            """);
        Assert.Equal(new SubmoduleChange(null, "1b01ab9ad0000000000000000000000000000000", false), SubmoduleChange.TryParse(diff));
    }

    [Fact]
    public void OrdinaryFileIsNotSubmodule()
    {
        var diff = Parse("""
            diff --git a/a.txt b/a.txt
            index 1111111..2222222 100644
            --- a/a.txt
            +++ b/a.txt
            @@ -1 +1 @@
            -old
            +new

            """);
        Assert.Null(SubmoduleChange.TryParse(diff));
    }
}

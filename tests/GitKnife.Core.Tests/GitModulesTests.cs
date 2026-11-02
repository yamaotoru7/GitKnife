using GitKnife.Core.Git;

namespace GitKnife.Core.Tests;

public class GitModulesTests
{
    [Fact]
    public void Parse_ReadsNamePathAndUrl()
    {
        var list = GitModules.Parse("[submodule \"sublib\"]\n\tpath = sublib\n\turl = D:/x/sublib\n[submodule \"libs/b\"]\r\n\tpath = \"libs/b\"\r\n\turl = https://e/b.git\r\n");
        Assert.Equal([new SubmoduleInfo("sublib", "sublib", "D:/x/sublib"), new SubmoduleInfo("libs/b", "libs/b", "https://e/b.git")], list);
    }

    [Fact]
    public void Parse_SkipsEntriesWithoutPathAndOtherSections()
    {
        var list = GitModules.Parse("# c\n[core]\n\tpath = no\n[submodule \"a\"]\n\turl = u\n");
        Assert.Empty(list);
    }
}

using GitKnife.Core;

namespace GitKnife.Core.Tests;

public class ProductInfoTests
{
    [Fact]
    public void VersionComesFromBuildProps()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+$", ProductInfo.Version);
    }

    [Fact]
    public void AppDataDirectoryIsUnderLocalAppData()
    {
        Assert.EndsWith(Path.DirectorySeparatorChar + "GitKnife", ProductInfo.AppDataDirectory);
    }
}

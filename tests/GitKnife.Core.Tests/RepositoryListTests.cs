using GitKnife.Core.Settings;

namespace GitKnife.Core.Tests;

/// <summary>repositories.toml のタブの色。</summary>
public class RepositoryListTests
{
    private const string SampleFile = "source_dirs = ['D:\\Proj\\']\nscan_depth = 5\nignore = []\n\n[[repository]]\npath = 'D:\\Proj\\a'\nopened = 1750482446\n\n[[repository]]\npath = 'D:\\Proj\\tools\\GitKnife\\.work\\sandbox\\basic'\nopened = 1792950586\ncolor = \"Red\"\n";

    [Fact]
    public void ReadsFileAndSetsAndClearsColor()
    {
        var path = Path.Combine(Path.GetTempPath(), "gk-repolist-" + Guid.NewGuid().ToString("N") + ".toml");
        File.WriteAllText(path, SampleFile);
        try
        {
            var list = RepositoryList.Load(path);
            Assert.Equal("Red", list.GetColor(@"D:\Proj\tools\GitKnife\.work\sandbox\basic"));
            Assert.Equal("Red", list.GetColor("d:/proj/tools/gitknife/.work/sandbox/basic/"));   // 大文字小文字と区切りは区別しない
            Assert.Null(list.GetColor(@"D:\Proj\a"));

            list.SetColor(@"D:\Proj\a", "Violet");
            list.SetColor(@"D:\Proj\tools\GitKnife\.work\sandbox\basic", null);
            list.SetColor(@"D:\New", "Blue");

            var again = RepositoryList.Load(path);
            Assert.Equal("Violet", again.GetColor(@"D:\Proj\a"));
            Assert.Null(again.GetColor(@"D:\Proj\tools\GitKnife\.work\sandbox\basic"));
            Assert.Equal("Blue", again.GetColor(@"D:\New"));
            var doc = TomlDocument.Load(path);
            Assert.Equal([@"D:\Proj\"], doc.GetStringArray("source_dirs"));
            Assert.Equal(5, doc.GetInt("scan_depth", 0));
            Assert.Equal(1792950586L, doc.GetTables("repository")[1].Entries.First(e => e.key == "opened").value);
        }
        finally { File.Delete(path); }
    }

    /// <summary>Rename は alias、Remove は表を消して ignore に足す。パスは '…' で書き戻す。</summary>
    [Fact]
    public void AliasRemoveAndLiteralPaths()
    {
        var path = Path.Combine(Path.GetTempPath(), "gk-repolist-" + Guid.NewGuid().ToString("N") + ".toml");
        File.WriteAllText(path, SampleFile);
        try
        {
            var list = RepositoryList.Load(path);
            list.SetAlias(@"D:\Proj\a", "Renamed");
            Assert.Equal("Renamed", RepositoryList.Load(path).Get(@"D:\Proj\a")!.Name);
            list.Remove([@"D:\Proj\a"]);

            var text = File.ReadAllText(path).Replace("\r", "");
            Assert.StartsWith("source_dirs = ['D:\\Proj\\']\nscan_depth = 5\nignore = ['D:\\Proj\\a']\n", text);
            Assert.Contains("path = 'D:\\Proj\\tools\\GitKnife\\.work\\sandbox\\basic'\nopened = 1792950586\ncolor = \"Red\"", text);
            Assert.DoesNotContain("Renamed", text);
            var again = RepositoryList.Load(path);
            Assert.Single(again.Repositories);
            Assert.Equal([@"D:\Proj\a"], again.Ignore);

            again.SetAlias(@"D:\Proj\tools\GitKnife\.work\sandbox\basic", "sandbox");
            Assert.Contains("path = 'D:\\Proj\\tools\\GitKnife\\.work\\sandbox\\basic'\nalias = \"sandbox\"\nopened = 1792950586\ncolor = \"Red\"", File.ReadAllText(path).Replace("\r", ""));
            RepositoryList.Load(path).SetAlias(@"D:\Proj\tools\GitKnife\.work\sandbox\basic", "basic");   // フォルダ名に戻すと alias は消える
            Assert.Null(RepositoryList.Load(path).Get(@"D:\Proj\tools\GitKnife\.work\sandbox\basic")!.Alias);
        }
        finally { File.Delete(path); }
    }

    /// <summary>Quick Launch の Recent：opened の新しい順、同じ秒なら先に記録した方が先。</summary>
    [Fact]
    public void RecentOrdersByOpenedThenByTouchOrder()
    {
        var path = Path.Combine(Path.GetTempPath(), "gk-repolist-" + Guid.NewGuid().ToString("N") + ".toml");
        File.WriteAllText(path, SampleFile);
        try
        {
            var list = RepositoryList.Load(path);
            Assert.Equal([@"D:\Proj\tools\GitKnife\.work\sandbox\basic", @"D:\Proj\a"], list.Recent(8));

            var now = DateTimeOffset.FromUnixTimeSeconds(1800000000);
            list.Touch(@"D:\Proj\tools\GitKnife\.work\sandbox\basic", now);
            list.Touch(@"D:\Proj\a", now);
            list.Touch(@"D:\New", now);
            list.Touch(@"D:\Proj\a", now);   // 同じ秒に記録し直すと後ろへ

            var again = RepositoryList.Load(path);
            Assert.Equal([@"D:\Proj\tools\GitKnife\.work\sandbox\basic", @"D:\New", @"D:\Proj\a"], again.Recent(8));
            Assert.Equal("Red", again.GetColor(@"D:\Proj\tools\GitKnife\.work\sandbox\basic"));   // 色は残る
            Assert.Single(again.Recent(1));
        }
        finally { File.Delete(path); }
    }
}

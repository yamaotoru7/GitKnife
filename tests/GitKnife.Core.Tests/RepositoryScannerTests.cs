using GitKnife.Core.Settings;

namespace GitKnife.Core.Tests;

public class RepositoryScannerTests
{
    [Fact]
    public void FindsRepositoriesAndBuildsCategories()
    {
        var root = Path.Combine(Path.GetTempPath(), "gk-scan-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "Apps", "libs", "Utility", ".git"));
            Directory.CreateDirectory(Path.Combine(root, "Apps", "libs", "Utility", "sub", "nested", ".git"));   // リポジトリの中には入らない
            Directory.CreateDirectory(Path.Combine(root, "Photo", ".git"));
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(root, "wt")).FullName, ".git"), "gitdir: x");   // worktree の .git ファイル
            Directory.CreateDirectory(Path.Combine(root, "a", "b", "c", "deep", ".git"));   // 深すぎる

            var found = RepositoryScanner.Scan(root, 3);
            Assert.Equal(["Utility", "Photo", "wt"], found.Select(f => Path.GetFileName(f.Path)));
            Assert.Equal(["Apps", "libs"], found[0].Folders);

            // repositories.toml に足す：ignore のものは足さない、2 回目は何も足さない、Reset previously deleted なら ignore を空にして足す
            var file = Path.Combine(root, "repositories.toml");
            var list = RepositoryList.Load(file);
            list.SourceDirectories = [root];
            var utility = found[0].Path;
            list.Add(found[1].Path);
            list.Remove([found[1].Path]);   // Photo を消したことにする（ignore に入る）
            Assert.Equal(2, list.AddScanned(found.Select(f => f.Path), resetDeleted: false));
            Assert.Equal(0, RepositoryList.Load(file).AddScanned(found.Select(f => f.Path), resetDeleted: false));
            Assert.Equal(1, RepositoryList.Load(file).AddScanned(found.Select(f => f.Path), resetDeleted: true));
            var again = RepositoryList.Load(file);
            Assert.Empty(again.Ignore);
            Assert.Equal(3, again.Repositories.Count);

            // 木：ソースフォルダの下の途中のフォルダ
            var tree = RepositoryTree.Build(again.Repositories, again.SourceDirectories);
            Assert.Equal(["Apps"], tree.Folders.Select(f => f.Name));
            Assert.Equal(["libs"], tree.Folders[0].Folders.Select(f => f.Name));
            Assert.Equal(utility, tree.Folders[0].Folders[0].Repositories.Single().Path);
            Assert.Equal(["Photo", "wt"], tree.Repositories.Select(r => r.Name));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    /// <summary>Repository Manager の木：外のリポジトリはフォルダに入らず、alias が名前。</summary>
    [Fact]
    public void BuildsTreeFromPaths()
    {
        var entries = new RepositoryListEntry[]
        {
            new(@"D:\Proj\_external\pytube", null, null, null),
            new(@"D:\Proj\Apps\libs\Utility", null, null, null),
            new(@"D:\Proj\tools\GitKnife\.work\sandbox\basic", null, null, null),
            new(@"D:\Proj\Clipper1-6.4.2", "ClipperRenamed", null, null),
            new(@"U:\home\user\AI\BPM", null, null, null),
            new(@"C:\proj\notes", null, null, null),
        };
        var tree = RepositoryTree.Build(entries, [@"D:\Proj\"]);
        Assert.Equal(["_external", "Apps", "tools"], tree.Folders.Select(f => f.Name));
        Assert.Equal(["BPM", "ClipperRenamed", "notes"], tree.Repositories.Select(r => r.Name));
        var sandbox = tree.Folders[2].Folders.Single().Folders.Single().Folders.Single();
        Assert.Equal(@"tools\GitKnife\.work\sandbox", sandbox.Key);
        Assert.Equal("basic", sandbox.Repositories.Single().Name);
        Assert.Single(tree.Folders[2].AllRepositories);
    }
}

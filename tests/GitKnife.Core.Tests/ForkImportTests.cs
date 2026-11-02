using GitKnife.Core.Settings;

namespace GitKnife.Core.Tests;

public class ForkImportTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GitKnife.Tests", Guid.NewGuid().ToString("N"));

    public ForkImportTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string WriteForkSettings(string json)
    {
        var path = Path.Combine(_dir, "fork-settings.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public void ReplacesWorkspacesRepositoryManagerAndRepositoryList()
    {
        var forkSettings = WriteForkSettings("""
            {
              "Guid": "fork",
              "RepositoryManager": { "SourceDirectories": ["D:\\Proj"], "Categories": [], "Repositories": [], "ScanDepth": 2 },
              "Workspaces": { "All": [
                { "Name": "Home", "Repositories": ["D:\\a", "D:\\b"], "ActiveRepository": "D:\\b" },
                { "Name": "Work/x", "Repositories": [], "ActiveRepository": null } ],
                "ActiveWorkspace": "Work/x", "ShowInTitle": true }
            }
            """);
        var forkList = Path.Combine(_dir, "fork-repositories.toml");
        File.WriteAllText(forkList, "source_dirs = ['D:\\Proj\\']\n\n[[repository]]\npath = 'D:\\a'\ncolor = \"Blue\"\n");
        var gkSettings = Path.Combine(_dir, "gk", "settings.json");
        var gkList = Path.Combine(_dir, "gk", "repositories.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(gkList)!);
        File.WriteAllText(gkList, "[[repository]]\npath = 'D:\\old'\n");
        var target = new AppSettings { Guid = "gitknife", DiffContextSize = 7 };
        target.Workspaces.All.Add(new Workspace { Name = "Old" });
        SettingsStore.Save(target, gkSettings);

        var source = ForkImport.Read(forkSettings, forkList);
        Assert.NotNull(source);
        ForkImport.Apply(source, target, gkSettings, gkList);

        Assert.Equal(["Home", "Work/x"], target.Workspaces.All.Select(w => w.Name));
        Assert.Equal("Work/x", target.Workspaces.ActiveWorkspace);
        Assert.True(target.Workspaces.ShowInTitle);
        Assert.Equal("D:\\b", target.Workspaces.All[0].ActiveRepository);
        Assert.Equal(2, target.RepositoryManager.ScanDepth);
        // ほかの設定はそのまま
        Assert.Equal("gitknife", target.Guid);
        Assert.Equal(7, target.DiffContextSize);
        // 一覧と色は Fork のもの、前のものは控えに
        Assert.Equal("Blue", RepositoryList.Load(gkList).GetColor("D:\\a"));
        Assert.Contains("D:\\old", File.ReadAllText(gkList + ForkImport.BackupSuffix));
        Assert.Contains("\"Old\"", File.ReadAllText(gkSettings + ForkImport.BackupSuffix));
    }

    [Fact]
    public void KeepsRepositoryListWhenForkHasNone()
    {
        var forkSettings = WriteForkSettings("""{ "Workspaces": { "All": [], "ActiveWorkspace": "Home", "ShowInTitle": false } }""");
        var gkList = Path.Combine(_dir, "repositories.toml");
        File.WriteAllText(gkList, "[[repository]]\npath = 'D:\\old'\n");
        var source = ForkImport.Read(forkSettings, Path.Combine(_dir, "missing.toml"))!;
        Assert.Null(source.RepositoryListPath);
        Assert.Null(source.RepositoryManager);
        var target = new AppSettings();
        target.RepositoryManager.ScanDepth = 9;
        ForkImport.Apply(source, target, Path.Combine(_dir, "settings.json"), gkList);
        Assert.Equal(9, target.RepositoryManager.ScanDepth);
        Assert.Contains("D:\\old", File.ReadAllText(gkList));
    }

    [Fact]
    public void ReturnsNullWithoutForkSettings()
    {
        Assert.Null(ForkImport.Read(Path.Combine(_dir, "missing.json")));
        Assert.Null(ForkImport.Read(WriteForkSettings("""{ "Guid": "x" }""")));
    }
}

using System.Text.Json;
using GitKnife.Core.Settings;

namespace GitKnife.Core.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "GitKnife.Tests", Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void DefaultsMatchExpectedValues()
    {
        var s = new AppSettings();
        Assert.Equal(19, s.DiffContextSize);
        Assert.Equal(50, s.CommitSubjectLowLimit);
        Assert.Equal(70, s.CommitSubjectHighLimit);
        Assert.Equal(1, s.MergeType);
        Assert.True(s.Pull_Rebase);
        Assert.Equal("-", s.ReferenceSpaceCharacterReplacement);
        Assert.Equal("Home", s.Workspaces.ActiveWorkspace);
    }

    [Fact]
    public void RoundTripsAndKeepsUnknownKeys()
    {
        var path = Path.Combine(_dir, "settings.json");
        // settings.json の形（知らないキー SeenNewYear2026 を含む）
        File.WriteAllText(path, """
            {
              "Guid": "11111111-2222-3333-4444-555555555555",
              "DiffContextSize": 7,
              "SeenNewYear2026": true,
              "RepositoryManager": { "SourceDirectories": ["D:\\Proj\\"], "Categories": [], "Repositories": [
                 { "Id": 3, "ParentId": 0, "Name": "basic", "Path": "D:\\x\\basic", "GitDirectoryPath": "D:\\x\\basic\\.git", "LastAccessTime": "2026-10-24T22:00:00+09:00" } ], "ScanDepth": 2 },
              "Workspaces": { "All": [ { "Name": "Home", "Repositories": ["D:\\x\\basic"], "ActiveRepository": "D:\\x\\basic" } ], "ActiveWorkspace": "Home", "ShowInTitle": false }
            }
            """);
        var s = SettingsStore.Load(path);
        Assert.Equal(7, s.DiffContextSize);
        Assert.Equal(2, s.RepositoryManager.ScanDepth);
        Assert.Equal("basic", s.RepositoryManager.Repositories[0].Name);
        Assert.True(s.Extra.ContainsKey("SeenNewYear2026"));

        s.DiffContextSize = 3;
        SettingsStore.Save(s, path);
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(3, doc.RootElement.GetProperty("DiffContextSize").GetInt32());
        Assert.True(doc.RootElement.GetProperty("SeenNewYear2026").GetBoolean());
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("MainWindowLocationState").ValueKind);
    }

    [Fact]
    public void BrokenFileFallsBackToDefaults()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path, "{ not json");
        var s = SettingsStore.Load(path);
        Assert.Equal(19, s.DiffContextSize);
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public void TomlParsesRepositoryConfig()
    {
        var doc = TomlDocument.Parse("""
            pinned_references = ["refs/heads/main", "refs/tags/v1"]
            sign_off = false
            tab_width = 4
            show_bugtracker_links = true
            draft_message = "hello \"world\"\nline2"
            collapse_toggled_shas = []
            """);
        Assert.Equal(new[] { "refs/heads/main", "refs/tags/v1" }, doc.GetStringArray("pinned_references"));
        Assert.False(doc.GetBool("sign_off", true));
        Assert.Equal(4, doc.GetInt("tab_width", 0));
        Assert.Equal("hello \"world\"\nline2", doc.GetString("draft_message"));
        Assert.Empty(doc.GetStringArray("collapse_toggled_shas"));
        // 書き戻しは同じ順序とクォート。改行を含む文字列は複数行の文字列にする（draft_message。DraftMessageUsesMultilineString）
        Assert.Equal(
            "pinned_references = [\"refs/heads/main\", \"refs/tags/v1\"]\n" +
            "sign_off = false\n" +
            "tab_width = 4\n" +
            "show_bugtracker_links = true\n" +
            "draft_message = \"\"\"\nhello \\\"world\\\"\nline2\"\"\"\n" +
            "collapse_toggled_shas = []\n", doc.ToString());
    }

    [Fact]
    public void RepositoryConfigWritesCompatibleFile()
    {
        var gitDir = Path.Combine(_dir, ".git");
        var cfg = RepositoryConfig.Load(gitDir);
        cfg.EnsureDefaults();
        cfg.TabWidth = 2;
        cfg.Save();
        var text = File.ReadAllText(Path.Combine(gitDir, "gitknife", "config.toml"));
        Assert.StartsWith("pinned_references = []\nsign_off = false\ntab_width = 2\n", text);

        var wt = WorktreeConfig.Load(gitDir);
        wt.EnsureDefaults();
        wt.HiddenReferences = ["refs/remotes/origin/old"];
        wt.Save();
        var again = WorktreeConfig.Load(gitDir);
        Assert.Equal(["refs/remotes/origin/old"], again.HiddenReferences);
        Assert.Equal("", again.DraftMessage);
    }

    /// <summary>コミットの下書き（worktree-config.toml の draft_message。件名と本文は複数行の文字列で書く）。</summary>
    [Fact]
    public void DraftMessageUsesMultilineString()
    {
        const string sample = "draft_message = \"\"\"\nDraft subject\n\nBody line\"\"\"\nhide_stashes = false\n";
        var doc = TomlDocument.Parse(sample);
        Assert.Equal("Draft subject\n\nBody line", doc.GetString("draft_message"));
        Assert.False(doc.GetBool("hide_stashes", true));
        Assert.Equal(sample, doc.ToString());
        var single = TomlDocument.Parse("draft_message = \"Draft subject\"\n");
        Assert.Equal("Draft subject", single.GetString("draft_message"));
    }
}

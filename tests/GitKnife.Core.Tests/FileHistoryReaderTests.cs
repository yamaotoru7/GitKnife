using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;

namespace GitKnife.Core.Tests;

/// <summary>File History と Blame（Blame は 8 行の表示）。</summary>
[Collection("sandbox")]
public class FileHistoryReaderTests(SandboxFixture sandbox)
{
    private FileHistoryReader Reader() => new(sandbox.Git, sandbox.Basic);

    [Fact]
    public async Task HistoryListsCommitsThatTouchedTheFile()
    {
        var history = await Reader().ReadHistoryAsync("src/app.cs");
        Assert.Equal(["Greet the world", "Print greeting", "Add app skeleton"], history.Select(h => h.Subject));
        Assert.Equal([FileChange.Modified, FileChange.Modified, FileChange.Added], history.Select(h => h.Change));
        Assert.All(history, h => Assert.Equal("src/app.cs", h.Path));
        var diff = await Reader().ReadDiffAsync(history[0], hasParent: true, fullFile: false);
        Assert.Contains(diff!.Hunks[0].Lines, l => l.DisplayText.Contains("hello, world"));
    }

    /// <summary>フォルダの History：コミットごとに中の変わったファイル（src は 3 コミット、どれも src/app.cs）。ほかのブランチ（bugfix/greeting）は入らない。</summary>
    [Fact]
    public async Task FolderHistoryGroupsFilesByCommit()
    {
        var commits = await Reader().ReadFolderHistoryAsync("src");
        Assert.Equal(["Greet the world", "Print greeting", "Add app skeleton"], commits.Select(c => c.Subject));
        Assert.All(commits, c => Assert.Equal(["src/app.cs"], c.Files.Select(f => f.Path)));
        Assert.Equal(FileChange.Added, commits[^1].Files[0].Change);
    }

    [Fact]
    public async Task BlameAttributesEachLine()
    {
        var r = Reader();
        var last = await r.ReadLastRevisionAsync("src/app.cs");
        var history = await r.ReadHistoryAsync("src/app.cs");
        Assert.Equal(history[0].Sha, last);
        var blame = await r.BlameAsync(last!, "src/app.cs");
        // Blame の左列：1-2 行 Add app skeleton、3-4 行 Print greeting、5 行 Greet the world、6 行 Print greeting、7 行 Add app skeleton
        Assert.Equal(["Add app skeleton", "Add app skeleton", "Print greeting", "Print greeting", "Greet the world", "Print greeting", "Add app skeleton"],
            blame.Select(b => b.Summary));
        Assert.Equal(Enumerable.Range(1, 7), blame.Select(b => b.FinalLine));
    }

    [Fact]
    public async Task BlameLayoutInterleavesRemovedLines()
    {
        var r = Reader();
        var history = await r.ReadHistoryAsync("src/app.cs");
        var head = history[0];
        var diff = await r.ReadDiffAsync(head, hasParent: true, fullFile: true);
        var rows = Core.Diff.BlameLayout.Build(diff, await r.BlameAsync(head.Sha, "src/app.cs"), await r.BlameAsync(head.Sha + "~", "src/app.cs"));
        // Blame：8 行（消した "hello" の行を挟む）。左の列のまとまりは 5 つ（Add app skeleton / Print greeting / Greet the world / Print greeting / Add app skeleton）
        Assert.Equal(8, rows.Count);
        Assert.Equal(Core.Diff.DiffLineKind.Removed, rows[4].Kind);
        Assert.Equal("Print greeting", rows[4].Blame!.Summary);
        Assert.Equal(["Add app skeleton", "Print greeting", "Greet the world", "Print greeting", "Add app skeleton"],
            rows.Where(x => x.StartsGroup).Select(x => x.Blame!.Summary));
    }
}

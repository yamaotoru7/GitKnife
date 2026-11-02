using GitKnife.Core.Diff;
using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;
using GitKnife.Testing;

namespace GitKnife.Core.Tests;

/// <summary>
/// Local Changes の操作。書き込むのでテストごとに sandbox を作る。
/// basic の初期状態：README.md が unstaged の変更、docs/todo.md が staged の新規、notes.txt が untracked。
/// </summary>
[Collection("sandbox")]
public class WorkingTreeTests(SandboxFixture sandbox) : IDisposable
{
    private readonly List<string> _roots = [];

    private async Task<(RepositoryReader reader, WorkingTree tree)> Fresh()
    {
        var path = SandboxFixture.CreateFresh(out var root);
        _roots.Add(root);
        var reader = (await RepositoryReader.OpenAsync(sandbox.Git, path))!;
        return (reader, reader.CreateWorkingTree());
    }

    public void Dispose()
    {
        foreach (var r in _roots) try { SandboxBuilder.DeleteDirectory(r); } catch { }
    }

    private static StatusEntry Entry(RepositoryStatus s, string path) => s.Entries.Single(e => e.Path == path);

    [Fact]
    public async Task ReadsDiffForEachList()
    {
        var (reader, tree) = await Fresh();
        var status = await reader.ReadStatusAsync();

        var readme = await tree.ReadDiffAsync(Entry(status, "README.md"), staged: false, context: 19);
        Assert.Equal("README.md", readme!.Path);
        Assert.Equal(2, readme.Additions);   // 空行と "（unstaged の変更）"

        var todo = await tree.ReadDiffAsync(Entry(status, "docs/todo.md"), staged: true, context: 19);
        Assert.True(todo!.IsNew);

        var notes = await tree.ReadDiffAsync(Entry(status, "notes.txt"), staged: false, context: 19);
        Assert.True(notes!.IsNew);
        Assert.Equal("untracked file", notes.Hunks[0].Lines[0].DisplayText);
    }

    [Fact]
    public async Task StageAndUnstageNewAndExistingFiles()
    {
        var (reader, tree) = await Fresh();
        await tree.StageAsync(["README.md", "notes.txt"]);
        var status = await reader.ReadStatusAsync();
        Assert.Equal(["README.md", "docs/todo.md", "notes.txt"], status.Staged.Select(e => e.Path).Order(StringComparer.Ordinal));
        Assert.Empty(status.Unstaged);

        await tree.UnstageAsync(status.Staged);
        status = await reader.ReadStatusAsync();
        Assert.Empty(status.Staged);
        Assert.Equal(FileChange.Untracked, Entry(status, "docs/todo.md").WorkTree);
        Assert.Equal(FileChange.Modified, Entry(status, "README.md").WorkTree);
    }

    [Fact]
    public async Task DiscardRestoresTrackedAndDeletesUntracked()
    {
        var (reader, tree) = await Fresh();
        var status = await reader.ReadStatusAsync();
        await tree.DiscardAsync([Entry(status, "README.md"), Entry(status, "notes.txt")]);
        status = await reader.ReadStatusAsync();
        Assert.Equal(["docs/todo.md"], status.Entries.Select(e => e.Path));
        Assert.False(File.Exists(Path.Combine(reader.WorkingDirectory, "notes.txt")));
    }

    [Fact]
    public async Task StagesAndUnstagesSelectedLines()
    {
        var (reader, tree) = await Fresh();
        var status = await reader.ReadStatusAsync();
        var diff = (await tree.ReadDiffAsync(Entry(status, "README.md"), staged: false, context: 3))!;
        var hunk = Assert.Single(diff.Hunks);
        var last = hunk.Lines.FindLastIndex(l => l.Kind == DiffLineKind.Added);

        // 最後の追加行だけステージ → index には空行なしで 1 行だけ増える
        await tree.ApplyPatchAsync(PatchBuilder.Build(diff, hunk, new HashSet<int> { last }, PatchTarget.Stage)!, PatchTarget.Stage);
        var staged = (await tree.ReadDiffAsync(Entry(await reader.ReadStatusAsync(), "README.md"), staged: true, context: 3))!;
        Assert.Equal(1, staged.Additions);
        Assert.Equal("（unstaged の変更）", staged.Hunks[0].Lines.Single(l => l.Kind == DiffLineKind.Added).DisplayText);
        var unstaged = (await tree.ReadDiffAsync(Entry(await reader.ReadStatusAsync(), "README.md"), staged: false, context: 3))!;
        Assert.Equal(1, unstaged.Additions);

        // ステージした行を戻す
        var sh = staged.Hunks[0];
        var idx = sh.Lines.FindIndex(l => l.Kind == DiffLineKind.Added);
        await tree.ApplyPatchAsync(PatchBuilder.Build(staged, sh, new HashSet<int> { idx }, PatchTarget.Unstage)!, PatchTarget.Unstage);
        status = await reader.ReadStatusAsync();
        Assert.DoesNotContain(status.Staged, e => e.Path == "README.md");
    }

    [Fact]
    public async Task DiscardsHunkAndHandlesCrlf()
    {
        var (reader, tree) = await Fresh();
        var file = Path.Combine(reader.WorkingDirectory, "src", "crlf.txt");
        await File.WriteAllTextAsync(file, "a\r\nb\r\nc\r\n");
        await tree.StageAsync(["src/crlf.txt"]);
        await File.WriteAllTextAsync(file, "a\r\nB\r\nc\r\nd\r\n");
        var status = await reader.ReadStatusAsync();
        var diff = (await tree.ReadDiffAsync(Entry(status, "src/crlf.txt"), staged: false, context: 3))!;
        Assert.Equal("B", diff.Hunks[0].Lines.First(l => l.Kind == DiffLineKind.Added).DisplayText);

        // "b" → "B" だけ捨てる（"d" の追加は残る）
        var h = diff.Hunks[0];
        var sel = new HashSet<int>(h.Lines.Select((l, i) => (l, i)).Where(x => x.l.DisplayText is "b" or "B").Select(x => x.i));
        await tree.ApplyPatchAsync(PatchBuilder.Build(diff, h, sel, PatchTarget.Discard)!, PatchTarget.Discard);
        Assert.Equal("a\r\nb\r\nc\r\nd\r\n", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task ReadsMergeConflictState()
    {
        var (reader, tree) = await Fresh();
        await tree.CommitAsync(WorkingTree.ComposeMessage("Add todo", null), amend: false);
        await tree.DiscardAsync((await reader.ReadStatusAsync()).Entries);
        // bugfix/greeting は main と src/app.cs で競合する
        var r = await sandbox.Git.RunAsync(new GitRequest(reader.WorkingDirectory, ["merge", "bugfix/greeting", "--no-ff"]));
        Assert.False(r.Succeeded);
        var refs = await reader.ReadReferencesAsync();
        Assert.Equal(RepositoryOperation.Merge, refs.Operation);
        var state = reader.ReadOperationState(refs);
        Assert.Equal(("bugfix/greeting", "main"), (state.Other, state.Target));
        Assert.Single((await reader.ReadStatusAsync()).Entries, e => e.IsConflicted);

        // 競合の案内のカード：両側でそのファイルを変えたコミット（"a03091 Greet the world" / "940122 Capitalize greeting"）
        var (ours, theirs) = await tree.ReadConflictCommitsAsync("src/app.cs");
        Assert.Equal("Greet the world", Assert.Single(ours).Subject);
        Assert.Equal("Capitalize greeting", Assert.Single(theirs).Subject);

        // Resolver：theirs を採ると解決済み。結果を書いて add すると競合が消える
        var res = new MergeResolution(await tree.ReadWorkingFileAsync("src/app.cs"));
        Assert.Equal(1, res.ConflictCount);
        Assert.Contains(MergeResolution.Placeholder, res.BuildMerged().text);
        res.Toggle(0, MergeResolution.Side.Theirs);
        Assert.True(res.IsResolved);
        Assert.DoesNotContain("<<<<<<<", res.MergedFileText());
        Assert.False(res.MergedFileText().EndsWith("\n\n"));
        await tree.SaveResolvedAsync("src/app.cs", res.MergedFileText());
        Assert.DoesNotContain((await reader.ReadStatusAsync()).Entries, e => e.IsConflicted);
    }

    [Fact]
    public async Task ResolvesUsingOneSide()
    {
        var (reader, tree) = await Fresh();
        await tree.CommitAsync(WorkingTree.ComposeMessage("Add todo", null), amend: false);
        await tree.DiscardAsync((await reader.ReadStatusAsync()).Entries);
        await sandbox.Git.RunAsync(new GitRequest(reader.WorkingDirectory, ["merge", "bugfix/greeting", "--no-ff"]));
        await tree.ResolveUsingAsync("src/app.cs", ours: true);
        Assert.DoesNotContain((await reader.ReadStatusAsync()).Entries, e => e.IsConflicted);
        Assert.Contains("hello, world", await tree.ReadWorkingFileAsync("src/app.cs"));   // main 側の内容
    }

    [Fact]
    public async Task CommitsWithMessageFile()
    {
        var (reader, tree) = await Fresh();
        await tree.CommitAsync(WorkingTree.ComposeMessage("Add todo", "Line 1\r\nLine 2"), amend: false);
        Assert.Equal("Add todo\n\nLine 1\nLine 2", await tree.ReadLastCommitMessageAsync());
        Assert.True(File.Exists(tree.CommitMessagePath));

        await tree.CommitAsync(WorkingTree.ComposeMessage("Add todo (amended)", null), amend: true);
        Assert.Equal("Add todo (amended)", await tree.ReadLastCommitMessageAsync());
        var refs = await reader.ReadReferencesAsync();
        Assert.Equal(2, refs.Head!.Ahead);   // 元の ahead 1 ＋ 今回の 1（amend で増えない）
    }
}

using GitKnife.Core.Diff;
using GitKnife.Core.Git;
using GitKnife.Core.Git.Commands;

namespace GitKnife.Core.Tests;

[Collection("sandbox")]
public class DiffParserTests(SandboxFixture sandbox)
{
    [Fact]
    public void ParsesPrefixedModification()
    {
        var diff = """
            diff --git gkSrcPrefix/src/app.cs gkDstPrefix/src/app.cs
            index 1111111111111111111111111111111111111111..2222222222222222222222222222222222222222 100644
            --- gkSrcPrefix/src/app.cs
            +++ gkDstPrefix/src/app.cs
            @@ -1,4 +1,5 @@ class App
             class App
             {
            -    static void Main() { }
            +    static void Main()
            +    { }
             }
            \ No newline at end of file
            """;
        var f = Assert.Single(UnifiedDiffParser.Parse(diff));
        Assert.Equal("src/app.cs", f.OldPath);
        Assert.Equal("src/app.cs", f.NewPath);
        Assert.Equal("100644", f.OldMode);
        var h = Assert.Single(f.Hunks);
        Assert.Equal("class App", h.Section);
        Assert.Equal((1, 4, 1, 5), (h.OldStart, h.OldCount, h.NewStart, h.NewCount));
        Assert.Equal(2, f.Additions);
        Assert.Equal(1, f.Deletions);
        var removed = h.Lines.Single(l => l.Kind == DiffLineKind.Removed);
        Assert.Equal(3, removed.OldNumber);
        Assert.Equal(0, removed.NewNumber);
        Assert.Equal(4, h.Lines.Last(l => l.Kind == DiffLineKind.Context).NewNumber + 0 - 1);   // "}" は新しい側の 5 行目
        Assert.Equal(DiffLineKind.NoNewlineAtEnd, h.Lines[^1].Kind);
    }

    [Fact]
    public void ParsesNewRenameModeAndBinary()
    {
        var diff = """
            diff --git a/new.txt b/new.txt
            new file mode 100644
            index 0000000..e69de29
            --- /dev/null
            +++ b/new.txt
            @@ -0,0 +1 @@
            +hello
            diff --git a/old name.txt b/new name.txt
            similarity index 90%
            rename from old name.txt
            rename to new name.txt
            diff --git a/run.sh b/run.sh
            old mode 100644
            new mode 100755
            diff --git a/img.png b/img.png
            index 1234567..89abcde 100644
            Binary files a/img.png and b/img.png differ
            diff --git "a/\346\227\245\346\234\254.txt" "b/\346\227\245\346\234\254.txt"
            index 1111111..2222222 100644
            --- "a/\346\227\245\346\234\254.txt"
            +++ "b/\346\227\245\346\234\254.txt"
            @@ -1 +1 @@
            -a
            +b
            """;
        var files = UnifiedDiffParser.Parse(diff);
        Assert.Equal(5, files.Count);
        Assert.True(files[0].IsNew);
        Assert.Equal("new.txt", files[0].Path);
        Assert.Equal(1, files[0].Hunks[0].NewCount);
        Assert.True(files[1].IsRename);
        Assert.Equal("old name.txt", files[1].OldPath);
        Assert.Equal("new name.txt", files[1].NewPath);
        Assert.Equal(90, files[1].Similarity);
        Assert.True(files[2].ModeChanged);
        Assert.True(files[3].IsBinary);
        Assert.Equal("日本.txt", files[4].Path);
    }

    [Fact]
    public async Task ParsesRealGitOutputFromSandbox()
    {
        var r = await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, GitArgs.DiffWorkingTree("README.md", 19)));
        var f = Assert.Single(UnifiedDiffParser.Parse(r.StandardOutput));
        Assert.Equal("README.md", f.Path);
        Assert.Equal(2, f.Additions);   // 空行と "（unstaged の変更）"
        var untracked = await sandbox.Git.RunAsync(new GitRequest(sandbox.Basic, GitArgs.DiffUntracked("notes.txt", 19)));
        Assert.Equal(1, untracked.ExitCode);   // 差分ありは exit 1
        Assert.True(Assert.Single(UnifiedDiffParser.Parse(untracked.StandardOutput)).IsNew);
    }

    [Fact]
    public void InlineDiffFindsChangedTokens()
    {
        var (removed, added) = InlineDiff.Compute("System.Console.WriteLine(\"hello, world\");", "System.Console.WriteLine(\"Hello!\");");
        var r = Assert.Single(removed);
        var a = Assert.Single(added);
        Assert.Equal("hello, world", "System.Console.WriteLine(\"hello, world\");".Substring(r.Start, r.Length));
        Assert.Equal("Hello!", "System.Console.WriteLine(\"Hello!\");".Substring(a.Start, a.Length));
    }

    [Fact]
    public void ConflictParserHandlesMergeAndDiff3()
    {
        var text = """
            class App
            {
            <<<<<<< HEAD
                    System.Console.WriteLine("hello, world");
            ||||||| 4f248f5
                    System.Console.WriteLine("hello");
            =======
                    System.Console.WriteLine("Hello!");
            >>>>>>> bugfix/greeting
            }

            """;
        var sections = ConflictParser.Parse(text);
        Assert.Equal(3, sections.Count);
        var block = Assert.IsType<ConflictBlock>(sections[1]);
        Assert.Equal("HEAD", block.OursLabel);
        Assert.Equal("bugfix/greeting", block.TheirsLabel);
        Assert.Equal("4f248f5", block.BaseLabel);
        Assert.Single(block.Ours);
        Assert.Single(block.Base!);
        var resolved = ConflictParser.Resolve(sections, (_, _) => ConflictParser.Choice.Theirs);
        Assert.Equal("class App\n{\n        System.Console.WriteLine(\"Hello!\");\n}\n", resolved);
    }

    [Fact]
    public void ConflictParserIgnoresUnclosedMarkers()
    {
        var sections = ConflictParser.Parse("<<<<<<< not a conflict\nline\n");
        var ctx = Assert.IsType<ContextSection>(Assert.Single(sections));
        Assert.Equal(2, ctx.Lines.Count);
    }
}

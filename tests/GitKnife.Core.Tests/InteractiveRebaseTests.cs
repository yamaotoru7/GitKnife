using GitKnife.Core.Git;
using GitKnife.Testing;

namespace GitKnife.Core.Tests;

[Collection("sandbox")]
public class InteractiveRebaseTests(SandboxFixture sandbox) : IDisposable
{
    private readonly List<string> _roots = [];

    public void Dispose()
    {
        foreach (var r in _roots) try { SandboxBuilder.DeleteDirectory(r); } catch { }
    }

    private const string A = "1111111111111111111111111111111111111111";
    private const string B = "2222222222222222222222222222222222222222";
    private const string C = "3333333333333333333333333333333333333333";

    // git が作る todo（rebase.abbreviateCommands=true、instructionFormat=#!_%H、commentChar=^、--update-refs）
    private static readonly string Todo =
        $"p 1111111 #!_{A}\np 2222222 #!_{B}\nupdate-ref refs/heads/topic\n\np 3333333 #!_{C}\n\n^ Rebase 0000000..3333333 onto 0000000 (3 commands)\n";

    [Fact]
    public void AppliesActionsAndOrderKeepingOtherLines()
    {
        var result = InteractiveRebase.ApplyPlan(Todo, [new("pick", C), new("reword", A), new("fixup", B)]);
        var lines = result.Split('\n');
        Assert.Equal($"pick 3333333 #!_{C}", lines[0]);
        Assert.Equal($"reword 1111111 #!_{A}", lines[1]);
        Assert.Equal("update-ref refs/heads/topic", lines[2]);   // コミット以外の行は元の位置
        Assert.Equal($"fixup 2222222 #!_{B}", lines[4]);
        Assert.Contains("^ Rebase", result);
    }

    [Fact]
    public void RemovesUpdateRefLinesWhenTurnedOff()
    {
        var result = InteractiveRebase.ApplyPlan(Todo, [new("pick", A), new("pick", B), new("pick", C), InteractiveRebase.NoUpdateRefs]);
        Assert.DoesNotContain("update-ref", result);
        Assert.Contains($"pick 3333333 #!_{C}", result);
    }

    [Fact]
    public void StepsMissingFromPlanAreDropped()
    {
        var result = InteractiveRebase.ApplyPlan(Todo, [new("pick", A), new("pick", C)]);
        Assert.DoesNotContain(B, result);
        Assert.Contains(A, result);
        Assert.Contains(C, result);
    }

    [Fact]
    public void PlanFileRoundTripsAndCancels()
    {
        var plan = InteractiveRebase.ParsePlan(InteractiveRebase.FormatPlan([new("drop", A), new("edit", B)]))!;
        Assert.Equal([new RebasePlanStep("drop", A), new RebasePlanStep("edit", B)], plan);
        Assert.Null(InteractiveRebase.ParsePlan("cancel\n"));
    }

    /// <summary>本物の git に GitKnife.RI.exe を起動させて、reword と並べ替えをする。</summary>
    [Fact]
    public async Task RunsWithRealGitAndRi()
    {
        var path = SandboxFixture.CreateFresh(out var root);
        _roots.Add(root);
        var reader = (await RepositoryReader.OpenAsync(sandbox.Git, path))!;
        var tree = reader.CreateWorkingTree();
        await tree.CommitAsync(WorkingTree.ComposeMessage("Add todo", null), amend: false);
        await tree.DiscardAsync((await reader.ReadStatusAsync()).Entries);

        async Task<string[]> Log(int n) => (await sandbox.Git.RunAsync(new GitRequest(path, ["log", $"-{n}", "--format=%s"]))).StandardOutput.Trim().Split('\n');
        async Task<string> Sha(string rev) => (await sandbox.Git.RunAsync(new GitRequest(path, ["rev-parse", rev]))).StandardOutput.Trim();
        // main：... Add usage doc ← Greet the world ← Document options ← Add todo
        var greet = await Sha("HEAD~2");
        var docs = await Sha("HEAD~1");
        var todo = await Sha("HEAD");
        var @basesha = await Sha("HEAD~3");

        var work = Path.Combine(root, "ir");
        Directory.CreateDirectory(work);
        var planFile = Path.Combine(work, "plan.txt");
        var messages = Path.Combine(work, "messages");
        Directory.CreateDirectory(messages);
        await File.WriteAllTextAsync(planFile, InteractiveRebase.FormatPlan([new("pick", docs), new("reword", greet), new("pick", todo)]));
        await File.WriteAllTextAsync(Path.Combine(messages, greet + ".txt"), "Greet the whole world\n");
        var ri = Path.Combine(AppContext.BaseDirectory, "GitKnife.RI.exe");

        var r = await sandbox.Git.RunAsync(new GitRequest(path, InteractiveRebase.Arguments(ri, planFile, messages, @basesha))
        {
            Environment = InteractiveRebase.Environment(ri, planFile, messages),
        });
        Assert.True(r.Succeeded, r.StandardError);
        Assert.Equal(["Add todo", "Greet the whole world", "Document options"], await Log(3));
    }

    /// <summary>最初のコミット（親なし）は `--root` で起動して reword / edit できる。</summary>
    [Fact]
    public async Task RewordsRootCommit()
    {
        var root = Path.Combine(Path.GetTempPath(), "gk-ir-root-" + Guid.NewGuid().ToString("N")[..8]);
        _roots.Add(root);
        var path = Path.Combine(root, "repo");
        Directory.CreateDirectory(path);
        async Task<string> Git(params string[] args)
        {
            var r = await sandbox.Git.RunAsync(new GitRequest(path, ["-c", "user.name=T", "-c", "user.email=t@example.com", .. args]));
            Assert.True(r.Succeeded, r.StandardError);
            return r.StandardOutput.Trim();
        }
        await Git("init", "-q", "-b", "main");
        await File.WriteAllTextAsync(Path.Combine(path, "a.txt"), "a\n");
        await Git("add", "-A");
        await Git("commit", "-q", "-m", "1");
        await File.WriteAllTextAsync(Path.Combine(path, "b.txt"), "b\n");
        await Git("add", "-A");
        await Git("commit", "-q", "-m", "fix");
        var first = await Git("rev-parse", "HEAD~1");
        var second = await Git("rev-parse", "HEAD");

        var work = Path.Combine(root, "ir");
        var messages = Path.Combine(work, "messages");
        Directory.CreateDirectory(messages);
        var planFile = Path.Combine(work, "plan.txt");
        await File.WriteAllTextAsync(planFile, InteractiveRebase.FormatPlan([new("reword", first), new("pick", second)]));
        await File.WriteAllTextAsync(Path.Combine(messages, first + ".txt"), "Initial commit\n");
        var ri = Path.Combine(AppContext.BaseDirectory, "GitKnife.RI.exe");

        var args = InteractiveRebase.Arguments(ri, planFile, messages, null);
        Assert.Equal("--root", args[^1]);
        var r = await sandbox.Git.RunAsync(new GitRequest(path, ["-c", "user.name=T", "-c", "user.email=t@example.com", .. args])
        {
            Environment = InteractiveRebase.Environment(ri, planFile, messages),
        });
        Assert.True(r.Succeeded, r.StandardError);
        Assert.Equal("fix\nInitial commit", await Git("log", "--format=%s"));
    }
}

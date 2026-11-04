using System.IO.Pipes;
using GitKnife.Core.Git;

namespace GitKnife.Core.Tests;

/// <summary>SSH_ASKPASS（GitKnife.AskPass）の質問の見分けと、本体の答え方。</summary>
public class AskPassTests
{
    private sealed class MemoryStore : IAskPassStore
    {
        public Dictionary<string, string> Items { get; } = [];
        public string? Read(string key) => Items.GetValueOrDefault(key);
        public void Write(string key, string secret) => Items[key] = secret;
        public void Delete(string key) => Items.Remove(key);
    }

    /// <summary>画面の代わり：聞かれた質問を記録し、用意した答えを順に返す。</summary>
    private sealed class FakePrompt(params AskPassAnswer?[] answers)
    {
        private readonly Queue<AskPassAnswer?> _answers = new(answers);
        public List<AskPassRequest> Asked { get; } = [];
        public Task<AskPassAnswer?> PromptAsync(AskPassRequest r, CancellationToken ct)
        {
            lock (Asked) Asked.Add(r);
            return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue() : null);
        }
    }

    private const string HostKeyPrompt =
        "The authenticity of host 'build01 (10.0.0.5)' can't be established.\r\n" +
        "ED25519 key fingerprint is SHA256:abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG.\r\n" +
        "This key is not known by any other names.\r\n" +
        "Are you sure you want to continue connecting (yes/no/[fingerprint])? ";

    [Theory]
    [InlineData("user@build01's password: ", AskPassKind.Password, "ssh:build01")]
    [InlineData("(user@build01) Password: ", AskPassKind.Password, "ssh:build01")]
    [InlineData("Enter passphrase for key 'C:\\Users\\a\\.ssh\\id_ed25519': ", AskPassKind.Passphrase, "ssh-key:C:\\Users\\a\\.ssh\\id_ed25519")]
    [InlineData(HostKeyPrompt, AskPassKind.HostKey, "hostkey:build01")]
    [InlineData("Please type 'yes', 'no' or the fingerprint: ", AskPassKind.HostKey, "hostkey:build01")]
    [InlineData("Verification code: ", AskPassKind.Other, "other:build01:Verification code: ")]
    public void ClassifiesSshPrompts(string prompt, AskPassKind kind, string key)
    {
        var q = AskPassClassifier.Classify("build01", prompt);
        Assert.Equal(kind, q.Kind);
        Assert.Equal(key, q.Key);
    }

    [Fact]
    public async Task RemembersThePasswordForLaterSshProcesses()
    {
        var store = new MemoryStore();
        var prompt = new FakePrompt(new AskPassAnswer("secret", Remember: true));
        using var broker = new AskPassBroker(prompt.PromptAsync, store);

        Assert.Equal("secret", await broker.AnswerAsync(broker.RegisterSession("build01"), "user@build01's password: "));
        Assert.Equal("secret", store.Items["ssh:build01"]);
        // 次の ssh（別のセッション）は聞かずに答える
        Assert.Equal("secret", await broker.AnswerAsync(broker.RegisterSession("build01"), "user@build01's password: "));
        Assert.Single(prompt.Asked);
        Assert.True(broker.HasSecret("ssh:build01"));
    }

    [Fact]
    public async Task UsesTheSavedPasswordAndAsksAgainWhenItIsRejected()
    {
        var store = new MemoryStore { Items = { ["ssh:build01"] = "old" } };
        var prompt = new FakePrompt(new AskPassAnswer("new", Remember: true));
        using var broker = new AskPassBroker(prompt.PromptAsync, store);
        var session = broker.RegisterSession("build01");

        Assert.Equal("old", await broker.AnswerAsync(session, "user@build01's password: "));
        Assert.Empty(prompt.Asked);
        // 同じ ssh がもう一度聞いた：保存したパスワードは違っていた
        Assert.Equal("new", await broker.AnswerAsync(session, "user@build01's password: "));
        Assert.True(Assert.Single(prompt.Asked).Retry);
        Assert.Equal("new", store.Items["ssh:build01"]);
    }

    [Fact]
    public async Task DoesNotSaveWhenRememberIsOffButKeepsItForThisRun()
    {
        var store = new MemoryStore { Items = { ["ssh:build01"] = "stale" } };
        var prompt = new FakePrompt(new AskPassAnswer("typed", Remember: false));
        using var broker = new AskPassBroker(prompt.PromptAsync, store);
        var session = broker.RegisterSession("build01");
        Assert.Equal("stale", await broker.AnswerAsync(session, "user@build01's password: "));
        Assert.Equal("typed", await broker.AnswerAsync(session, "user@build01's password: "));
        Assert.False(store.Items.ContainsKey("ssh:build01"));
        Assert.Equal("typed", await broker.AnswerAsync(broker.RegisterSession("build01"), "user@build01's password: "));
        Assert.Single(prompt.Asked);
    }

    [Fact]
    public async Task CancelRefusesAndStaysQuietForAWhile()
    {
        var prompt = new FakePrompt([null]);
        using var broker = new AskPassBroker(prompt.PromptAsync, new MemoryStore());
        Assert.Null(await broker.AnswerAsync(broker.RegisterSession("build01"), "user@build01's password: "));
        // 続けて張られる ssh には画面を出さずに断る
        Assert.Null(await broker.AnswerAsync(broker.RegisterSession("build01"), "user@build01's password: "));
        Assert.Single(prompt.Asked);
    }

    [Fact]
    public async Task CancelOnRetryIsNotAskedAgainBySameSsh()
    {
        var prompt = new FakePrompt(new AskPassAnswer("wrong", false), null);
        using var broker = new AskPassBroker(prompt.PromptAsync, new MemoryStore()) { DeclineQuietPeriod = TimeSpan.Zero };
        var session = broker.RegisterSession("build01");
        Assert.Equal("wrong", await broker.AnswerAsync(session, "user@build01's password: "));
        Assert.Null(await broker.AnswerAsync(session, "user@build01's password: "));   // Retry でキャンセル
        Assert.Null(await broker.AnswerAsync(session, "user@build01's password: "));   // ssh の 3 回目は画面を出さない
        Assert.Equal(2, prompt.Asked.Count);
    }

    [Fact]
    public async Task HostKeyIsAnsweredYesOrNoAndNeverSaved()
    {
        var store = new MemoryStore();
        var prompt = new FakePrompt(new AskPassAnswer("yes", false), null);
        using var broker = new AskPassBroker(prompt.PromptAsync, store) { DeclineQuietPeriod = TimeSpan.Zero };
        Assert.Equal("yes", await broker.AnswerAsync(broker.RegisterSession("build01"), HostKeyPrompt));
        // キャンセルは "no"（yes / no 以外を返すと ssh が聞き続ける）
        Assert.Equal("no", await broker.AnswerAsync(broker.RegisterSession("build01"), HostKeyPrompt));
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task RefusesUnknownSessions()
    {
        var prompt = new FakePrompt(new AskPassAnswer("secret", true));
        using var broker = new AskPassBroker(prompt.PromptAsync, new MemoryStore());
        Assert.Null(await broker.AnswerAsync("not-a-session", "user@build01's password: "));
        Assert.Empty(prompt.Asked);
    }

    [Fact]
    public async Task ConcurrentQuestionsForTheSameHostAskOnce()
    {
        var gate = new TaskCompletionSource();
        var asked = 0;
        using var broker = new AskPassBroker(async (_, _) => { Interlocked.Increment(ref asked); await gate.Task; return new AskPassAnswer("secret", false); }, new MemoryStore());
        var answers = Enumerable.Range(0, 4).Select(_ => broker.AnswerAsync(broker.RegisterSession("build01"), "user@build01's password: ")).ToList();
        await Task.Delay(100);
        gate.SetResult();
        Assert.All(await Task.WhenAll(answers), a => Assert.Equal("secret", a));
        Assert.Equal(1, asked);
    }

    [Fact]
    public async Task AnswersOverTheNamedPipe()
    {
        var prompt = new FakePrompt(new AskPassAnswer("pä$$ word", false));
        using var broker = new AskPassBroker(prompt.PromptAsync, new MemoryStore());
        var session = broker.RegisterSession("build01");
        var answer = await Task.Run(() =>
        {
            using var client = new NamedPipeClientStream(".", broker.PipeName, PipeDirection.InOut);
            client.Connect(5000);
            return AskPassProtocol.Ask(client, session, HostKeyPrompt.Replace("(yes/no/[fingerprint])", "") + "user@build01's password: ");
        });
        Assert.Equal("pä$$ word", answer);

        var refused = await Task.Run(() =>
        {
            using var client = new NamedPipeClientStream(".", broker.PipeName, PipeDirection.InOut);
            client.Connect(5000);
            return AskPassProtocol.Ask(client, "unknown", "user@build01's password: ");
        });
        Assert.Null(refused);
    }
}

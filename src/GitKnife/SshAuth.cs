using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using GitKnife.Core.Git;
using GitKnife.Core.Logging;
using GitKnife.ViewModels.Dialogs;

namespace GitKnife;

/// <summary>
/// SSH の接続の認証（パスワード、鍵のパスフレーズ、知らないホストの鍵の確認）を画面で聞く。
/// ssh が GitKnife.AskPass.exe を起動し、AskPass が名前付きパイプで <see cref="AskPassBroker"/> に聞き、ここで画面を出す。
/// 画面は UI のスレッドで出す。UI のスレッドが ssh の終わりを同期で待っているとき（接続先のファイルの読み書き）は、
/// 待つ間に呼ばれる <see cref="SshAskPass.WhileWaiting"/> から出す（出さないと互いに待って止まる）。
/// </summary>
internal static class SshAuth
{
    private sealed record Pending(AskPassRequest Request, TaskCompletionSource<AskPassAnswer?> Result);

    private static readonly ConcurrentQueue<Pending> s_pending = new();
    private static Dispatcher? s_dispatcher;

    public static void Start(Dispatcher dispatcher)
    {
        s_dispatcher = dispatcher;
        var helper = System.IO.Path.Combine(AppContext.BaseDirectory, "GitKnife.AskPass.exe");
        if (!System.IO.File.Exists(helper))
        {
            Log.Warning($"{helper} is missing: SSH hosts can use only keys and ssh-agent");
            return;
        }
        SshAskPass.HelperPath = helper;
        SshAskPass.Broker = new AskPassBroker(PromptAsync, new CredentialAskPassStore());
        SshAskPass.WhileWaiting = () => { if (dispatcher.CheckAccess()) ServicePending(); };
    }

    public static void Stop() => SshAskPass.Broker?.Dispose();

    /// <summary>接続先のパスワード（保存したものも）を忘れる（Preferences でホストを消したとき）。</summary>
    public static void ForgetPassword(string destination) => SshAskPass.Broker?.Forget("ssh:" + destination);

    private static Task<AskPassAnswer?> PromptAsync(AskPassRequest request, CancellationToken ct)
    {
        var p = new Pending(request, new TaskCompletionSource<AskPassAnswer?>(TaskCreationOptions.RunContinuationsAsynchronously));
        ct.Register(() => p.Result.TrySetResult(null));
        s_pending.Enqueue(p);
        if (s_dispatcher is null || s_dispatcher.HasShutdownStarted) p.Result.TrySetResult(null);
        else s_dispatcher.BeginInvoke(ServicePending);
        return p.Result.Task;
    }

    private static void ServicePending()
    {
        while (s_pending.TryDequeue(out var p))
        {
            if (p.Result.Task.IsCompleted) continue;
            try { p.Result.TrySetResult(Show(p.Request)); }
            catch (Exception ex)
            {
                Log.Warning($"AskPass: showing the prompt failed: {ex}");
                p.Result.TrySetResult(null);
            }
        }
    }

    private static AskPassAnswer? Show(AskPassRequest request)
    {
        if (Application.Current?.MainWindow is null) return null;
        if (request.Question.Kind == AskPassKind.HostKey)
        {
            var confirm = new SshHostKeyDialogViewModel(request.Question);
            return Views.Dialogs.DialogWindow.Run(confirm, out _) ? new AskPassAnswer("yes", false) : null;
        }
        var vm = new SshPasswordDialogViewModel(request);
        return Views.Dialogs.DialogWindow.Run(vm, out _) ? new AskPassAnswer(vm.Password, vm.Remember && request.Question.IsSecret) : null;
    }

    /// <summary>保存は Windows の資格情報マネージャー（"GitKnife:ssh:&lt;接続先&gt;"、"GitKnife:ssh-key:&lt;鍵のファイル&gt;"）。</summary>
    private sealed class CredentialAskPassStore : IAskPassStore
    {
        private static string Target(string key) => "GitKnife:" + key;
        public string? Read(string key) => CredentialStore.Read(Target(key));
        public void Write(string key, string secret) => CredentialStore.Write(Target(key), secret);
        public void Delete(string key) => CredentialStore.Delete(Target(key));
    }
}

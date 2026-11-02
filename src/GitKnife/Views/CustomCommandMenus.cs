using System.Diagnostics;
using System.Text;
using System.Windows.Controls;
using GitKnife.Core.Git.Model;
using GitKnife.Core.Logging;
using GitKnife.Core.Settings;
using GitKnife.ViewModels;
using GitKnife.ViewModels.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// カスタムコマンドのメニュー項目と実行。
/// ファイルはメニューを開くたびに読み直す。
/// </summary>
internal static class CustomCommandMenus
{
    /// <summary>
    /// メニューの末尾に足す。区切り線のあと、header なら "Custom Commands"（disabled の見出し。ブランチ・タグのメニュー）、その下にコマンド。
    /// 1 つも無ければ何も足さない。
    /// </summary>
    public static void AddTo(ItemsControl menu, RepositoryViewModel repo, string target, Func<CustomCommandContext> context,
        CustomCommandRefKind? refKind = null, bool header = false)
    {
        var commands = CustomCommandStore.For(CustomCommandStore.Load(repo.Path, repo.CommonGitDirectory), target, refKind);
        if (commands.Count == 0) return;
        if (menu.Items.Count > 0 && menu.Items[^1] is not Separator) menu.Items.Add(new Separator());
        if (header) menu.Items.Add(new MenuItem { Header = "Custom Commands", IsEnabled = false });
        AddNodes(menu, CustomCommandMenuNode.Build(commands), repo, context);
    }

    private static void AddNodes(ItemsControl parent, IReadOnlyList<CustomCommandMenuNode> nodes, RepositoryViewModel repo, Func<CustomCommandContext> context)
    {
        foreach (var node in nodes)
        {
            var mi = new MenuItem { Header = new AccessText { Text = node.Title.Replace("_", "__") } };
            System.Windows.Automation.AutomationProperties.SetName(mi, node.Title);
            if (node.Command is { } command) mi.Click += (_, _) => _ = RunAsync(repo, command, context());
            else AddNodes(mi, node.Children, repo, context);
            parent.Items.Add(mi);
        }
    }

    // ------------------------------------------------------------------ 場面の値

    public static CustomCommandContext Commit(RepositoryViewModel repo, CommitInfo c) => new(repo.Path) { Sha = c.Sha, ShortSha = c.ShortSha };

    public static CustomCommandContext Reference(RepositoryViewModel repo, ReferenceInfo r)
    {
        var shortName = r.ShortName;
        var withoutRemote = r.Kind == ReferenceKind.RemoteBranch && r.RemoteName is { } remote && shortName.StartsWith(remote + "/", StringComparison.Ordinal)
            ? shortName[(remote.Length + 1)..] : shortName;
        var commit = repo.Commits.FirstOrDefault(row => row.Commit.Sha == r.TargetSha)?.Commit;
        return new CustomCommandContext(repo.Path) { Sha = r.TargetSha, ShortSha = commit?.ShortSha, Ref = shortName, RefShort = withoutRemote, RefFull = r.CanonicalName };
    }

    public static CustomCommandContext File(RepositoryViewModel repo, CommitInfo c, string path) => Commit(repo, c) with { File = path };

    public static CustomCommandContext Submodule(RepositoryViewModel repo, string path) => new(repo.Path) { Submodule = path };

    public static CustomCommandContext Repository(RepositoryViewModel repo) => new(repo.Path);

    public static CustomCommandRefKind? RefKind(ReferenceInfo r) => r.Kind switch
    {
        ReferenceKind.LocalBranch => CustomCommandRefKind.LocalBranch,
        ReferenceKind.RemoteBranch => CustomCommandRefKind.RemoteBranch,
        ReferenceKind.Tag => CustomCommandRefKind.Tag,
        _ => null,
    };

    // ------------------------------------------------------------------ 実行

    public static async Task RunAsync(RepositoryViewModel repo, CustomCommand command, CustomCommandContext ctx)
    {
        // Shared のコマンドは、信頼していなければ先に確かめる
        if (command.Source == CustomCommandSource.Shared && repo.CommonGitDirectory is { } common)
        {
            var config = RepositoryConfig.Load(common);
            if (!config.TrustSharedCommands)
            {
                var confirm = new SharedCustomCommandConfirmDialogViewModel(System.IO.Path.GetFileName(repo.Path.TrimEnd('\\', '/')));
                if (Dialogs.DialogWindow.Run(confirm, out _) != true) return;
                if (confirm.Trust)
                {
                    config.TrustSharedCommands = true;
                    config.Save();
                }
            }
        }

        var action = command.Action;
        if (command.Ui is { } ui)
        {
            var dialog = new CustomCommandUiDialogViewModel(ui,
                CustomCommandVariables.Expand(ui.Title, ctx, command.Target), CustomCommandVariables.Expand(ui.Description, ctx, command.Target),
                repo.References?.References ?? []);
            var ok = Dialogs.DialogWindow.Run(dialog, out _) == true;
            action = ok ? ui.Buttons.FirstOrDefault()?.Action : ui.Buttons.ElementAtOrDefault(1)?.Action;
            ctx = ctx with { Controls = dialog.Values };
        }
        if (action is null || action.Type == "cancel") return;
        await ExecuteAsync(repo, command, action, ctx);
    }

    /// <summary>
    /// 動作を実行する。ログに「Run bash custom action for '名前'」「Running '...'」を残す。
    /// 待つ（waitForExit / showOutput）なら終わってから Refresh し、showOutput なら結果のウィンドウ（題 = 名前、"名前 completed"）を出す。
    /// </summary>
    private static async Task ExecuteAsync(RepositoryViewModel repo, CustomCommand command, CustomCommandAction action, CustomCommandContext ctx)
    {
        string Expand(string? s) => CustomCommandVariables.Expand(s, ctx, command.Target);
        ProcessStartInfo psi;
        switch (action.Type)
        {
            case "url":
            {
                var url = Expand(action.Url);
                Log.Info($"Run url custom action for '{command.Name}'");
                try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
                catch (Exception ex) { Alerts.ShowGitError(url, ex.Message); }
                return;
            }
            case "sh" when repo.Git.Host is Core.Git.WslGitHost wsl:
            {
                // WSL のリポジトリはその WSL の bash で、パスの変数は Linux の形で（T1301）
                var script = CustomCommandVariables.Expand(action.Script, ctx with { RepositoryPath = wsl.ToHostPath(ctx.RepositoryPath) }, command.Target);
                Log.Info($"Run bash custom action for '{command.Name}' ({wsl.DisplayName})");
                Log.Info($"Running 'bash -c \"{script}\"' ({wsl.DisplayName})");
                psi = new ProcessStartInfo(Core.Git.WslGitHost.WslExe) { ArgumentList = { "-d", wsl.Distribution, "--cd", wsl.ToHostPath(repo.Path), "--exec", "bash", "-c", script } };
                psi.WorkingDirectory = Environment.SystemDirectory;
                break;
            }
            case "sh":
            {
                var bash = repo.Git.Git.FindBash();
                if (bash is null) { Alerts.ShowGitError("bash", "bash.exe was not found."); return; }
                var script = Expand(action.Script);
                Log.Info($"Run bash custom action for '{command.Name}'");
                Log.Info($"Running '{bash} -c \"{script}\"'");
                psi = new ProcessStartInfo(bash) { ArgumentList = { "-c", script } };
                break;
            }
            case "process":
            {
                var path = Expand(action.Path);
                var args = Expand(action.Args);
                Log.Info($"Run process custom action for '{command.Name}'");
                Log.Info($"Running '{path} {args}'");
                psi = new ProcessStartInfo(path, args);
                break;
            }
            default:
                Log.Warning($"Unknown custom action type '{action.Type}' in '{command.Name}'");
                return;
        }

        if (psi.WorkingDirectory.Length == 0) psi.WorkingDirectory = repo.Path;
        psi.UseShellExecute = false;
        var wait = action.WaitForExit || action.ShowOutput;
        if (action.ShowOutput)
        {
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = psi.StandardErrorEncoding = new UTF8Encoding(false);
        }
        try
        {
            using var p = Process.Start(psi);
            if (p is null || !wait) return;
            var output = new StringBuilder();
            if (action.ShowOutput)
            {
                var err = p.StandardError.ReadToEndAsync();
                output.Append(await p.StandardOutput.ReadToEndAsync());
                output.Append(await err);
            }
            await p.WaitForExitAsync();
            if (action.ShowOutput) Dialogs.DialogWindow.Run(new CustomActionResultDialogViewModel(command.Name, output.ToString().TrimEnd()), out _);
            await repo.RefreshAsync();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Alerts.ShowGitError(psi.FileName, ex.Message);
        }
    }
}

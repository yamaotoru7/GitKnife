using System.Windows;
using GitKnife.Core.Git.Commands;
using GitKnife.Core.Git.Model;
using GitKnife.Core.Logging;
using GitKnife.ViewModels;
using GitKnife.ViewModels.Dialogs;
using GitKnife.Views.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// メニュー・ツールバー・右クリックから呼ぶ操作。ダイアログを出して実行し、終わったら読み直す。
/// ダイアログの無い操作（ダブルクリックのチェックアウト、Fast-Forward など）もここ。
/// </summary>
public static class RepositoryActions
{
    /// <summary>ダイアログを出し、実行したら全体を読み直す。</summary>
    public static async void Run(RepositoryViewModel repo, DialogViewModel vm)
    {
        vm.ActivityRepository = repo;
        if (!DialogWindow.Run(vm, DialogViews.For(vm))) return;
        await repo.RefreshAsync();
        // 競合で止まったら Local Changes に切り替える
        if (repo.Status?.Entries.Any(e => e.IsConflicted) == true) repo.View = ViewModels.RepositoryView.LocalChanges;
    }

    /// <summary>ダイアログなしで git を実行する（失敗なら Git Error）。</summary>
    public static async void RunDirect(RepositoryViewModel repo, string what, params string[][] commands)
    {
        try
        {
            foreach (var c in commands) await repo.RunGitAsync(c);
        }
        catch (Core.Git.GitException ex)
        {
            Log.Error($"{what} failed: {ex.Message}");
            Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError.Length > 0 ? ex.Result.StandardError : ex.Result.StandardOutput);
        }
        await repo.RefreshAsync();
    }

    /// <summary>
    /// 帯の Bad / Good / Skip。見つかったとき（"... is the first bad commit"。新しい git は 'bad' と引用符つき）は、git の出力をそのまま Git Error の窓で見せる。
    /// bisect は終わらず、Abort（`bisect reset`）で戻る。
    /// </summary>
    public static async void RunBisect(RepositoryViewModel repo, string[] args)
    {
        try
        {
            var r = await repo.RunGitAsync(args);
            if (System.Text.RegularExpressions.Regex.IsMatch(r.StandardOutput, "is the first '?bad'? commit")) Alerts.ShowGitError(r.CommandLine, r.StandardOutput.Trim());
        }
        catch (Core.Git.GitException ex)
        {
            Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError.Length > 0 ? ex.Result.StandardError : ex.Result.StandardOutput);
        }
        await repo.RefreshAsync();
    }

    // --- Git Flow（H18）---
    public static void GitFlowStart(RepositoryViewModel repo, Core.Git.Model.GitFlowConfig flow, Core.Git.Model.GitFlowKind kind)
        => Run(repo, new GitFlowStartDialogViewModel(repo, flow, kind));

    public static void GitFlowFinish(RepositoryViewModel repo, Core.Git.Model.GitFlowConfig flow, Core.Git.Model.GitFlowKind kind, string branch)
        => Run(repo, new GitFlowFinishDialogViewModel(repo, flow, kind, branch));

    /// <summary>ダイアログなしで Fetch（Quick Launch の "Fetch"。前回の Fetch ダイアログの設定で）。</summary>
    public static Task FetchNow(RepositoryViewModel repo)
    {
        var vm = new FetchDialogViewModel(repo);
        return RunJob(repo, vm.ActivityName, () => vm.SubmitAsync(CancellationToken.None), vm.ActivitySummary);
    }

    /// <summary>
    /// Alt を押しながらツールバーの Fetch（QuickFetch）：今のブランチのリモートだけを取る。"Fetch all remotes" の設定に関係なく
    /// `fetch --prune origin --progress`。設定は変えない。
    /// </summary>
    public static Task QuickFetch(RepositoryViewModel repo)
    {
        if ((FetchDialogViewModel.UpstreamRemote(repo) ?? repo.References?.Remotes.FirstOrDefault()?.Name) is not { } remote) return Task.CompletedTask;
        return RunJob(repo, $"Fetch '{remote}'", () => repo.RunGitAsync(Core.Git.Commands.GitArgs.Fetch(remote)), null);
    }

    /// <summary>Alt を押しながらツールバーの Pull（QuickPull）：前回の Pull ダイアログの設定のまま、ダイアログなしで。</summary>
    public static Task QuickPull(RepositoryViewModel repo)
    {
        var vm = new PullDialogViewModel(repo);
        return vm.CanSubmit ? RunJob(repo, vm.ActivityName, () => vm.SubmitAsync(CancellationToken.None), vm.ActivitySummary) : Task.CompletedTask;
    }

    /// <summary>ダイアログなしで今のブランチを push（Quick Launch の "Push"、Alt を押しながらツールバーの Push）。</summary>
    public static Task PushNow(RepositoryViewModel repo) => RunJob(repo, "Push", repo.PushCurrentBranchAsync, null);

    /// <summary>Activity Manager にジョブを残して実行し、失敗なら Git Error。</summary>
    private static async Task RunJob(RepositoryViewModel repo, string name, Func<Task> action, Func<string, string>? summary)
    {
        var job = repo.StartJob(name);
        using (job.Enter())
        {
            try
            {
                await action();
                job.Finish(true, summary?.Invoke(job.OutputText));
            }
            catch (OperationCanceledException) { job.Finish(false, "Canceled"); }
            catch (Core.Git.GitException ex)
            {
                var error = ex.Result.StandardError.Length > 0 ? ex.Result.StandardError : ex.Result.StandardOutput;
                job.Finish(false, error.Trim().Split('\n')[0]);
                Alerts.ShowGitError(ex.Result.CommandLine, error);
            }
        }
        await repo.RefreshAsync();
    }

    public static void Fetch(RepositoryViewModel repo) => Run(repo, new FetchDialogViewModel(repo));
    public static void Pull(RepositoryViewModel repo) => Run(repo, new PullDialogViewModel(repo));
    public static void Push(RepositoryViewModel repo) => Run(repo, new PushDialogViewModel(repo));
    public static void SaveStash(RepositoryViewModel repo) => Run(repo, new SaveStashDialogViewModel(repo));

    /// <summary>Repository > Settings for This Repository...（閉じたら名前・メール・テンプレートを書く）。</summary>
    public static async void RepositorySettings(RepositoryViewModel repo)
    {
        var vm = new RepositorySettingsViewModel(repo);
        DialogWindow.Run(vm, DialogViews.For(vm));
        vm.CustomCommands.Save();
        vm.IssueRules.Save();
        try { await vm.SaveAsync(); }
        catch (Core.Git.GitException ex) { Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
    }

    /// <summary>New Worktree...（Create Worktree ダイアログ。作ったら新しいタブで開く）。</summary>
    public static void NewWorktree(RepositoryViewModel repo, string? startFrom = null, string? existingBranch = null, string? remoteBranch = null)
    {
        var vm = new CreateWorktreeDialogViewModel(repo, startFrom, existingBranch, remoteBranch) { ActivityRepository = repo };
        if (!DialogWindow.Run(vm, DialogViews.For(vm))) return;
        _ = repo.RefreshAsync();
        if (Application.Current.MainWindow?.DataContext is MainViewModel main) _ = main.OpenAsync(vm.CreatedPath);
    }

    /// <summary>
    /// Lean Branching の Sync 'main' (Rebase on 'origin/main')。fetch はしない。変更があれば untracked も含めて退避してから戻す：
    /// `add --force --verbose --pathspec-from-file=- --pathspec-file-nul --`（untracked）→ `stash save --message "... sync autostash <日時>"`
    /// → `-c core.commentChar=^ rebase --rebase-merges refs/remotes/origin/main` → `stash pop --index refs/stash@{0}`。
    /// 戻した後は untracked だったファイルが index に入ったままになる。
    /// </summary>
    /// <param name="rebaseMerges">main を origin/main に合わせるときは --rebase-merges、ほかのブランチを main に合わせるときは付けない。</param>
    public static async Task SyncAsync(RepositoryViewModel repo, string branch, string upstream, bool rebaseMerges = true)
    {
        var job = repo.StartJob($"Sync '{branch}'");
        using (job.Enter())
        {
            try
            {
                var entries = repo.Status?.Entries ?? [];
                var hasChanges = entries.Any(e => e.WorkTree is not (Core.Git.Model.FileChange.Unmodified or Core.Git.Model.FileChange.Ignored) || e.HasStagedChange);
                if (hasChanges)
                {
                    var untracked = entries.Where(e => e.WorkTree == Core.Git.Model.FileChange.Untracked).Select(e => e.Path).ToList();
                    if (untracked.Count > 0)
                    {
                        var input = System.Text.Encoding.UTF8.GetBytes(string.Join("\0", untracked) + "\0");
                        (await repo.Git.RunAsync(new Core.Git.GitRequest(repo.Path, ["add", "--force", "--verbose", "--pathspec-from-file=-", "--pathspec-file-nul", "--"]) { StandardInput = input })).ThrowIfFailed();
                    }
                    await repo.RunGitAsync(["stash", "save", "--message", $"{Core.ProductInfo.Name} sync autostash {DateTime.Now:yyyy/MM/dd (ddd) HH:mm:ss}"]);
                }
                string[] rebase = rebaseMerges ? ["-c", "core.commentChar=^", "rebase", "--rebase-merges", upstream] : ["-c", "core.commentChar=^", "rebase", upstream];
                try { await repo.RunGitAsync(rebase); }
                finally { if (hasChanges && !await repo.HasConflictsAsync()) await repo.RunGitAsync(["stash", "pop", "--index", "refs/stash@{0}"]); }
                job.Finish(true, null);
            }
            catch (Core.Git.GitException ex)
            {
                var error = ex.Result.StandardError.Length > 0 ? ex.Result.StandardError : ex.Result.StandardOutput;
                job.Finish(false, error.Trim().Split('\n')[0]);
                if (await repo.HasConflictsAsync()) { await repo.RefreshAsync(); repo.View = ViewModels.RepositoryView.LocalChanges; DialogWindow.ShowOutput("Merge Conflict", "Automatic merge failed. Fix conflicts and then commit the result", error); return; }
                Alerts.ShowGitError(ex.Result.CommandLine, error);
            }
        }
        await repo.RefreshAsync();
    }

    /// <summary>
    /// Lean Branching の Finish。変更があると git を呼ばずに "Cannot sync: You have unstaged changes. Please commit or stash them." の Git Error。
    /// </summary>
    public static void FinishBranch(RepositoryViewModel repo, string branch, string main)
    {
        var entries = repo.Status?.Entries ?? [];
        if (entries.Any(e => e.WorkTree is not (Core.Git.Model.FileChange.Unmodified or Core.Git.Model.FileChange.Ignored) || e.HasStagedChange))
        {
            DialogWindow.ShowOutput("Git Error", "An unexpected error occured while performing the git request", "Cannot sync: You have unstaged changes. Please commit or stash them.");
            return;
        }
        Run(repo, new FinishBranchDialogViewModel(repo, branch, main));
    }

    /// <summary>worktree の Delete '...'...（"Are you sure you want to delete worktree wt2?" → `worktree remove --force &lt;path&gt;`。ブランチは残る）。</summary>
    public static void DeleteWorktree(RepositoryViewModel repo, string name, string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        if (!Alerts.Confirm($"Are you sure you want to delete worktree {name}?", $"Do you want to delete worktree {full}?", "Delete")) return;
        RunDirect(repo, "Delete Worktree", ["worktree", "remove", "--force", full]);
    }

    // ---------------------------------------------------------- submodule

    /// <summary>Update 'sublib' / Update Submodules：`submodule update --init --recursive --progress -- &lt;path...&gt;`。</summary>
    public static void UpdateSubmodules(RepositoryViewModel repo, params string[] paths)
    {
        if (paths.Length == 0) return;
        _ = RunJob(repo, "Update Submodules", () => repo.RunGitAsync(["submodule", "update", "--init", "--recursive", "--progress", "--", .. paths]), null);
    }

    /// <summary>Delete 'sublib'...：確認 → `submodule deinit -f &lt;path&gt;` → `rm -r &lt;path&gt;`（.git/modules は残す）。</summary>
    public static void DeleteSubmodule(RepositoryViewModel repo, Core.Git.SubmoduleInfo sm)
    {
        if (!Alerts.Confirm($"Are you sure you want to delete submodule {sm.Path}?", $"Do you want to delete submodule {sm.Path}?", "Delete")) return;
        RunDirect(repo, "Delete Submodule", ["submodule", "deinit", "-f", sm.Path], ["rm", "-r", sm.Path]);
    }

    /// <summary>
    /// Move 'sublib'...：フォルダを選ぶ（"Select empty folder for new submodule location"）→ 選んだフォルダそのものが新しい場所になる
    /// （空の moved を選ぶと moved に入る）。`mv --verbose sublib moved` → `config --file=.gitmodules --rename-section submodule.sublib submodule.moved`。
    /// </summary>
    public static void MoveSubmodule(RepositoryViewModel repo, Core.Git.SubmoduleInfo sm)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Select empty folder for new submodule location", InitialDirectory = repo.Path };
        if (dlg.ShowDialog() != true) return;
        var rel = System.IO.Path.GetRelativePath(repo.Path, dlg.FolderName).Replace('\\', '/');
        if (rel.StartsWith("..", StringComparison.Ordinal) || rel == "." || rel == sm.Path) return;
        _ = RunJob(repo, "Move Submodule", async () =>
        {
            await repo.RunGitAsync(["mv", "--verbose", sm.Path, rel]);
            await repo.RunGitAsync(["config", "--file=.gitmodules", "--rename-section", $"submodule.{sm.Name}", $"submodule.{rel}"]);
        }, null);
    }

    /// <summary>Open 'sublib'...：新しいタブで開く（まだ取ってきていなければ先に Update）。</summary>
    public static async void OpenSubmodule(RepositoryViewModel repo, Core.Git.SubmoduleInfo sm)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(repo.Path, sm.Path));
        if (!System.IO.File.Exists(System.IO.Path.Combine(full, ".git")) && !System.IO.Directory.Exists(System.IO.Path.Combine(full, ".git")))
            await RunJob(repo, "Update Submodules", () => repo.RunGitAsync(["submodule", "update", "--init", "--recursive", "--progress", "--", sm.Path]), null);
        if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel main) await main.OpenAsync(full);
    }

    /// <summary>Open In Console（Windows Terminal があればそれ、なければ cmd）。</summary>
    /// <summary>エクスプローラーを開く。SSH のリポジトリ（\gitknife-ssh\…）はエクスプローラーで開けないので知らせるだけ（T1306）。</summary>
    public static void Explorer(string arguments)
    {
        if (arguments.Contains(Core.Git.SshPath.Server, StringComparison.OrdinalIgnoreCase))
        {
            Alerts.ShowInfo("File Explorer", "Files of a repository on an SSH host can't be opened in File Explorer. Use Open In Console instead.");
            return;
        }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
    }

    public static void OpenConsole(string path)
    {
        // SSH のリポジトリは ssh でその場所に入ったシェル（T1306）
        if (Core.Git.GitHost.FromPath(path) is Core.Git.SshGitHost ssh)
        {
            var remote = $"cd {Core.Git.SshGitHost.Quote(ssh.ToHostPath(path))} && exec \"$SHELL\" -l";
            var args = $"-t {ssh.Destination} \"{remote.Replace("\"", "\\\"")}\"";
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wt.exe", $"\"{Core.Git.SshGitHost.SshExe}\" {args}") { UseShellExecute = true }); }
            catch { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Core.Git.SshGitHost.SshExe, args) { UseShellExecute = true }); }
            return;
        }
        // WSL のリポジトリはその WSL のシェルで開く（T1306）
        if (Core.Git.GitHost.FromPath(path) is Core.Git.WslGitHost wsl)
        {
            var linux = wsl.ToHostPath(path);
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wt.exe", $"wsl.exe -d \"{wsl.Distribution}\" --cd \"{linux}\"") { UseShellExecute = true }); }
            catch { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Core.Git.WslGitHost.WslExe, $"-d \"{wsl.Distribution}\" --cd \"{linux}\"") { UseShellExecute = true }); }
            return;
        }
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wt.exe", $"-d \"{path}\"") { UseShellExecute = true }); }
        catch { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe") { WorkingDirectory = path, UseShellExecute = true }); }
    }

    /// <summary>Ctrl+B：ブランチを選んでチェックアウト（Quick checkout）。</summary>
    public static void QuickCheckout(RepositoryViewModel repo) => Run(repo, new QuickCheckoutDialogViewModel(repo));

    /// <summary>New Branch...（起点を省くと現在のブランチ、detached なら HEAD のコミット）。</summary>
    public static void NewBranch(RepositoryViewModel repo, RevisionTarget? start = null)
        => Run(repo, new CreateBranchDialogViewModel(repo, start ?? CurrentTarget(repo)));

    public static void NewTag(RepositoryViewModel repo, RevisionTarget? target = null)
        => Run(repo, new CreateTagDialogViewModel(repo, target ?? CurrentTarget(repo)));

    private static RevisionTarget CurrentTarget(RepositoryViewModel repo)
        => repo.References?.HeadBranch is { } b ? RevisionTarget.Branch(b) : new RevisionTarget(repo.References?.HeadSha?[..7] ?? "HEAD", repo.References?.HeadSha ?? "HEAD");

    /// <summary>ローカルブランチのダブルクリック：ダイアログなしで `checkout --progress`。</summary>
    public static void CheckoutDirect(RepositoryViewModel repo, ReferenceInfo branch)
    {
        if (branch.IsHead) return;
        RunDirect(repo, "Checkout", GitArgs.Checkout(branch.ShortName));
    }

    public static void Checkout(RepositoryViewModel repo, ReferenceInfo branch) => Run(repo, new CheckoutDialogViewModel(repo, branch));

    /// <summary>Fast-Forward to 'origin/x'：現在のブランチなら merge --ff-only（推定）、そうでなければ `update-ref refs/heads/x refs/remotes/origin/x`。</summary>
    public static void FastForward(RepositoryViewModel repo, ReferenceInfo branch)
    {
        if (branch.Upstream is not { } up) return;
        var upShort = up.Replace("refs/remotes/", "");
        if (branch.IsHead) RunDirect(repo, "Fast-Forward", ["merge", "--ff-only", upShort]);
        else RunDirect(repo, "Fast-Forward", ["update-ref", $"refs/heads/{branch.ShortName}", $"refs/remotes/{upShort}"]);
    }

    public static void Merge(RepositoryViewModel repo, RevisionTarget source) => Run(repo, new MergeDialogViewModel(repo, source));
    public static void Rebase(RepositoryViewModel repo, RevisionTarget onto) => Run(repo, new RebaseDialogViewModel(repo, onto));
    public static void DeleteBranch(RepositoryViewModel repo, ReferenceInfo branch) => Run(repo, new DeleteBranchDialogViewModel(repo, branch));
    public static void RenameBranch(RepositoryViewModel repo, ReferenceInfo branch) => Run(repo, new RenameBranchDialogViewModel(repo, branch));
    public static void DeleteTag(RepositoryViewModel repo, string tag) => Run(repo, new DeleteTagDialogViewModel(repo, tag));
    public static void ApplyStash(RepositoryViewModel repo, StashInfo stash) => Run(repo, new ApplyStashDialogViewModel(repo, stash));
    public static void DeleteStash(RepositoryViewModel repo, StashInfo stash) => Run(repo, new DeleteStashDialogViewModel(repo, stash));

    /// <summary>Tracking > リモートブランチ（upstream を設定する）。</summary>
    public static void SetUpstream(RepositoryViewModel repo, ReferenceInfo branch, string? remoteBranch)
        => RunDirect(repo, "Tracking", remoteBranch is null
            ? ["branch", "--unset-upstream", branch.ShortName]
            : ["branch", $"--set-upstream-to={remoteBranch}", branch.ShortName]);

    public static void Copy(string text) => Clipboard.SetText(text);
}

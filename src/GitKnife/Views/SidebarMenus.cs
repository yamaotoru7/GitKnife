using System.Windows.Controls;
using GitKnife.Core.Git.Model;
using GitKnife.ViewModels;
using GitKnife.ViewModels.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// サイドバーの右クリックメニュー。
/// まだ作っていない項目も同じ文言で出し、押したら「未実装」と知らせる。
/// </summary>
internal static class SidebarMenus
{
    /// <summary>項目のメニュー。メニューが無い項目（フォルダ、Stashes の見出し）は null。</summary>
    public static ContextMenu? For(RepositoryViewModel repo, SidebarItem item)
    {
        var m = new ContextMenu();
        var refs = repo.References;
        var head = refs?.HeadBranch ?? "HEAD";
        switch (item.Kind)
        {
            case SidebarItemKind.LocalBranch when item.Reference is { } b:
            {
                var upstream = b.Upstream?.Replace("refs/remotes/", "");
                var pushRemote = upstream?.Split('/', 2)[0] ?? refs?.Remotes.FirstOrDefault()?.Name ?? "origin";
                Add(m, "Checkout...", () => RepositoryActions.Checkout(repo, b));
                Add(m, "Checkout as Worktree...", () => RepositoryActions.NewWorktree(repo, existingBranch: b.ShortName));
                Sep(m);
                if (upstream is not null && !b.UpstreamGone) Add(m, $"Fast-Forward to '{upstream}'", () => RepositoryActions.FastForward(repo, b));
                if (b.IsHead && upstream is not null) Add(m, $"Pull '{upstream}'...", () => RepositoryActions.Pull(repo));
                PushItems(m, repo, b, pushRemote);
                Sep(m);
                if (!b.IsHead)
                {
                    var t = RevisionTarget.Branch(b.ShortName);
                    Add(m, $"Merge into '{head}'...", () => RepositoryActions.Merge(repo, t));
                    Add(m, $"Rebase '{head}' on '{b.ShortName}'...", () => RepositoryActions.Rebase(repo, t));
                    Add(m, $"Interactively Rebase '{head}' on '{b.ShortName}...", () => _ = RevisionMenus.OpenInteractiveRebase(repo, b.TargetSha, null, null));
                    Sep(m);
                }
                Add(m, "New Branch...", () => RepositoryActions.NewBranch(repo, RevisionTarget.Branch(b.ShortName)));
                Add(m, "New Tag...", () => RepositoryActions.NewTag(repo, RevisionTarget.Branch(b.ShortName)));
                Sep(m);
                // Git Flow を初期化していれば "Git Flow >"。feature/・release/・hotfix/ のブランチは先頭に Finish（-5）
                if (refs?.GitFlow is { } flow)
                {
                    var gitFlow = new MenuItem { Header = "Git Flow" };
                    if (flow.Classify(b.ShortName) is { } own)
                    {
                        Add(gitFlow, $"Finish '{b.ShortName}'...", () => RepositoryActions.GitFlowFinish(repo, flow, own.Kind, b.ShortName));
                        gitFlow.Items.Add(new Separator());
                    }
                    Add(gitFlow, "Start Feature...", () => RepositoryActions.GitFlowStart(repo, flow, Core.Git.Model.GitFlowKind.Feature));
                    Add(gitFlow, "Start Release...", () => RepositoryActions.GitFlowStart(repo, flow, Core.Git.Model.GitFlowKind.Release));
                    Add(gitFlow, "Start Hotfix...", () => RepositoryActions.GitFlowStart(repo, flow, Core.Git.Model.GitFlowKind.Hotfix));
                    m.Items.Add(gitFlow);
                    Sep(m);
                }
                var tracking = new MenuItem { Header = "Tracking" };
                if (upstream is not null)
                {
                    Add(tracking, "Remove tracking reference", () => RepositoryActions.SetUpstream(repo, b, null));
                    tracking.Items.Add(new Separator());
                }
                foreach (var r in refs?.RemoteBranches ?? [])
                {
                    var name = r.ShortName;
                    var mi = Add(tracking, name, () => RepositoryActions.SetUpstream(repo, b, name));
                    if (name == upstream) mi.FontWeight = System.Windows.FontWeights.SemiBold;   // 今の upstream（チェックの状態は持たない）
                }
                m.Items.Add(tracking);
                Add(m, "Rename...", () => RepositoryActions.RenameBranch(repo, b));
                Add(m, "Delete...", () => RepositoryActions.DeleteBranch(repo, b)).IsEnabled = !b.IsHead;
                Sep(m);
                Ai(m, enabled: !b.IsHead);
                Sep(m);
                Add(m, "Copy Branch Name", () => RepositoryActions.Copy(b.ShortName));
                CustomRef(m, repo, b);
                break;
            }
            case SidebarItemKind.RemoteBranch when item.Reference is { } rb:
            {
                var t = RevisionTarget.Remote(rb.ShortName);
                Add(m, "Checkout...", () => RepositoryActions.Checkout(repo, rb));
                Add(m, "Checkout as Worktree...", () => RepositoryActions.NewWorktree(repo, remoteBranch: rb.ShortName));
                Sep(m);
                Add(m, $"Pull into '{head}'...", () => RepositoryActions.Run(repo, new PullDialogViewModel(repo) { SelectedRemote = rb.RemoteName, SelectedBranch = rb.ShortName }));
                Sep(m);
                Add(m, $"Merge into '{head}'...", () => RepositoryActions.Merge(repo, t));
                Add(m, $"Rebase '{head}' on '{rb.ShortName}'...", () => RepositoryActions.Rebase(repo, t));
                Add(m, $"Interactively Rebase '{head}' on '{rb.ShortName}...", () => _ = RevisionMenus.OpenInteractiveRebase(repo, rb.TargetSha, null, null));
                Sep(m);
                Add(m, "New Branch...", () => RepositoryActions.NewBranch(repo, t));
                Add(m, "New Tag...", () => RepositoryActions.NewTag(repo, t));
                Sep(m);
                Add(m, "Delete...", () => RepositoryActions.Run(repo, new DeleteRemoteBranchDialogViewModel(repo, rb)));
                Ai(m, enabled: refs?.Head?.Upstream != rb.CanonicalName);
                Sep(m);
                Add(m, "Copy Branch Name", () => RepositoryActions.Copy(rb.ShortName));
                CustomRef(m, repo, rb);
                break;
            }
            case SidebarItemKind.Remote when item.Remote is { } remote:
                Add(m, $"Fetch '{remote.Name}'...", () => RepositoryActions.Run(repo, new FetchDialogViewModel(repo) { FetchAll = false, SelectedRemote = remote.Name }));
                Sep(m);
                Add(m, $"Edit '{remote.Name}'...", () => RepositoryActions.Run(repo, new RemoteEditDialogViewModel(repo, remote)));
                Add(m, $"Delete '{remote.Name}'...", () =>
                {
                    // "Are you sure you want to delete reference to remote 'origin'?" / "Do you want to delete 'origin'?" → `remote remove origin`
                    if (Alerts.Confirm($"Are you sure you want to delete reference to remote '{remote.Name}'?", $"Do you want to delete '{remote.Name}'?", "Delete"))
                        RepositoryActions.RunDirect(repo, "Delete Remote", ["remote", "remove", remote.Name]);
                });
                Sep(m);
                // 外すと `config remote.origin.disableimplicitfetch true`、付けると `... false`
                var auto = !repo.IsImplicitFetchDisabled(remote.Name);
                Add(m, $"Fetch '{remote.Name}' Automatically", () => RepositoryActions.RunDirect(repo, "Fetch Automatically",
                    ["config", $"remote.{remote.Name}.disableimplicitfetch", auto ? "true" : "false"])).IsChecked = auto;
                Sep(m);
                // GitHub のリモートは "View on GitHub"（Frieve-Editor、pytube）
                if (GitHubWeb(remote.FetchUrl) is { } web)
                {
                    Add(m, "View on GitHub", () => OpenUrl(web));
                    Sep(m);
                }
                Add(m, "Add New Remote...", () => RepositoryActions.Run(repo, new RemoteEditDialogViewModel(repo, null)));
                Sep(m);
                Add(m, "Copy Remote Address", () => RepositoryActions.Copy(remote.FetchUrl));
                break;
            case SidebarItemKind.Worktree when item.Reference is { WorktreePath: { } wtPath } wt:
                // Open 'wt2'...、Show in File Explorer、---、Delete 'wt2'...、---、Copy Path
                Add(m, $"Open '{wt.ShortName}'...", () => { if (System.Windows.Application.Current.MainWindow?.DataContext is MainViewModel main) _ = main.OpenAsync(System.IO.Path.GetFullPath(wtPath)); });
                Add(m, "Show in File Explorer", () => RepositoryActions.Explorer($"\"{System.IO.Path.GetFullPath(wtPath)}\""));
                Sep(m);
                Add(m, $"Delete '{wt.ShortName}'...", () => RepositoryActions.DeleteWorktree(repo, wt.ShortName, wtPath));
                Sep(m);
                Add(m, "Copy Path", () => RepositoryActions.Copy(System.IO.Path.GetFullPath(wtPath)));
                break;
            case SidebarItemKind.Submodule when item.Submodule is { } sm:
            {
                // 並び
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(repo.Path, sm.Path));
                Add(m, $"Open '{sm.Path}'...", () => RepositoryActions.OpenSubmodule(repo, sm));
                Sep(m);
                Add(m, "Open In Console", () => RepositoryActions.OpenConsole(full));
                Add(m, "Show in File Explorer", () => RepositoryActions.Explorer($"\"{full}\""));
                var editors = ToolbarMenus.Editors().ToList();
                if (editors.Count > 0) Sep(m);
                foreach (var (name, exe) in editors)
                    Add(m, $"Open in {name}", () => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"\"{full}\"") { UseShellExecute = false }));
                Sep(m);
                Add(m, "History... ", () => new HistoryWindow(repo, sm.Path).Show());
                Sep(m);
                Add(m, $"Update '{sm.Path}'", () => RepositoryActions.UpdateSubmodules(repo, sm.Path));
                Add(m, $"Move '{sm.Path}'...", () => RepositoryActions.MoveSubmodule(repo, sm));
                Sep(m);
                Add(m, $"Delete '{sm.Path}'...", () => RepositoryActions.DeleteSubmodule(repo, sm));
                Sep(m);
                Add(m, "Add New Submodule...", () => RepositoryActions.Run(repo, new AddSubmoduleDialogViewModel(repo)));
                Sep(m);
                Add(m, "Copy Path", () => RepositoryActions.Copy(sm.Path));
                Add(m, "Copy Full Path", () => RepositoryActions.Copy(full));
                // submodule のカスタムコマンド（ブランチと同じ形）
                CustomCommandMenus.AddTo(m, repo, "submodule", () => CustomCommandMenus.Submodule(repo, sm.Path));
                break;
            }
            case SidebarItemKind.Tag when item.Reference is { } tag:
            {
                var t = RevisionTarget.Tag(tag.ShortName);
                var remote = refs?.Remotes.FirstOrDefault()?.Name ?? "origin";
                Add(m, "Checkout Commit...", () =>
                {
                    if (repo.Commits.FirstOrDefault(r => r.Commit.Sha == tag.TargetSha) is { } row) RepositoryActions.Run(repo, new CheckoutCommitDialogViewModel(repo, row.Commit));
                });
                Add(m, "Show Tag Details...", () => Dialogs.DialogWindow.Run(new TagDetailsDialogViewModel(repo, tag.ShortName), out _));
                Sep(m);
                Add(m, $"Push to '{remote}'...", () => RepositoryActions.RunDirect(repo, "Push tag", Core.Git.Commands.GitArgs.PushTag(remote, tag.ShortName)));
                Sep(m);
                Add(m, $"Merge into '{head}'...", () => RepositoryActions.Merge(repo, t));
                Add(m, $"Rebase '{head}' on '{tag.ShortName}'...", () => RepositoryActions.Rebase(repo, t));
                Add(m, $"Interactively Rebase '{head}' on '{tag.ShortName}...", () => _ = RevisionMenus.OpenInteractiveRebase(repo, tag.TargetSha, null, null));
                Sep(m);
                Add(m, "New Branch...", () => RepositoryActions.NewBranch(repo, t));
                Add(m, "New Tag...", () => RepositoryActions.NewTag(repo, t));
                Sep(m);
                Add(m, "Delete...", () => RepositoryActions.DeleteTag(repo, tag.ShortName));
                Sep(m);
                Add(m, "Copy Tag Name", () => RepositoryActions.Copy(tag.ShortName));
                CustomRef(m, repo, tag);
                break;
            }
            case SidebarItemKind.Stash when item.Stash is { } stash:
                Add(m, "Apply...", () => RepositoryActions.ApplyStash(repo, stash));
                Add(m, "Rename...", () => RepositoryActions.Run(repo, new RenameStashDialogViewModel(repo, stash, SidebarViewModel.StashTitle(stash.Message))));
                Add(m, "Save as Patch...", () => Dialogs.DialogWindow.Run(new CreatePatchDialogViewModel(repo, PatchSource.Stash, stash.Sha, SidebarViewModel.StashTitle(stash.Message)), out _));
                Sep(m);
                Add(m, "Delete...", () => RepositoryActions.DeleteStash(repo, stash));
                break;
            case SidebarItemKind.Group:
                switch (item.Title)
                {
                    case "Branches":
                        Add(m, "New Branch...", () => RepositoryActions.NewBranch(repo));
                        Add(m, "Select Stale Local Branches", repo.Sidebar.SelectStaleLocalBranches);
                        Sep(m);
                        SortItems(m, "Sort Branches:", repo, () => repo.Settings.LocalBranchSortOrder, v => repo.Settings.LocalBranchSortOrder = v);
                        break;
                    case "Remotes":
                        Add(m, "Add New Remote...", () => RepositoryActions.Run(repo, new RemoteEditDialogViewModel(repo, null)));
                        Sep(m);
                        SortItems(m, "Sort Branches:", repo, () => repo.Settings.RemoteBranchSortOrder, v => repo.Settings.RemoteBranchSortOrder = v);
                        break;
                    case "Tags":
                        Add(m, "New Tag...", () => RepositoryActions.NewTag(repo));
                        Sep(m);
                        SortItems(m, "Sort Tags:", repo, () => repo.Settings.TagSortOrder, v => repo.Settings.TagSortOrder = v);
                        break;
                    case "Submodules":
                        Add(m, "Add New Submodule...", () => RepositoryActions.Run(repo, new AddSubmoduleDialogViewModel(repo)));
                        Add(m, "Update Submodules", () => RepositoryActions.UpdateSubmodules(repo, repo.Submodules.Select(s => s.Path).ToArray())).IsEnabled = repo.Submodules.Count > 0;
                        break;
                    default:
                        return null;
                }
                break;
            default:
                return null;
        }
        return m.Items.Count == 0 ? null : m;
    }

    /// <summary>
    /// 複数選択のメニュー。いまはローカルブランチだけのとき：
    /// "Fast-Forward 2 branches to their upstreams"、"Push 2 branches to 'origin'..."、---、"Delete 2 Branches..."。
    /// </summary>
    public static ContextMenu? ForMany(RepositoryViewModel repo, IReadOnlyList<SidebarItem> items)
    {
        if (!items.All(i => i is { Kind: SidebarItemKind.LocalBranch, Reference: not null })) return null;
        var branches = items.Select(i => i.Reference!).ToList();
        var n = branches.Count;
        var remote = repo.References?.Remotes.FirstOrDefault()?.Name ?? "origin";
        var m = new ContextMenu();
        // 選んだ順に `update-ref refs/heads/<b> refs/remotes/<upstream>`（upstream の無い・消えたブランチは飛ばす＝推定）
        Add(m, $"Fast-Forward {n} branches to their upstreams", () => RepositoryActions.RunDirect(repo, "Fast-Forward",
            branches.Where(b => b.Upstream is not null && !b.UpstreamGone).Select(b => new[] { "update-ref", b.CanonicalName, b.Upstream! }).ToArray()));
        Add(m, $"Push {n} branches to '{remote}'...", () => RepositoryActions.Run(repo, new MultiPushDialogViewModel(repo, remote, branches)));
        Sep(m);
        Add(m, $"Delete {n} Branches...", () => RepositoryActions.Run(repo, new DeleteBranchesDialogViewModel(repo, branches))).IsEnabled = !branches.Any(b => b.IsHead);
        return m;
    }

    private static MenuItem Add(ItemsControl parent, string header, Action action)
    {
        // "_" をアクセスキーにしない（ブランチ名に _ が入る）
        var mi = new MenuItem { Header = new AccessText { Text = header.Replace("_", "__") } };
        System.Windows.Automation.AutomationProperties.SetName(mi, header);
        mi.Click += (_, _) => action();
        parent.Items.Add(mi);
        return mi;
    }

    private static void Sep(ItemsControl parent)
    {
        if (parent.Items.Count > 0 && parent.Items[^1] is not Separator) parent.Items.Add(new Separator());
    }

    private static void Todo(ItemsControl parent, string header) => Add(parent, header, () => Alerts.NotImplemented(header));

    /// <summary>ブランチ・タグのメニューの末尾のカスタムコマンド（"Custom Commands" の見出しつき）。</summary>
    private static void CustomRef(ContextMenu m, RepositoryViewModel repo, Core.Git.Model.ReferenceInfo r)
    {
        if (CustomCommandMenus.RefKind(r) is { } kind)
            CustomCommandMenus.AddTo(m, repo, "ref", () => CustomCommandMenus.Reference(repo, r), kind, header: true);
    }

    /// <summary>
    /// AI > Code Review with Claude...。現在のブランチとその upstream では押せない。
    /// </summary>
    private static void Ai(ItemsControl parent, bool enabled)
    {
        var ai = new MenuItem { Header = "AI" };
        Todo(ai, "Code Review with Claude...");
        ((MenuItem)ai.Items[0]!).IsEnabled = enabled;
        parent.Items.Add(ai);
    }

    /// <summary>
    /// ブランチの Push の項目。リモートが 2 つ以上なら "Push >" の下にリモートごとの "Push to 'x'..."。
    /// GitHub のリモートなら続けて "Create Pull Request on 'x'"（Frieve-Editor、pytube）。
    /// </summary>
    internal static void PushItems(ItemsControl m, RepositoryViewModel repo, Core.Git.Model.ReferenceInfo b, string pushRemote)
    {
        var remotes = repo.References?.Remotes ?? [];
        MenuItem PushTo(ItemsControl parent, string remote)
            => Add(parent, $"Push to '{remote}'...", () => RepositoryActions.Run(repo, new PushDialogViewModel(repo) { SelectedBranch = b.ShortName, SelectedRemote = remote }));
        if (remotes.Count > 1)
        {
            var push = new MenuItem { Header = "Push" };
            foreach (var r in remotes) PushTo(push, r.Name);
            m.Items.Add(push);
        }
        else PushTo(m, pushRemote);
        // Pull Request は push 先のリモート（リモートが複数のときは未確認。BPM のリモートは GitHub ではない）
        if (remotes.FirstOrDefault(r => r.Name == pushRemote) is { } pr && GitHubWeb(pr.FetchUrl) is { } web)
            Add(m, $"Create Pull Request on '{pr.Name}'", () => OpenUrl($"{web}/compare/{Uri.EscapeDataString(b.ShortName).Replace("%2F", "/")}?expand=1"));
    }

    /// <summary>GitHub のリモートなら Web の URL（https://github.com/owner/repo）。それ以外は null。</summary>
    internal static string? GitHubWeb(string? url)
        => url is not null && Core.Git.RemoteWebLinks.WebBase(url) is { } web && new Uri(web).Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? web : null;

    internal static void OpenUrl(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { }   // 既定のブラウザが無い
    }

    /// <summary>並び順（設定 LocalBranchSortOrder 等。1 = Alphabetically、3 = Alphabetically backward、2 = Recently used。値の対応は SidebarViewModel.Sort）。</summary>
    private static void SortItems(ItemsControl parent, string title, RepositoryViewModel repo, Func<int> get, Action<int> set)
    {
        parent.Items.Add(new MenuItem { Header = title, IsEnabled = false });
        foreach (var (s, v) in new[] { ("Alphabetically", 1), ("Alphabetically backward", 3), ("Recently used", 2) })
            Add(parent, s, () => { set(v); _ = repo.RefreshAsync(); }).IsChecked = get() == v;
    }
}

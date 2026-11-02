using GitKnife.Core.Git.Model;
using GitKnife.ViewModels;
using GitKnife.ViewModels.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// Quick Launch に出すもの（最近のリポジトリ 8 件と、コマンド 32 個）。
/// まだ無い機能は「未実装」を出す。ブランチやタグを選ぶコマンドは 2 段目の一覧（推定）。
/// </summary>
internal static class QuickLaunchCommands
{
    public static void Show(MainWindow window, MainViewModel vm, Action<int> zoom, Action openRepository, Action openInExplorer, Action openInConsole)
    {
        var repo = vm.SelectedTab is { References: not null } t ? t : null;
        // Repository Manager の一覧（repositories.toml）。Recent は opened の新しい順
        var list = Core.Settings.RepositoryList.Load();
        var known = list.Repositories;
        QuickLaunchItem RepoItem(Core.Settings.RepositoryListEntry r) => new(r.Name, r.Path, true, () => _ = vm.OpenAsync(r.Path));
        var recent = list.Recent(8).Select(p => list.Get(p)!).Select(RepoItem).ToList();
        var all = known.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Select(RepoItem).ToList();

        QuickLaunchItem Cmd(string name, Action<RepositoryViewModel>? action) => new(name, null, false, () =>
        {
            if (action is null) { Alerts.NotImplemented(name); return; }
            if (repo is not null) action(repo);
        });
        QuickLaunchItem App(string name, Action action) => new(name, null, false, action);
        QuickLaunchItem Pick(string name, Func<RepositoryViewModel, IEnumerable<QuickLaunchItem>> children, string header = "Branches", string placeholder = "branch")
            => new(name, null, false, () => { }, () => repo is null ? [] : children(repo).ToList(), header, placeholder);

        IEnumerable<ReferenceInfo> Locals(RepositoryViewModel r) => r.References!.References.Where(x => x.Kind == ReferenceKind.LocalBranch);
        IEnumerable<QuickLaunchItem> Branches(RepositoryViewModel r, Action<ReferenceInfo> action)
            => Locals(r).Select(b => new QuickLaunchItem(b.ShortName, null, false, () => action(b), Icon: "Icon.Branch"));
        // Checkout Branch はリモートのブランチも出す（リモートは Checkout ダイアログで追跡ブランチを作る：推定）
        IEnumerable<QuickLaunchItem> AllBranches(RepositoryViewModel r)
            => Branches(r, b => RepositoryActions.CheckoutDirect(r, b)).Concat(r.References!.References
                .Where(x => x.Kind == ReferenceKind.RemoteBranch && !x.ShortName.EndsWith("/HEAD", StringComparison.Ordinal))
                .Select(b => new QuickLaunchItem(b.ShortName, null, false, () => RepositoryActions.Checkout(r, b), Icon: "Icon.Branch")));

        var commands = new List<QuickLaunchItem>
        {
            App("Apply Patch…", () => window.ApplyPatchFromQuickLaunch()),
            Cmd("Benchmark...", r => Dialogs.DialogWindow.Run(new BenchmarkDialogViewModel(r), out _)),
            Cmd("Bisect", r => RepositoryActions.RunDirect(r, "Bisect", ["bisect", "start"])),
            Cmd("Blame...", null),
            Pick("Checkout Branch", AllBranches),
            Cmd("Create Branch...", r => RepositoryActions.NewBranch(r)),
            Cmd("Create Tag...", r => RepositoryActions.NewTag(r)),
            Cmd("Create Worktree...", r => RepositoryActions.NewWorktree(r)),
            Pick("Delete Branch...", r => Branches(r, b => RepositoryActions.DeleteBranch(r, b))),
            Pick("Delete Tag...", r => r.References!.References.Where(x => x.Kind == ReferenceKind.Tag)
                .Select(x => new QuickLaunchItem(x.ShortName, null, false, () => RepositoryActions.DeleteTag(r, x.ShortName), Icon: "Icon.Tag")), "Tags", "tag"),
            Cmd("Fast-Forward", r => { if (Locals(r).FirstOrDefault(b => b.IsHead) is { } head) RepositoryActions.FastForward(r, head); }),
            Cmd("Fetch", r => _ = RepositoryActions.FetchNow(r)),
            Cmd("Fetch...", RepositoryActions.Fetch),
            Cmd("File History...", null),
            Cmd("Git Flow: Initialize...", r => RepositoryActions.Run(r, new ViewModels.Dialogs.GitFlowInitDialogViewModel(r))),
            Pick("Merge...", r => Branches(r, b => RepositoryActions.Merge(r, RevisionTarget.Branch(b.ShortName)))),
            App("Open In Console", openInConsole),
            App("Open In File Explorer", openInExplorer),
            App("Open Repository...", openRepository),
            Cmd("Pull...", RepositoryActions.Pull),
            Cmd("Push", r => _ = RepositoryActions.PushNow(r)),
            Cmd("Push...", RepositoryActions.Push),
            Pick("Rebase...", r => Branches(r, b => RepositoryActions.Rebase(r, RevisionTarget.Branch(b.ShortName)))),
            Pick("Rename Branch...", r => Branches(r, b => RepositoryActions.RenameBranch(r, b))),
            App("Repository Manager", () => Alerts.NotImplemented("Repository Manager")),
            Cmd("Repository Settings...", RepositoryActions.RepositorySettings),
            Cmd("Repository Statistics...", null),
            Cmd("Repository Treemap...", null),
            Cmd("Save Stash...", RepositoryActions.SaveStash),
            App("Switch Workspace", () => Alerts.NotImplemented("Switch Workspace")),
            App("Zoom In", () => zoom(+10)),
            App("Zoom Out", () => zoom(-10)),
        };
        new QuickLaunchWindow(recent, commands, all) { Owner = window }.Show();
    }
}

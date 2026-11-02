using System.Windows.Controls;
using GitKnife.Core.Git.Model;
using GitKnife.ViewModels;
using GitKnife.ViewModels.Dialogs;

namespace GitKnife.Views;

/// <summary>
/// コミット一覧の右クリックメニュー。
/// </summary>
internal static class RevisionMenus
{
    /// <summary>
    /// 2 つ選んだときのメニュー：Save Commit Range as Patch…、---、AI >（Code Review with Claude...）、---、Cherry-pick…、---、Copy Commit SHA、Copy Commit Info。
    /// rows は一覧の上から（新しい順）。
    /// </summary>
    public static ContextMenu ForTwo(RepositoryViewModel repo, IReadOnlyList<CommitRow> rows)
    {
        var m = new ContextMenu();
        // rows は新しい順。範囲は「古い方~..新しい方」
        Add(m, "Save Commit Range as Patch…", () =>
        {
            var vm = new ViewModels.Dialogs.CreatePatchDialogViewModel(repo, rows[^1].Commit.Sha, rows[0].Commit.Sha);
            Dialogs.DialogWindow.Run(vm, Dialogs.DialogViews.For(vm));
        });
        Sep(m);
        var ai = new MenuItem { Header = "AI" };
        Todo(ai, "Code Review with Claude...");
        m.Items.Add(ai);
        Sep(m);
        Add(m, "Cherry-pick…", () => RepositoryActions.Run(repo, new ViewModels.Dialogs.MultiCherryPickDialogViewModel(repo, rows.Select(r => r.Commit).ToList())));
        Sep(m);
        var nl = Environment.NewLine;
        Add(m, "Copy Commit SHA", () => RepositoryActions.Copy(string.Join(nl, rows.Select(r => r.Commit.Sha))));
        Add(m, "Copy Commit Info", () => RepositoryActions.Copy(string.Join(nl + nl, rows.Select(r =>
            $"{r.Commit.ShortSha} {r.Commit.Subject}{nl}Author: {r.Commit.AuthorName} <{r.Commit.AuthorEmail}>{nl}Date: {r.Commit.AuthorTime:yyyy-MM-dd HH:mm:ss zzz}"))));
        return m;
    }

    public static ContextMenu For(RepositoryViewModel repo, CommitRow row)
    {
        var m = new ContextMenu();
        var c = row.Commit;
        var head = repo.References?.HeadBranch ?? "HEAD";
        var info = $"{c.ShortSha} {c.Subject}\nAuthor: {c.AuthorName} <{c.AuthorEmail}>\nDate: {c.AuthorTime:yyyy-MM-dd HH:mm:ss zzz}";

        if (c.IsStash && repo.References?.Stashes.FirstOrDefault(s => s.Sha == c.Sha) is { } stash)
        {
            Add(m, "Apply...", () => RepositoryActions.ApplyStash(repo, stash));
            Add(m, "Rename...", () => RepositoryActions.Run(repo, new RenameStashDialogViewModel(repo, stash, SidebarViewModel.StashTitle(stash.Message))));
            Add(m, "Save as Patch...", () => Dialogs.DialogWindow.Run(new CreatePatchDialogViewModel(repo, PatchSource.Stash, c.Sha, SidebarViewModel.StashTitle(stash.Message)), out _));
            Sep(m);
            Add(m, "Delete...", () => RepositoryActions.DeleteStash(repo, stash));
            Sep(m);
            Add(m, "Compare to Local Changes", () => _ = repo.Details.LoadCompareToLocalAsync(row));
            Sep(m);
            Add(m, "Copy Commit SHA", () => RepositoryActions.Copy(c.Sha));
            Add(m, "Copy Commit Info", () => RepositoryActions.Copy(info));
            return m;
        }

        // ラベルごとのサブメニュー（upstream とまとめて表示しているローカルブランチは、リモートの分も出す）
        foreach (var label in row.Labels)
        {
            var refs = repo.References;
            switch (label.Kind)
            {
                case RefLabelKind.CurrentBranch or RefLabelKind.LocalBranch when refs?.LocalBranches.FirstOrDefault(b => b.ShortName == label.Text) is { } b:
                    m.Items.Add(LocalBranchMenu(repo, b, head));
                    if (label.IsSynced && b.Upstream is { } up && refs.RemoteBranches.FirstOrDefault(r => r.CanonicalName == up) is { } upstreamRef)
                        m.Items.Add(RemoteBranchMenu(repo, upstreamRef, head));
                    break;
                case RefLabelKind.RemoteBranch when refs?.RemoteBranches.FirstOrDefault(r => r.ShortName == label.Text) is { } rb:
                    m.Items.Add(RemoteBranchMenu(repo, rb, head));
                    break;
                case RefLabelKind.Tag:
                    m.Items.Add(TagMenu(repo, label.Text, head));
                    break;
            }
        }
        Sep(m);
        var target = RevisionTarget.Commit(c);
        Add(m, "New Branch...", () => RepositoryActions.NewBranch(repo, target));
        Add(m, "New Tag...", () => RepositoryActions.NewTag(repo, target));
        Sep(m);
        if (row.IsReachableFromHead)
        {
            var ir = new MenuItem { Header = "Interactive Rebase" };
            // 対象のコミット自身も書き換えるので、起点はその親（HEAD のコミットでも同じ）
            var parent = c.Parents.Count > 0 ? c.Parents[0] : null;
            void Ir(string? action) => _ = OpenInteractiveRebase(repo, parent, c.Sha, action);
            Add(ir, $"Interactively Rebase '{head}' to Here...", () => _ = OpenInteractiveRebase(repo, c.Sha, null, null));
            ir.Items.Add(new Separator());
            ir.Items.Add(new MenuItem { Header = "Quick Actions", IsEnabled = false });
            ir.Items.Add(new Separator());
            Add(ir, "Reword...", () => Ir("reword")).IsEnabled = parent is not null;
            Add(ir, "Edit...", () => Ir("edit")).IsEnabled = parent is not null;
            Add(ir, "Squash into Parent...", () => Ir("squash")).IsEnabled = parent is not null;
            Add(ir, "Fixup into Parent…", () => Ir("fixup")).IsEnabled = parent is not null;
            Add(ir, "Drop…", () => Ir("drop")).IsEnabled = parent is not null;
            m.Items.Add(ir);
        }
        else
        {
            if (!row.IsLost) Add(m, $"Merge into '{head}'...", () => RepositoryActions.Merge(repo, target));
            Add(m, $"Rebase '{head}' to Here...", () => RepositoryActions.Rebase(repo, target));
            // HEAD の祖先でないコミット：そのコミットの上に HEAD 側のコミットを並べ直す（`rebase -i <sha>`）
            Add(m, $"Interactively Rebase '{head}' to Here...", () => _ = OpenInteractiveRebase(repo, c.Sha, null, null));
        }
        Sep(m);
        if (c.Sha != repo.References?.HeadSha)
        {
            Add(m, $"Reset '{head}' to Here...", () => RepositoryActions.Run(repo, new ResetDialogViewModel(repo, c)));
            Sep(m);
        }
        Add(m, "Checkout Commit...", () => RepositoryActions.Run(repo, new CheckoutCommitDialogViewModel(repo, c)));
        Add(m, "Cherry-pick Commit...", () => RepositoryActions.Run(repo, new CherryPickDialogViewModel(repo, c)));
        Add(m, "Revert Commit...", () => RepositoryActions.Run(repo, new RevertDialogViewModel(repo, c)));
        Add(m, "Save as Patch...", () => Dialogs.DialogWindow.Run(new CreatePatchDialogViewModel(repo, PatchSource.Commit, c.Sha, c.Subject), out _));
        Sep(m);
        Add(m, "Compare to Local Changes", () => _ = repo.Details.LoadCompareToLocalAsync(row));
        Sep(m);
        Add(m, "Copy Commit SHA", () => RepositoryActions.Copy(c.Sha));
        Add(m, "Copy Commit Info", () => RepositoryActions.Copy(info));
        // カスタムコマンド（区切り線の下に見出しなしで並ぶ）
        CustomCommandMenus.AddTo(m, repo, "revision", () => CustomCommandMenus.Commit(repo, c));
        return m;
    }

    /// <summary>ブランチ・タグのサブメニューの末尾のカスタムコマンド（"Custom Commands" の見出しつき）。</summary>
    private static void CustomRef(MenuItem s, RepositoryViewModel repo, ReferenceInfo? r)
    {
        if (r is not null && CustomCommandMenus.RefKind(r) is { } kind)
            CustomCommandMenus.AddTo(s, repo, "ref", () => CustomCommandMenus.Reference(repo, r), kind, header: true);
    }

    /// <summary>
    /// Interactive Rebase のウィンドウを開く。baseSha より後のコミットが対象。Quick Actions は対象のコミット（target）に最初から動作を入れておく
    /// （squash / fixup は親と合わせるので、起点は親のさらに親）。
    /// </summary>
    internal static async Task OpenInteractiveRebase(RepositoryViewModel repo, string? baseSha, string? target, string? action)
    {
        if (baseSha is null) return;
        if (action is "squash" or "fixup")
        {
            var parent = repo.Commits.FirstOrDefault(r => r.Commit.Sha == baseSha)?.Commit;
            if (parent?.Parents.Count is not > 0) return;
            baseSha = parent.Parents[0];
        }
        var @base = repo.Commits.FirstOrDefault(r => r.Commit.Sha == baseSha)?.Commit;
        if (@base is null) return;
        // 窓を先に出すので、変更があれば `git rebase -i` が断るときと同じ条件と同じ文言で先に断る（Git Error）
        var entries = repo.Status?.Entries ?? [];
        var unstaged = entries.Any(e => e.WorkTree is not (Core.Git.Model.FileChange.Unmodified or Core.Git.Model.FileChange.Untracked or Core.Git.Model.FileChange.Ignored));
        var staged = entries.Any(e => e.HasStagedChange);
        if (unstaged || staged)
        {
            var lines = new List<string>();
            if (unstaged) lines.Add("error: cannot rebase: You have unstaged changes.");
            if (staged) lines.Add(unstaged ? "error: additionally, your index contains uncommitted changes." : "error: cannot rebase: Your index contains uncommitted changes.");
            lines.Add("error: Please commit or stash them.");
            Alerts.ShowGitError($"git rebase -i --autosquash --update-refs {@base.Sha}", string.Join("\n", lines));
            return;
        }
        await Task.Yield();
        RepositoryActions.Run(repo, new InteractiveRebaseViewModel(repo, @base, action, target));
    }

    /// <summary>サブメニューの頭（名前の disabled 行と空の行）。</summary>
    private static MenuItem Sub(string name)
    {
        var sub = new MenuItem { Header = new AccessText { Text = name.Replace("_", "__") } };
        System.Windows.Automation.AutomationProperties.SetName(sub, name);
        var title = new MenuItem { Header = new AccessText { Text = name.Replace("_", "__") }, IsEnabled = false };
        System.Windows.Automation.AutomationProperties.SetName(title, name);
        sub.Items.Add(title);
        sub.Items.Add(new MenuItem { Header = "", Focusable = false, IsHitTestVisible = false, Height = 4 });
        sub.Items.Add(new Separator());
        return sub;
    }

    private static MenuItem LocalBranchMenu(RepositoryViewModel repo, ReferenceInfo b, string head)
    {
        var s = Sub(b.ShortName);
        var upstream = b.Upstream?.Replace("refs/remotes/", "");
        var pushRemote = upstream?.Split('/', 2)[0] ?? repo.References?.Remotes.FirstOrDefault()?.Name ?? "origin";
        if (!b.IsHead)
        {
            Add(s, "Checkout", () => RepositoryActions.CheckoutDirect(repo, b));
            Todo(s, "Checkout as Worktree...");
            Sep(s);
        }
        if (upstream is not null && !b.UpstreamGone) Add(s, $"Fast-Forward to '{upstream}'", () => RepositoryActions.FastForward(repo, b));
        if (b.IsHead) Add(s, "Pull...", () => RepositoryActions.Pull(repo));
        SidebarMenus.PushItems(s, repo, b, pushRemote);
        Sep(s);
        if (!b.IsHead)
        {
            Add(s, $"Merge into '{head}'...", () => RepositoryActions.Merge(repo, RevisionTarget.Branch(b.ShortName)));
            Sep(s);
        }
        Add(s, "Rename...", () => RepositoryActions.RenameBranch(repo, b));
        if (!b.IsHead) Add(s, "Delete...", () => RepositoryActions.DeleteBranch(repo, b));
        Sep(s);
        // 現在のブランチでは押せない（Frieve-Editor の master）
        Ai(s, enabled: !b.IsHead);
        Sep(s);
        Add(s, "Copy Branch Name", () => RepositoryActions.Copy(b.ShortName));
        CustomRef(s, repo, b);
        return s;
    }

    private static MenuItem RemoteBranchMenu(RepositoryViewModel repo, ReferenceInfo rb, string head)
    {
        var s = Sub(rb.ShortName);
        Add(s, "Checkout", () => RepositoryActions.Checkout(repo, rb));
        Todo(s, "Checkout as Worktree...");
        // GitHub なら "Create Pull Request on 'origin'"（Frieve-Editor の origin/master）
        if (repo.References?.Remotes.FirstOrDefault(r => r.Name == rb.RemoteName) is { } remote && SidebarMenus.GitHubWeb(remote.FetchUrl) is { } web)
        {
            var branch = rb.ShortName[(remote.Name.Length + 1)..];
            Add(s, $"Create Pull Request on '{remote.Name}'", () => SidebarMenus.OpenUrl($"{web}/compare/{Uri.EscapeDataString(branch).Replace("%2F", "/")}?expand=1"));
        }
        Sep(s);
        // HEAD と同じコミットなら Merge は出さない（Frieve-Editor の origin/master は master と同じコミットで、Merge が無かった。推定）
        if (rb.TargetSha != repo.References?.Head?.TargetSha)
        {
            Add(s, $"Merge into '{head}'...", () => RepositoryActions.Merge(repo, RevisionTarget.Remote(rb.ShortName)));
            Sep(s);
        }
        Add(s, "Delete...", () => RepositoryActions.Run(repo, new DeleteRemoteBranchDialogViewModel(repo, rb)));
        Sep(s);
        // 現在のブランチの upstream では AI のレビューは押せない
        Ai(s, enabled: repo.References?.Head?.Upstream != rb.CanonicalName);
        Sep(s);
        Add(s, "Copy Branch Name", () => RepositoryActions.Copy(rb.ShortName));
        CustomRef(s, repo, rb);
        return s;
    }

    private static MenuItem TagMenu(RepositoryViewModel repo, string tag, string head)
    {
        var s = Sub(tag);
        var remote = repo.References?.Remotes.FirstOrDefault()?.Name ?? "origin";
        Add(s, "Show Tag Details...", () => Dialogs.DialogWindow.Run(new TagDetailsDialogViewModel(repo, tag), out _));
        Add(s, $"Merge into '{head}'...", () => RepositoryActions.Merge(repo, RevisionTarget.Tag(tag)));
        Sep(s);
        Add(s, "Delete...", () => RepositoryActions.DeleteTag(repo, tag));
        Sep(s);
        Add(s, "Copy Tag Name", () => RepositoryActions.Copy(tag));
        Sep(s);
        Add(s, $"Push to '{remote}'...", () => RepositoryActions.RunDirect(repo, "Push tag", Core.Git.Commands.GitArgs.PushTag(remote, tag)));
        CustomRef(s, repo, repo.References?.Tags.FirstOrDefault(t => t.ShortName == tag));
        return s;
    }

    private static MenuItem Add(ItemsControl parent, string header, Action action)
    {
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

    private static void Ai(ItemsControl parent, bool enabled)
    {
        var ai = new MenuItem { Header = "AI" };
        Todo(ai, "Code Review with Claude...");
        ((MenuItem)ai.Items[0]!).IsEnabled = enabled;
        parent.Items.Add(ai);
    }
}

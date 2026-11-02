using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GitKnife.ViewModels;

namespace GitKnife.Views;

/// <summary>
/// Local Changes のファイルの右クリックメニュー。
/// 競合中のファイルは External Merge と "Resolve Using '…'" が入る。
/// </summary>
internal static class LocalChangesMenus
{
    public static ContextMenu? For(LocalChangesViewModel vm, bool staged, IReadOnlyList<FileListItem> selected)
    {
        if (selected.Count == 0) return null;
        var m = new ContextMenu();
        var entries = selected.SelectMany(s => s.Entries).ToList();
        var single = selected.Count == 1 && !selected[0].IsFolder ? selected[0] : null;
        var conflicted = single?.Entry is { IsConflicted: true } c ? c : null;
        var full = single is null ? null : System.IO.Path.Combine(vm.RepositoryPath, single.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var hasFolder = selected.Any(s => s.IsFolder);

        // Open はフォルダを含まなければ使える（消したファイルや複数選択でも）。開けるのは作業ツリーにあるものだけ
        Add(m, "Open", () =>
        {
            foreach (var s in selected)
            {
                var path = System.IO.Path.Combine(vm.RepositoryPath, s.Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
                if (Core.Git.HostFile.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }).IsEnabled = !hasFolder;
        if (conflicted is not null)
        {
            var merge = new MenuItem { Header = "External Merge" };
            foreach (var t in ExternalTools.MergeTools)
            {
                var tool = t;
                Add(merge, t.Name, () => _ = vm.ExternalMergeAsync(tool, conflicted.Path));
            }
            merge.IsEnabled = merge.Items.Count > 0;
            m.Items.Add(merge);
        }
        else if (single is null)
        {
            // 複数選択と Staged のフォルダの行は無効の項目、Unstaged のフォルダの行は項目自体が無い（理由は不明）
            if (!hasFolder || staged) m.Items.Add(new MenuItem { Header = "External Diff", IsEnabled = false, Items = { new MenuItem() } });
        }
        else
        {
            var diff = new MenuItem { Header = "External Diff" };
            foreach (var t in ExternalTools.DiffTools)
            {
                var tool = t;
                Add(diff, t.Name, () => _ = OpenExternalDiff(vm, tool, single, staged));
            }
            diff.IsEnabled = diff.Items.Count > 0;   // 消したファイルでも使える（作業ツリー側は空のファイル）
            m.Items.Add(diff);
        }
        Add(m, "Show in File Explorer", () =>
        {
            var path = System.IO.Path.Combine(vm.RepositoryPath, selected[0].Path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            RepositoryActions.Explorer(Core.Git.HostFile.Exists(path) || Core.Git.HostDirectory.Exists(path) ? $"/select,\"{path}\"" : $"\"{vm.RepositoryPath}\"");
        });
        Sep(m);
        // 別窓で開く（メインとは別のトップレベル窓）。フォルダの履歴は未実装
        Add(m, "Blame/Timeline...", () => { if (single is not null) new BlameWindow(vm.Repository, single.Path).Show(); }).IsEnabled = single is not null;
        if (single is not null) Add(m, "History... ", () => new HistoryWindow(vm.Repository, single.Path, single.IsFolder).Show());
        else if (selected.Count == 1) Todo(m, "History... ");   // フォルダの履歴
        else Add(m, "History... ", () => { }).IsEnabled = false;
        Sep(m);
        // 選んだ中（フォルダの中も）に競合ファイルがあれば、全部をまとめて解決する（複数選択でも出す）
        var conflictedAll = entries.Where(e => e.IsConflicted).ToList();
        if (conflictedAll.Count > 0 && vm.Conflict is { } info)
        {
            Add(m, $"Resolve Using '{info.OursName}'", () => _ = vm.ResolveUsing(conflictedAll, ours: true));
            Add(m, $"Resolve Using '{info.TheirsName}'", () => _ = vm.ResolveUsing(conflictedAll, ours: false));
            Sep(m);
        }
        if (staged)
        {
            Add(m, "Unstage", () => _ = vm.Unstage(entries));
            Sep(m);
            if (selected.Count == 1) Add(m, "Unstage All", () => _ = vm.UnstageAll());   // 複数選択では出ない
        }
        else
        {
            Add(m, "Stage", () => _ = vm.Stage(entries));
            Add(m, "Discard changes...", () => vm.DiscardSelectedCommand.Execute(null));
            Sep(m);
            if (selected.Count == 1) Add(m, "Stage All", () => _ = vm.StageAll());   // 複数選択では出ない
            Sep(m);
            var ignore = new MenuItem { Header = "Ignore" };
            if (selected.Count > 1)
            {
                // 複数選択は "Ignore N Selected Files" だけ（N はフォルダを除いたファイルの数。パスをそれぞれ .gitignore の末尾に足す。足し方は推定）
                var paths = selected.Where(s => !s.IsFolder).Select(s => s.Path).ToList();
                Add(ignore, $"Ignore {paths.Count} Selected Files", () => _ = vm.IgnoreAsync(string.Join("\n", paths)));
            }
            else if (single is not null)
            {
                Add(ignore, $"Ignore '{single.Name}'", () => _ = vm.IgnoreAsync(single.Path));
                var ext = System.IO.Path.GetExtension(single.Name);
                // "..." の付いたものは確認してから足す
                if (ext.Length > 0) Add(ignore, $"Ignore All {ext} Files...", () => IgnorePattern(vm, "*" + ext));
            }
            else if (selected.Count == 1 && selected[0].IsFolder)
            {
                var folder = selected[0].Path;
                Add(ignore, $"Ignore all files in '{folder}'...", () => IgnorePattern(vm, folder + "/"));
            }
            if (selected.Count == 1)
            {
                ignore.Items.Add(new Separator());
                Add(ignore, "Custom Pattern...", () => IgnorePattern(vm, single?.Path ?? selected[0].Path + (selected[0].IsFolder ? "/" : "")));
            }
            m.Items.Add(ignore);
        }
        Sep(m);
        // Save stash（一部）：変更のある全ファイルを並べ、選んだものにチェック（並びは大文字小文字を区別しない名前順）
        Add(m, entries.Count == 1 ? "Stash 1 File..." : $"Stash {entries.Count} Files...", () =>
        {
            var all = (vm.Repository.Status?.Entries ?? []).Where(e => e.WorkTree != Core.Git.Model.FileChange.Ignored)
                .Select(e => e.Path).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            RepositoryActions.Run(vm.Repository, new ViewModels.Dialogs.PartialStashDialogViewModel(vm.Repository, all, entries.Select(e => e.Path)));
        });
        Add(m, "Save as Patch…", () => _ = vm.SaveAsPatchAsync(entries, staged));
        // ファイルのカスタムコマンド（Save as Patch… の後、区切りを挟む。Unstaged / Staged とも）
        if (single is not null)
            CustomCommandMenus.AddTo(m, vm.Repository, "file", () => new Core.Settings.CustomCommandContext(vm.RepositoryPath) { File = single.Path });
        Sep(m);
        Add(m, "Copy Path", () => Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(s => s.Path))));
        Add(m, "Copy Full Path", () => Clipboard.SetText(string.Join(Environment.NewLine, selected.Select(s => System.IO.Path.Combine(vm.RepositoryPath, s.Path.Replace('/', System.IO.Path.DirectorySeparatorChar))))));
        return m;
    }

    /// <summary>外部 diff：unstaged は index と作業ツリー、staged は HEAD と index（古い側は一時ファイルに書き出す）。</summary>
    private static async Task OpenExternalDiff(LocalChangesViewModel vm, ExternalDiffTool tool, FileListItem item, bool staged)
    {
        var path = item.Path;
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GitKnife", "diff-" + Guid.NewGuid().ToString("N")[..8]);
        Core.Git.HostDirectory.CreateDirectory(dir);
        var name = System.IO.Path.GetFileName(path);
        var left = System.IO.Path.Combine(dir, (staged ? "HEAD_" : "INDEX_") + name);
        await Core.Git.HostFile.WriteAllTextAsync(left, await vm.ReadRevisionFile(staged ? $"HEAD:{path}" : $":{path}"));
        string right;
        if (staged)
        {
            right = System.IO.Path.Combine(dir, "STAGED_" + name);
            await Core.Git.HostFile.WriteAllTextAsync(right, await vm.ReadRevisionFile($":{path}"));
        }
        else
        {
            right = System.IO.Path.Combine(vm.RepositoryPath, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
            // 消したファイルは作業ツリーに無いので空のファイルと比べる
            if (!Core.Git.HostFile.Exists(right))
            {
                right = System.IO.Path.Combine(dir, "DELETED_" + name);
                await Core.Git.HostFile.WriteAllTextAsync(right, "");
            }
        }
        tool.Open(left, right);
    }

    /// <summary>"Add Pattern to .gitignore" ダイアログ。</summary>
    private static void IgnorePattern(LocalChangesViewModel vm, string pattern)
    {
        var dlg = new ViewModels.Dialogs.IgnorePatternDialogViewModel(vm, pattern);
        Dialogs.DialogWindow.Run(dlg, Dialogs.DialogViews.For(dlg));
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

    private static MenuItem Todo(ItemsControl parent, string header) => Add(parent, header, () => Alerts.NotImplemented(header));
}

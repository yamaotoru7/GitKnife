using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GitKnife.ViewModels;

namespace GitKnife.Views;

/// <summary>
/// ツールバーのドロップダウン：Stash、Branch（Lean Branching）、Open In、Appearance、Workspaces。
/// Lean Branching の Start / Sync / Finish、Save Snapshot...、New Worktree...、Configure... は未実装。
/// </summary>
internal static class ToolbarMenus
{
    public static void Show(FrameworkElement anchor, ContextMenu menu)
    {
        menu.PlacementTarget = anchor;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Stash ▼："Recent Stashes"（見出し）、---、stash ごと（選ぶと Apply）、---、Save Snapshot...</summary>
    public static ContextMenu Stash(RepositoryViewModel? repo)
    {
        var m = new ContextMenu();
        m.Items.Add(new MenuItem { Header = "Recent Stashes", IsEnabled = false });
        m.Items.Add(new Separator());
        foreach (var s in repo?.References?.Stashes ?? [])
        {
            var stash = s;
            Add(m, SidebarViewModel.StashTitle(s.Message), () => RepositoryActions.ApplyStash(repo!, stash));
        }
        m.Items.Add(new Separator());
        Add(m, "Save Snapshot...", () => { if (repo is not null) RepositoryActions.Run(repo, new ViewModels.Dialogs.SaveSnapshotDialogViewModel(repo)); }).IsEnabled = repo is not null;
        return m;
    }

    /// <summary>Branch ▼：New Branch...、New Worktree...、---、Start Branch on 'main'...、Sync 'main' (Rebase on 'origin/main')、Finish (Merge into 'main')...</summary>
    public static ContextMenu Branch(RepositoryViewModel? repo)
    {
        var m = new ContextMenu();
        var newBranch = Add(m, "New Branch...", () => { if (repo is not null) RepositoryActions.NewBranch(repo); });
        newBranch.IsEnabled = repo is not null;
        newBranch.InputGestureText = "Ctrl+Shift+B";
        Add(m, "New Worktree...", () => { if (repo is not null) RepositoryActions.NewWorktree(repo); }).IsEnabled = repo is not null;
        m.Items.Add(new Separator());
        // 節の見出し（名前の無い項目で、灰色の "Lean Branching" と右端の青い "?"）
        // "?" は説明のツールチップ
        var leanTitle = new TextBlock { Text = "Lean Branching" };
        leanTitle.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        var leanHelp = new TextBlock { Text = "?", FontWeight = FontWeights.SemiBold, Margin = new Thickness(60, 0, 0, 0), Background = System.Windows.Media.Brushes.Transparent,
            ToolTip = "Lean Branching: start a short-lived branch from the main branch, keep it in sync, and finish by merging it back." };
        leanHelp.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Accent");
        var leanHeader = new StackPanel { Orientation = Orientation.Horizontal };
        leanHeader.Children.Add(leanTitle);
        leanHeader.Children.Add(leanHelp);
        m.Items.Add(new MenuItem { Header = leanHeader, StaysOpenOnClick = true, Focusable = false });
        // Lean Branching：main にいるときは main を origin/main に合わせる。ほかのブランチにいるときは main に合わせて、main に merge する
        var main = repo?.LeanMainBranch ?? "main";
        var current = repo?.References?.HeadBranch;
        Add(m, $"Start Branch on '{main}'...", () => { if (repo is not null) RepositoryActions.Run(repo, new ViewModels.Dialogs.StartBranchDialogViewModel(repo, main)); }).IsEnabled = repo is not null;
        if (current is null || current == main)
        {
            Add(m, $"Sync '{main}' (Rebase on 'origin/{main}')", () => { if (repo is not null) _ = RepositoryActions.SyncAsync(repo, main, $"refs/remotes/origin/{main}"); }).IsEnabled = repo is not null;
            m.Items.Add(new MenuItem { Header = $"Finish (Merge into '{main}')...", IsEnabled = false });
        }
        else
        {
            Add(m, $"Sync '{current}' (Rebase on '{main}')", () => _ = RepositoryActions.SyncAsync(repo!, current, $"refs/heads/{main}", rebaseMerges: false));
            Add(m, $"Finish '{current}' (Merge into '{main}')...", () => RepositoryActions.FinishBranch(repo!, current, main));
        }
        return m;
    }

    /// <summary>Open In ▼：Open In Console、Open In File Explorer、---、入っているエディタ（"Open in Visual Studio Code" など）。</summary>
    public static ContextMenu OpenIn(RepositoryViewModel? repo, Action console, Action explorer)
    {
        var m = new ContextMenu();
        Add(m, "Open In Console", console);
        Add(m, "Open In File Explorer", explorer);
        var editors = Editors().ToList();
        if (editors.Count > 0) m.Items.Add(new Separator());
        foreach (var (name, exe) in editors)
            Add(m, $"Open in {name}", () => { if (repo is not null) Process.Start(new ProcessStartInfo(exe, $"\"{repo.Path}\"") { UseShellExecute = false }); });
        // エディタの後ろに区切り線を残す
        if (editors.Count > 0) m.Items.Add(new Separator());
        // repository のカスタムコマンド（区切り線の下、アイコンなし。区切り線は 1 本）
        if (repo is { IsManager: false }) CustomCommandMenus.AddTo(m, repo, "repository", () => CustomCommandMenus.Repository(repo));
        return m;
    }

    /// <summary>この PC のエディタ（Antigravity、Antigravity IDE、Visual Studio Code、Cursor）。</summary>
    internal static IEnumerable<(string name, string exe)> Editors()
    {
        var programs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
        foreach (var (name, rel) in new[]
        {
            ("Antigravity", @"Antigravity\Antigravity.exe"),
            ("Antigravity IDE", @"Antigravity IDE\Antigravity IDE.exe"),
            ("Cursor", @"cursor\Cursor.exe"),
            ("Visual Studio Code", @"Microsoft VS Code\Code.exe"),
        })
        {
            var exe = Path.Combine(programs, rel);
            if (File.Exists(exe)) yield return (name, exe);
        }
    }

    /// <summary>Appearance：Theme（見出し）、System、Light、Dark、---、Commit List Layout（見出し）、Horizontal、Vertical。</summary>
    public static ContextMenu Appearance(Action<int> setOrientation)
    {
        var m = new ContextMenu();
        m.Items.Add(new MenuItem { Header = "Theme", IsEnabled = false });
        // System は Windows のアプリの配色に合わせる（FollowSystemTheme）。Light／Dark を選ぶとそのテーマに固定する
        var system = Add(m, "System", () =>
        {
            App.Settings.FollowSystemTheme = true;
            ThemeManager.Apply(App.Settings);
        });
        system.IsCheckable = false;
        system.IsChecked = App.Settings.FollowSystemTheme;
        System.Windows.Automation.AutomationProperties.SetAutomationId(system, "ThemeSystemMenuItem");
        foreach (var (name, dark) in new[] { ("Light", false), ("Dark", true) })
        {
            var item = Add(m, name, () =>
            {
                App.Settings.FollowSystemTheme = false;
                App.Settings.Theme = dark ? 1 : 0;
                ThemeManager.Apply(App.Settings);
            });
            item.IsCheckable = false;
            item.IsChecked = !App.Settings.FollowSystemTheme && ThemeManager.IsDark == dark;
        }
        m.Items.Add(new Separator());
        m.Items.Add(new MenuItem { Header = "Commit List Layout", IsEnabled = false });
        // RevisionListOrientation：1 = Horizontal、0 = Vertical
        Add(m, "Horizontal", () => setOrientation(1)).IsChecked = App.Settings.RevisionListOrientation != 0;
        Add(m, "Vertical", () => setOrientation(0)).IsChecked = App.Settings.RevisionListOrientation == 0;
        return m;
    }

    /// <summary>Workspaces："Workspaces"（見出し）、ワークスペースの名前（選ぶと切り替え。ドラッグで並べ替え）、---、Configure...</summary>
    public static ContextMenu Workspaces(MainViewModel vm)
    {
        var m = new ContextMenu();
        m.Items.Add(new MenuItem { Header = "Workspaces", IsEnabled = false });
        AddWorkspaces(m, vm, name => _ = vm.SwitchWorkspaceAsync(name));
        WorkspaceMenuDrag.Attach(m, vm);
        m.Items.Add(new Separator());
        Add(m, "Configure...", () =>
        {
            var dlg = new ViewModels.Dialogs.WorkspacesDialogViewModel(vm);
            Dialogs.DialogWindow.Run(dlg, out _);
            dlg.Apply();
        });
        // Fork の設定から取り込む
        System.Windows.Automation.AutomationProperties.SetAutomationId(Add(m, "Import from Fork...", () => _ = ImportFromForkAsync(vm)), "ImportFromForkMenuItem");
        return m;
    }

    /// <summary>Fork の Workspaces・Repository Manager・タブの色で置き換える（確認してから。ForkImport）。</summary>
    private static async Task ImportFromForkAsync(MainViewModel vm)
    {
        Core.Settings.ForkImportSource? source;
        try { source = Core.Settings.ForkImport.Read(); }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Alerts.ShowError("Import from Fork", $"Reading Fork settings failed: {ex.Message}");
            return;
        }
        if (source is null)
        {
            Alerts.ShowError("Import from Fork", $"Fork settings were not found: {Core.Settings.ForkImport.DefaultSettingsPath}");
            return;
        }
        var all = source.Workspaces.All;
        var tabs = all.Sum(w => w.Repositories.Count);
        var message = $"Replace all workspaces with Fork's {all.Count} workspace(s) ({tabs} tabs, active '{source.Workspaces.ActiveWorkspace}')"
            + (source.RepositoryListPath is null ? "" : ", and the Repository Manager list and tab colors")
            + $"? Open tabs are closed. The current settings are kept as *{Core.Settings.ForkImport.BackupSuffix}."
            + (Process.GetProcessesByName("Fork").Length > 0 ? " Fork is running: its latest tabs are saved only when it exits." : "");
        if (!Alerts.Confirm("Import Workspaces from Fork", message, "Replace")) return;
        try { await vm.ImportFromForkAsync(source); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Alerts.ShowError("Import from Fork", ex.Message);
        }
    }

    /// <summary>
    /// ワークスペースの一覧をメニューに足す。名前の '/' はフォルダとしてサブメニューに分ける
    /// （入れ子ワークスペース）。今のワークスペースとそれを含むフォルダに印。
    /// </summary>
    public static void AddWorkspaces(ItemsControl parent, MainViewModel vm, Action<string> action)
    {
        var active = vm.Settings.Workspaces.ActiveWorkspace;
        var folders = new Dictionary<string, MenuItem>(StringComparer.Ordinal);
        foreach (var name in vm.Settings.Workspaces.All.Select(w => w.Name).DefaultIfEmpty(active).Distinct())
        {
            var parts = Core.Settings.WorkspaceOrder.Parts(name);
            ItemsControl host = parent;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var key = string.Join('/', parts[..(i + 1)]);
                if (!folders.TryGetValue(key, out var folder))
                {
                    folder = new MenuItem { Header = new AccessText { Text = parts[i].Replace("_", "__") }, Tag = Core.Settings.WorkspaceOrder.FolderKey(parts, i + 1) };
                    System.Windows.Automation.AutomationProperties.SetName(folder, parts[i]);
                    host.Items.Add(folder);
                    folders[key] = folder;
                }
                if (active.StartsWith(key + "/", StringComparison.Ordinal)) folder.IsChecked = true;
                host = folder;
            }
            var n = name;
            var item = Add(host, parts[^1], () => action(n));
            item.IsChecked = n == active;
            item.Tag = n;  // 並べ替えのキー（WorkspaceMenuDrag）
        }
    }

    private static MenuItem Add(ItemsControl parent, string header, Action action)
    {
        var mi = new MenuItem { Header = new AccessText { Text = header.Replace("_", "__") } };
        System.Windows.Automation.AutomationProperties.SetName(mi, header);
        mi.Click += (_, _) => action();
        parent.Items.Add(mi);
        return mi;
    }

    private static MenuItem Todo(ItemsControl parent, string header) => Add(parent, header, () => Alerts.NotImplemented(header));
}

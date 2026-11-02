using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using GitKnife.Core.Git;
using GitKnife.ViewModels;

namespace GitKnife.Views;

/// <summary>
/// コミット詳細のファイルの右クリック。
/// Changes：Open、External Diff >、Show in File Explorer、---、Reset File to >（State At Commit... / State Before Commit...）、---、
/// Blame/Timeline...、History... 、Show in File Tree、---、Save as...、---、Copy Path、Copy Full Path（フォルダは History と Show in File Explorer、Copy だけ押せる）。
/// File Tree：（ファイルの行は Open）、Show in File Explorer、---、Blame/Timeline...、History... 、---、Save as...、---、Copy Path、Copy Full Path。
/// Reset File to の git コマンドは未採取（`checkout &lt;sha&gt; -- path` と推定。確認してから実行）。
/// </summary>
internal static class RevisionFileMenus
{
    public static ContextMenu? For(RevisionDetailsViewModel details, FileListItem item, bool fileTree)
    {
        if (details.Commit?.Commit is not { } commit) return null;
        var repo = details.Repository;
        var isFile = !item.IsFolder;
        var full = Path.Combine(repo.Path, item.Path.Replace('/', Path.DirectorySeparatorChar));
        var parent = RevisionReader.BaseOf(commit);
        var m = new ContextMenu();

        // File Tree でもファイルの行には Open（フォルダの行には無い）
        if (fileTree && isFile) Add(m, "Open", () => _ = OpenRevisionFile(repo, commit.Sha, item.Path));
        if (!fileTree)
        {
            Add(m, "Open", () => _ = OpenRevisionFile(repo, commit.Sha, item.Path)).IsEnabled = isFile;
            var diff = new MenuItem { Header = "External Diff", IsEnabled = isFile && ExternalTools.DiffTools.Count > 0 };
            foreach (var t in ExternalTools.DiffTools)
            {
                var tool = t;
                Add(diff, t.Name, () => _ = ExternalDiff(repo, tool, parent, commit.Sha, item.Path));
            }
            if (diff.Items.Count == 0) diff.Items.Add(new MenuItem());
            m.Items.Add(diff);
        }
        Add(m, "Show in File Explorer", () => RepositoryActions.Explorer(Core.Git.HostFile.Exists(full) || Core.Git.HostDirectory.Exists(full) ? $"/select,\"{full}\"" : $"\"{repo.Path}\""));
        m.Items.Add(new Separator());
        if (!fileTree)
        {
            var reset = new MenuItem { Header = "Reset File to", IsEnabled = isFile };
            Add(reset, "State At Commit...", () => ResetFile(repo, commit.Sha, item.Path, "at"));
            Add(reset, "State Before Commit...", () => ResetFile(repo, commit.Sha + "~1", item.Path, "before"));
            m.Items.Add(reset);
            m.Items.Add(new Separator());
        }
        Add(m, "Blame/Timeline...", () => new BlameWindow(repo, item.Path).Show()).IsEnabled = isFile;
        Add(m, "History... ", () => new HistoryWindow(repo, item.Path, item.IsFolder).Show());
        if (!fileTree) Add(m, "Show in File Tree", () => _ = details.ShowInFileTreeAsync(item.Path)).IsEnabled = isFile;
        m.Items.Add(new Separator());
        Add(m, "Save as...", () => _ = SaveAs(repo, commit.Sha, item.Path)).IsEnabled = isFile;
        // カスタムコマンド（Save as... と Copy Path のあいだ）
        if (isFile) CustomCommandMenus.AddTo(m, repo, "file", () => CustomCommandMenus.File(repo, commit, item.Path));
        m.Items.Add(new Separator());
        Add(m, "Copy Path", () => Clipboard.SetText(item.Path));
        Add(m, "Copy Full Path", () => Clipboard.SetText(full));
        return m;
    }

    private static async Task<byte[]> ReadBlob(RepositoryViewModel repo, string revision, string path)
    {
        var r = await repo.Git.RunAsync(new GitRequest(repo.Path, ["show", $"{revision}:{path}"]) { BinaryOutput = true });
        return r.Succeeded ? r.BinaryStandardOutput ?? [] : [];
    }

    private static string TempPath(string prefix, string path)
    {
        var dir = Path.Combine(Path.GetTempPath(), "GitKnife", "rev-" + Guid.NewGuid().ToString("N")[..8]);
        Core.Git.HostDirectory.CreateDirectory(dir);
        return Path.Combine(dir, prefix + Path.GetFileName(path));
    }

    /// <summary>そのコミットでの中身を一時ファイルに書いて開く。</summary>
    internal static async Task OpenRevisionFile(RepositoryViewModel repo, string sha, string path)
    {
        var file = TempPath(sha[..7] + "_", path);
        await Core.Git.HostFile.WriteAllBytesAsync(file, await ReadBlob(repo, sha, path));
        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
    }

    private static async Task ExternalDiff(RepositoryViewModel repo, ExternalDiffTool tool, string parent, string sha, string path)
    {
        var left = TempPath(parent[..Math.Min(7, parent.Length)] + "_", path);
        var right = TempPath(sha[..7] + "_", path);
        await Core.Git.HostFile.WriteAllBytesAsync(left, await ReadBlob(repo, parent, path));
        await Core.Git.HostFile.WriteAllBytesAsync(right, await ReadBlob(repo, sha, path));
        tool.Open(left, right);
    }

    internal static void ResetFile(RepositoryViewModel repo, string revision, string path, string when)
    {
        var text = when == "at" ? "the state at this commit" : "the state before this commit";
        if (!Alerts.Confirm("Reset File", $"Do you want to reset '{path}' to {text}? Local changes in the file will be lost.", "Reset")) return;
        RepositoryActions.RunDirect(repo, "Reset File", ["checkout", revision, "--", path]);
    }

    internal static async Task SaveAs(RepositoryViewModel repo, string sha, string path)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog { FileName = Path.GetFileName(path), Title = "Save as" };
        if (dlg.ShowDialog(Application.Current.MainWindow) != true) return;
        await Core.Git.HostFile.WriteAllBytesAsync(dlg.FileName, await ReadBlob(repo, sha, path));
    }

    private static MenuItem Add(ItemsControl parent, string header, Action action)
    {
        var mi = new MenuItem { Header = new AccessText { Text = header.Replace("_", "__") } };
        System.Windows.Automation.AutomationProperties.SetName(mi, header);
        mi.Click += (_, _) => action();
        parent.Items.Add(mi);
        return mi;
    }
}

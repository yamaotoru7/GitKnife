using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitKnife.Core.Diff;
using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;
using GitKnife.Core.Logging;
using GitKnife.Views;

namespace GitKnife.ViewModels;

/// <summary>diff の上で選んだ行に対する操作。</summary>
public enum LinesAction { Stage, Unstage, Discard }

/// <summary>
/// Local Changes。Unstaged / Staged の 2 リスト、選択ファイルの diff、コミット欄。
/// </summary>
public sealed partial class LocalChangesViewModel : ObservableObject
{
    private readonly RepositoryViewModel _repo;
    public RepositoryViewModel Repository => _repo;
    private List<FileListItem> _unstagedRoots = [];
    private List<FileListItem> _stagedRoots = [];
    private CancellationTokenSource? _diffCts;

    public ObservableCollection<FileListItem> UnstagedRows { get; } = [];
    public ObservableCollection<FileListItem> StagedRows { get; } = [];
    /// <summary>コミットメッセージのドロップダウン（最近のコミットの件名）。</summary>
    public ObservableCollection<CommitInfo> RecentMessages { get; } = [];

    private IReadOnlyList<FileListItem> _selectedUnstaged = [];
    private IReadOnlyList<FileListItem> _selectedStaged = [];

    /// <summary>diff を出しているファイル（null なら diff なし）。</summary>
    /// <summary>
    /// ファイル一覧のフィルタ（上端の虫眼鏡 / Ctrl+F。大文字小文字を区別せず、パスの途中でも当たる、
    /// Unstaged と Staged の両方を絞る。閉じたら空に戻す）。
    /// </summary>
    [ObservableProperty] private string _fileFilter = "";
    [ObservableProperty] private FileDiff? _diff;
    [ObservableProperty] private string _diffTitle = "";
    [ObservableProperty] private bool _diffIsStaged;
    [ObservableProperty] private StatusEntry? _diffEntry;
    /// <summary>選んだファイルが競合中なら、その案内（diff の代わりに出す）。</summary>
    [ObservableProperty] private ConflictInfo? _conflict;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCommit), nameof(SubjectLengthText), nameof(SubjectLengthLevel))] private string _subject = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCommit), nameof(CommitButtonText), nameof(Warning))] private bool _amend;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanCommit))] private bool _isWorking;
    [ObservableProperty] private bool _hasUnstagedSelection;
    [ObservableProperty] private bool _hasStagedSelection;

    public LocalChangesViewModel(RepositoryViewModel repo) => _repo = repo;

    public string RepositoryPath => _repo.Path;

    /// <summary>件名欄にフォーカスしてほしい（Ctrl+1）。</summary>
    public event EventHandler? FocusSubjectRequested;
    public void RequestFocusSubject() => FocusSubjectRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>表示の設定（FileListMode、DiffContextSize、DiffShowEntireFile、CommitDiffLayoutMode）が変わった。</summary>
    public void SettingsChanged()
    {
        if (_repo.Status is { } s) Load(s, force: true);
    }

    private FileListMode Mode => (FileListMode)_repo.Settings.FileListMode;
    private WorkingTree? Tree => _repo.WorkingTree;
    public int StagedCount => _stagedRoots.SelectMany(r => r.Entries).Count();
    public int UnstagedCount => _unstagedRoots.SelectMany(r => r.Entries).Count();

    /// <summary>"Commit 1 File" / "Commit 3 Files"、staged が無ければ "Commit"。</summary>
    /// <summary>"Commit 1 File" / "Commit 3 Files"、staged が無ければ "Commit"、Amend なら "Amend Last Commit"。</summary>
    public string CommitButtonText => Amend ? "Amend Last Commit" : StagedCount switch { 0 => "Commit", 1 => "Commit 1 File", var n => $"Commit {n} Files" };
    /// <summary>WarningTextBlock：push 済みのコミットを amend しようとしている。</summary>
    public string? Warning => Amend && _repo.References?.Head is { Upstream: not null, Ahead: 0, UpstreamGone: false } ? "Amending commit that has already been pushed" : null;
    public bool CanCommit => !IsWorking && Subject.Trim().Length > 0 && (StagedCount > 0 || Amend);
    /// <summary>件名の文字数（下限 CommitSubjectLowLimit を超えたら出す）。</summary>
    public string SubjectLengthText => Subject.Length > _repo.Settings.CommitSubjectLowLimit ? Subject.Length.ToString() : "";
    /// <summary>0 = 普通、1 = 下限超え、2 = 上限超え（色を変える）。</summary>
    public int SubjectLengthLevel => Subject.Length > _repo.Settings.CommitSubjectHighLimit ? 2 : Subject.Length > _repo.Settings.CommitSubjectLowLimit ? 1 : 0;

    // ------------------------------------------------------------------ 一覧

    /// <summary>status を一覧に反映する。選択と展開状態は引き継ぎ、diff を読み直す。</summary>
    private bool MatchesFilter(StatusEntry e)
        => FileFilter.Length == 0 || e.Path.Contains(FileFilter, StringComparison.OrdinalIgnoreCase)
           || e.OriginalPath?.Contains(FileFilter, StringComparison.OrdinalIgnoreCase) == true;

    partial void OnFileFilterChanged(string value)
    {
        if (_repo.Status is { } st) Load(st, force: true);
    }

    /// <summary>
    /// force = false（読み直し：自動 fetch、ファイルの監視、定期の status）のときは、一覧が前と同じなら一覧を作り直さず、
    /// diff も中身が同じなら差し替えない（表示のちらつきとスクロール位置が先頭に戻るのを避ける）。
    /// 表示の設定やフィルタを変えたときは force = true で作り直す。
    /// </summary>
    public void Load(RepositoryStatus status, bool force = false)
    {
        var expandedOff = FileListItem.Flatten(_unstagedRoots).Concat(FileListItem.Flatten(_stagedRoots))
            .Where(r => r.IsFolder && !r.IsExpanded).Select(r => r.Path).ToHashSet();
        var selU = _selectedUnstaged.Select(s => s.Path).ToHashSet();
        var selS = _selectedStaged.Select(s => s.Path).ToHashSet();

        // Hide Untracked Files / Show Ignored Files（ファイル一覧の設定ドロップダウン）
        var unstaged = status.Unstaged.Where(e => !(_repo.HideUntracked && e.WorkTree == FileChange.Untracked))
            .Concat(_repo.ShowIgnored ? status.Entries.Where(e => e.WorkTree == FileChange.Ignored) : []);
        var newUnstaged = FileListItem.Build(unstaged.Where(MatchesFilter), Mode, staged: false);
        // Amend 中は Staged に最後のコミットの変更も出す
        var newStaged = FileListItem.Build((Amend && _amendStaged is { } amendStaged ? amendStaged : status.Staged).Where(MatchesFilter), Mode, staged: true);
        if (Amend) _ = RefreshAmendStagedAsync();
        foreach (var f in AllRows(newUnstaged).Concat(AllRows(newStaged)))
            if (f.IsFolder && expandedOff.Contains(f.Path)) f.IsExpanded = false;
        if (!force && SameRows(_unstagedRoots, newUnstaged) && SameRows(_stagedRoots, newStaged))
        {
            // 一覧は変わらない：行と選択はそのまま、diff だけ読み直して変わったときだけ差し替える
            PrefillMergeMessage();
            _ = LoadDiffAsync(keepIfSame: true);
            return;
        }
        _unstagedRoots = newUnstaged;
        _stagedRoots = newStaged;
        RebuildRows();

        _selectedUnstaged = FileListItem.Flatten(_unstagedRoots).Where(r => selU.Contains(r.Path)).ToList();
        _selectedStaged = FileListItem.Flatten(_stagedRoots).Where(r => selS.Contains(r.Path)).ToList();
        // 選んでいたファイルが反対側へ移った（ステージした）ときは、移った先を選ぶ
        if (_selectedUnstaged.Count == 0 && _selectedStaged.Count == 0 && DiffEntry is { } d)
        {
            var moved = FileListItem.Flatten(DiffIsStaged ? _unstagedRoots : _stagedRoots).FirstOrDefault(r => r.Path == d.Path);
            if (moved is not null)
            {
                if (DiffIsStaged) _selectedUnstaged = [moved]; else _selectedStaged = [moved];
            }
        }
        // どちらも選んでいなければ、Unstaged の先頭のファイル（無ければ Staged の先頭）を選んで diff を出す
        if (_selectedUnstaged.Count == 0 && _selectedStaged.Count == 0)
        {
            if (FileListItem.Flatten(_unstagedRoots).FirstOrDefault(r => !r.IsFolder) is { } firstU) _selectedUnstaged = [firstU];
            else if (FileListItem.Flatten(_stagedRoots).FirstOrDefault(r => !r.IsFolder) is { } firstS) _selectedStaged = [firstS];
        }
        SelectionRestored?.Invoke(this, EventArgs.Empty);
        UpdateSelectionState();
        PrefillMergeMessage();
        OnPropertyChanged(nameof(CommitButtonText));
        OnPropertyChanged(nameof(CanCommit));
        _ = LoadDiffAsync();
    }

    /// <summary>Load の後でビューに選択を戻してもらう。</summary>
    public event EventHandler? SelectionRestored;
    public IReadOnlyList<FileListItem> SelectedUnstaged => _selectedUnstaged;
    public IReadOnlyList<FileListItem> SelectedStaged => _selectedStaged;

    /// <summary>2 つの一覧の行（パス、フォルダか、status、展開）がすべて同じか。</summary>
    private static bool SameRows(List<FileListItem> a, List<FileListItem> b)
    {
        var x = AllRows(a).ToList();
        var y = AllRows(b).ToList();
        if (x.Count != y.Count) return false;
        for (var i = 0; i < x.Count; i++)
            if (x[i].Path != y[i].Path || x[i].IsFolder != y[i].IsFolder || x[i].Level != y[i].Level || x[i].Change != y[i].Change
                || x[i].IsExpanded != y[i].IsExpanded || !Equals(x[i].Entry, y[i].Entry)) return false;
        return true;
    }

    private static IEnumerable<FileListItem> AllRows(IEnumerable<FileListItem> roots)
        => roots.SelectMany(r => new[] { r }.Concat(AllRows(r.Children)));

    private void RebuildRows()
    {
        Replace(UnstagedRows, FileListItem.Flatten(_unstagedRoots));
        Replace(StagedRows, FileListItem.Flatten(_stagedRoots));
        static void Replace(ObservableCollection<FileListItem> target, IEnumerable<FileListItem> rows)
        {
            target.Clear();
            foreach (var r in rows) target.Add(r);
        }
    }

    [RelayCommand]
    private void ToggleFolder(FileListItem folder) => ToggleExpanded(folder);

    public void ToggleExpanded(FileListItem folder)
    {
        folder.IsExpanded = !folder.IsExpanded;
        RebuildRows();
        SelectionRestored?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>ビューの選択が変わった。片方を選ぶともう片方の選択は外す（diff は 1 つ）。</summary>
    public void SetSelection(bool staged, IReadOnlyList<FileListItem> items)
    {
        if (staged)
        {
            _selectedStaged = items;
            if (items.Count > 0) _selectedUnstaged = [];
        }
        else
        {
            _selectedUnstaged = items;
            if (items.Count > 0) _selectedStaged = [];
        }
        UpdateSelectionState();
        _ = LoadDiffAsync();
    }

    private void UpdateSelectionState()
    {
        HasUnstagedSelection = _selectedUnstaged.Count > 0;
        HasStagedSelection = _selectedStaged.Count > 0;
        OnPropertyChanged(nameof(CanUnstage));
    }

    // ------------------------------------------------------------------ diff

    /// <param name="keepIfSame">読み直しのとき：同じファイルで中身も同じなら表示を差し替えない（スクロール位置を保つ）。</param>
    private async Task LoadDiffAsync(bool keepIfSame = false)
    {
        _diffCts?.Cancel();
        var cts = _diffCts = new CancellationTokenSource();
        var staged = _selectedStaged.Count > 0;
        var file = (staged ? _selectedStaged : _selectedUnstaged).FirstOrDefault(f => !f.IsFolder);
        if (file?.Entry is not { } entry || Tree is not { } tree)
        {
            Diff = null; DiffEntry = null; DiffTitle = ""; Conflict = null;
            return;
        }
        try
        {
            // 競合中のファイルは diff の代わりに競合の案内
            if (!staged && entry.IsConflicted)
            {
                var (ours, theirs) = await tree.ReadConflictCommitsAsync(entry.Path, cts.Token);
                if (cts.IsCancellationRequested) return;
                var text = await tree.ReadWorkingFileAsync(entry.Path);
                var resolved = !text.Split('\n').Any(l => l.StartsWith("<<<<<<< ", StringComparison.Ordinal) || l.StartsWith(">>>>>>> ", StringComparison.Ordinal));
                if (cts.IsCancellationRequested) return;
                if (keepIfSame && Conflict is { } current && current.Path == entry.Path && current.IsResolved == resolved && Equals(DiffEntry, entry)) return;
                DiffEntry = entry;
                DiffIsStaged = false;
                DiffTitle = entry.Path;
                Diff = null;
                Conflict = new ConflictInfo(entry.Path, _repo.Notification.State?.Other ?? "theirs", _repo.References?.HeadBranch ?? "HEAD", ours, theirs) { IsResolved = resolved };
                return;
            }
            Conflict = null;
            var diff = staged && Amend
                ? await tree.ReadAmendDiffAsync(entry, FileDiffPanel.ContextLines, cts.Token)
                : await tree.ReadDiffAsync(entry, staged, FileDiffPanel.ContextLines, cts.Token);
            if (cts.IsCancellationRequested) return;
            if (keepIfSame && Equals(DiffEntry, entry) && DiffIsStaged == staged && FileDiff.SameContent(Diff, diff)) return;
            DiffEntry = entry;
            DiffIsStaged = staged;
            DiffTitle = entry.Path;
            Diff = diff;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error($"diff '{entry.Path}' failed: {ex.Message}");
            Diff = null;
        }
    }

    // ------------------------------------------------------------------ 競合（I01、I02）

    /// <summary>Resolve Using 'main' / 'bugfix/x'（ours = 今のブランチ側）。</summary>
    /// <summary>Resolve Using 'ours' / 'theirs'。複数のときは 1 つずつ同じコマンドを流す。</summary>
    public Task ResolveUsing(IReadOnlyList<StatusEntry> entries, bool ours) => RunAsync("Resolve", async t =>
    {
        foreach (var e in entries) await t.ResolveUsingAsync(e.Path, ours);
    });

    /// <summary>Merge Conflict Resolver で作った結果を保存して解決済みにする。</summary>
    public Task SaveResolved(string path, string content) => RunAsync("Resolve", t => t.SaveResolvedAsync(path, content));

    public Task<string> ReadConflictFile(string path) => Tree!.ReadWorkingFileAsync(path);

    /// <summary>
    /// External Merge（競合ファイルの右クリック > External Merge > ツール、競合の案内の "Merge in ..."）。
    /// index の stage 1 / 2 / 3（BASE / LOCAL = ours / REMOTE = theirs）を一時ファイルに書き、MERGED は作業ツリーのファイル。
    /// ツールが終わったら status を読み直す（解決したかは利用者が Stage で決める）。
    /// </summary>
    public async Task ExternalMergeAsync(Views.ExternalDiffTool tool, string path)
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GitKnife", "merge-" + Guid.NewGuid().ToString("N")[..8]);
        Core.Git.HostDirectory.CreateDirectory(dir);
        var name = System.IO.Path.GetFileName(path);
        async Task<string> Stage(int n, string prefix)
        {
            var file = System.IO.Path.Combine(dir, prefix + name);
            await Core.Git.HostFile.WriteAllTextAsync(file, await ReadRevisionFile($":{n}:{path}"));
            return file;
        }
        var @base = await Stage(1, "BASE_");
        var local = await Stage(2, "LOCAL_");
        var remote = await Stage(3, "REMOTE_");
        var merged = System.IO.Path.Combine(RepositoryPath, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        try { await tool.MergeAsync(remote, local, @base, merged); }
        catch (Exception ex) { Alerts.ShowGitError(tool.Name, ex.Message); }
        await _repo.RefreshStatusAsync();
    }

    /// <summary>
    /// Save as Patch…：選んだファイルの diff をファイルに書く（既定の名前は "basic-09-51-33"、種類 "Patches (*.patch)"、保存先は RecentPatchDirectory）。
    /// untracked は 1 つずつ（--no-index は 2 つのパスしか取れない）。
    /// </summary>
    public async Task SaveAsPatchAsync(IReadOnlyList<StatusEntry> entries, bool staged)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save patch as...",
            FileName = $"{_repo.Name}-{DateTime.Now:HH-mm-ss}",
            DefaultExt = ".patch",
            Filter = "Patches (*.patch)|*.patch",
            InitialDirectory = _repo.Settings.RecentPatchDirectory is { } d && Core.Git.HostDirectory.Exists(d) ? d : null,
        };
        if (dlg.ShowDialog(System.Windows.Application.Current.MainWindow) != true) return;
        _repo.Settings.RecentPatchDirectory = System.IO.Path.GetDirectoryName(dlg.FileName);
        try
        {
            var text = new System.Text.StringBuilder();
            var tracked = entries.Where(e => staged || e.WorkTree != FileChange.Untracked).Select(e => e.Path).ToList();
            if (tracked.Count > 0) text.Append((await _repo.RunGitAsync(Core.Git.Commands.GitArgs.DiffForPatch(tracked, staged, untracked: false))).StandardOutput);
            foreach (var u in entries.Where(e => !staged && e.WorkTree == FileChange.Untracked))
            {
                // --no-index は違いがあると exit 1 なので、失敗扱いにせず出力を使う
                try { text.Append((await _repo.RunGitAsync(Core.Git.Commands.GitArgs.DiffForPatch([u.Path], false, untracked: true))).StandardOutput); }
                catch (GitException ex) when (ex.Result.ExitCode == 1) { text.Append(ex.Result.StandardOutput); }
            }
            await Core.Git.HostFile.WriteAllTextAsync(dlg.FileName, text.ToString());
        }
        catch (GitException ex) { Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
        catch (System.IO.IOException ex) { Alerts.ShowGitError("Save as Patch", ex.Message); }
    }

    /// <summary>
    /// .gitignore（リポジトリの直下）の最後にパターンをそのまま足す（git コマンドは使わない）。
    /// 最後の行に改行が無ければ先に足す。
    /// </summary>
    public async Task IgnoreAsync(string pattern)
    {
        var path = System.IO.Path.Combine(RepositoryPath, ".gitignore");
        try
        {
            var text = Core.Git.HostFile.Exists(path) ? await Core.Git.HostFile.ReadAllTextAsync(path) : "";
            var prefix = text.Length > 0 && !text.EndsWith('\n') ? "\n" : "";
            await Core.Git.HostFile.AppendAllTextAsync(path, prefix + pattern + "\n");
        }
        catch (System.IO.IOException ex) { Alerts.ShowGitError("Ignore", ex.Message); }
        await _repo.RefreshStatusAsync();
    }

    /// <summary>`git show <spec>` の中身（":path" で index、"HEAD:path" で HEAD。無ければ空）。外部 diff の古い側に使う。</summary>
    public async Task<string> ReadRevisionFile(string spec)
    {
        try { return (await _repo.RunGitAsync(["show", spec])).StandardOutput; }
        catch (Core.Git.GitException) { return ""; }
    }

    // ------------------------------------------------------------------ ステージ操作（G03）

    /// <param name="job">Activity Manager にジョブとして残す（コミット）。</param>
    private async Task RunAsync(string what, Func<WorkingTree, Task> action, bool fullRefresh = false, bool job = false)
    {
        if (Tree is not { } tree || IsWorking) return;
        var activity = job ? _repo.StartJob(what) : null;
        using var scope = activity?.Enter();
        try
        {
            IsWorking = true;
            await action(tree);
            activity?.Finish(true, null);
        }
        catch (GitException ex)
        {
            activity?.Finish(false, ex.Result.StandardError.Trim().Split('\n')[0]);
            Log.Error($"{what} failed: {ex.Message}");
            Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError.Length > 0 ? ex.Result.StandardError : ex.Message);
        }
        catch (Exception ex)
        {
            activity?.Finish(false, ex.Message);
            Log.Error($"{what} failed: {ex}");
            Alerts.ShowGitError(what, ex.Message);
        }
        finally
        {
            IsWorking = false;
        }
        if (fullRefresh) await _repo.RefreshAsync(); else await _repo.RefreshStatusAsync();
    }

    [RelayCommand]
    public Task StageSelected() => Stage(_selectedUnstaged.SelectMany(r => r.Entries));
    [RelayCommand]
    public Task UnstageSelected() => Unstage(_selectedStaged.SelectMany(r => r.Entries));
    [RelayCommand]
    public Task StageAll() => Stage(_unstagedRoots.SelectMany(r => r.Entries));
    [RelayCommand]
    public Task UnstageAll() => Unstage(_stagedRoots.SelectMany(r => r.Entries));

    public Task Stage(IEnumerable<StatusEntry> entries)
    {
        var list = entries.ToList();
        return list.Count == 0 ? Task.CompletedTask : RunAsync("Stage", t => t.StageAsync(list.SelectMany(e => e.OriginalPath is null ? [e.Path] : new[] { e.Path, e.OriginalPath })));
    }

    public Task Unstage(IEnumerable<StatusEntry> entries)
    {
        var list = entries.ToList();
        return list.Count == 0 ? Task.CompletedTask : RunAsync("Unstage", t => t.UnstageAsync(list));
    }

    /// <summary>Discard changes...（確認ダイアログの後）。</summary>
    [RelayCommand]
    public Task DiscardSelected()
    {
        var list = _selectedUnstaged.SelectMany(r => r.Entries).ToList();
        if (list.Count == 0) return Task.CompletedTask;
        if (!Alerts.Confirm("Discard changes", "Do you want to discard all your changes in the selected files?", "Discard")) return Task.CompletedTask;
        return RunAsync("Discard", t => t.DiscardAsync(list));
    }

    /// <summary>
    /// diff の上で選んだ行（ハンク番号 → hunk.Lines の添字）を stage / unstage / discard する。
    /// 下のハンクから当てるので、上のハンクの行番号はずれない。
    /// </summary>
    public Task ApplyLines(LinesAction action, IReadOnlyDictionary<int, HashSet<int>> lines)
    {
        if (Diff is not { } diff || lines.Count == 0) return Task.CompletedTask;
        if (action == LinesAction.Discard && !Alerts.Confirm("Discard changes", "Do you want to discard the selected lines?", "Discard")) return Task.CompletedTask;
        var target = action switch { LinesAction.Stage => PatchTarget.Stage, LinesAction.Unstage => PatchTarget.Unstage, _ => PatchTarget.Discard };
        var patches = lines.OrderByDescending(kv => kv.Key)
            .Select(kv => PatchBuilder.Build(diff, diff.Hunks[kv.Key], kv.Value, target))
            .OfType<string>().ToList();
        return RunAsync($"{action} lines", async t =>
        {
            foreach (var p in patches) await t.ApplyPatchAsync(p, target);
        });
    }

    // ------------------------------------------------------------------ コミット（G05、G06）

    private string? _prefilledFor;

    /// <summary>
    /// merge / cherry-pick / revert の途中なら、件名と説明に .git/MERGE_MSG を入れる（"# Conflicts:" の行も残す。
    /// コミット時に git が # の行を消す）。同じ操作の間は 1 回だけ。
    /// </summary>
    private void PrefillMergeMessage()
    {
        var op = _repo.References?.Operation ?? Core.Git.Model.RepositoryOperation.None;
        if (op is not (Core.Git.Model.RepositoryOperation.Merge or Core.Git.Model.RepositoryOperation.CherryPick or Core.Git.Model.RepositoryOperation.Revert))
        {
            _prefilledFor = null;
            return;
        }
        if (Tree is not { } tree || Subject.Length > 0) return;
        var path = System.IO.Path.Combine(tree.GitDirectory, "MERGE_MSG");
        var key = op + (Core.Git.HostFile.Exists(path) ? Core.Git.HostFile.GetLastWriteTimeUtc(path).Ticks.ToString() : "");
        if (_prefilledFor == key || !Core.Git.HostFile.Exists(path)) return;
        _prefilledFor = key;
        var parts = Core.Git.HostFile.ReadAllText(path).Replace("\r\n", "\n").TrimEnd('\n').Split('\n', 2);
        Subject = parts[0];
        Description = parts.Length > 1 ? parts[1].Trim('\n') : "";
    }

    /// <summary>
    /// コミットのテンプレート（commit.template）：件名の欄にフォーカスが入ったとき、件名も説明も空なら `git config commit.template` のファイルを入れる
    /// （1 行目が件名、空行の後が説明）。
    /// </summary>
    public async Task ApplyCommitTemplateAsync()
    {
        if (Subject.Length > 0 || Description.Length > 0 || Tree is not { } tree) return;
        var text = await tree.ReadCommitTemplateAsync();
        if (string.IsNullOrEmpty(text) || Subject.Length > 0 || Description.Length > 0) return;
        var lines = text.Replace("\r", "").TrimEnd('\n').Split('\n');
        Subject = lines[0];
        Description = string.Join("\n", lines.Skip(1)).Trim('\n');
    }

    private (string subject, string description)? _amendInserted;
    private IReadOnlyList<StatusEntry>? _amendStaged;

    /// <summary>Amend 中の Staged を読み直し、変わっていたら一覧を作り直す。Unstage は押せない。</summary>
    private async Task RefreshAmendStagedAsync()
    {
        if (Tree is not { } tree) return;
        try
        {
            var list = await tree.ReadAmendStagedAsync();
            if (!Amend) return;
            var changed = _amendStaged is null || !_amendStaged.Select(e => (e.Path, e.Index)).SequenceEqual(list.Select(e => (e.Path, e.Index)));
            _amendStaged = list;
            if (changed && _repo.Status is { } status) Load(status);
        }
        catch (Core.Git.GitException ex) { Log.Warning($"amend staged failed: {ex.Message}"); }   // 最初のコミット（HEAD^ が無い）など
    }

    /// <summary>Unstage のボタン（Amend 中は押せない）。</summary>
    public bool CanUnstage => HasStagedSelection && !Amend;

    partial void OnAmendChanged(bool value)
    {
        OnPropertyChanged(nameof(CanUnstage));
        if (!value) _amendStaged = null;
        if (_repo.Status is { } st) Load(st, force: true);
        // Amend をオンにすると最後のコミットのメッセージを欄に入れる（件名が空のときだけ）。オフに戻したら、入れたままなら消す
        if (!value)
        {
            if (_amendInserted is { } ins && ins.subject == Subject && ins.description == Description) { Subject = ""; Description = ""; }
            _amendInserted = null;
            return;
        }
        if (Tree is not { } tree || Subject.Length > 0) return;
        _ = Task.Run(async () =>
        {
            var message = await tree.ReadLastCommitMessageAsync();
            var parts = message.Split('\n', 2);
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (!Amend || Subject.Length > 0) return;
                Subject = parts[0];
                Description = parts.Length > 1 ? parts[1].TrimStart('\n') : "";
                _amendInserted = (Subject, Description);
            });
        });
    }

    [RelayCommand]
    public Task Commit() => DoCommit(push: false);

    [RelayCommand]
    public Task CommitAndPush() => DoCommit(push: true);

    private Task DoCommit(bool push)
    {
        if (!CanCommit) return Task.CompletedTask;
        var message = WorkingTree.ComposeMessage(Subject, Description);
        var amend = Amend;
        return RunAsync(push ? "Commit and Push" : "Commit", async t =>
        {
            var signOff = _repo.CommonGitDirectory is { } common && Core.Settings.RepositoryConfig.Load(common).SignOff;
            await t.CommitAsync(message, amend, signOff);
            Subject = "";
            Description = "";
            Amend = false;
            if (push || _repo.Settings.PushAutomaticallyOnCommit) await _repo.PushCurrentBranchAsync();
        }, fullRefresh: true, job: true);
    }

    /// <summary>
    /// 最近のコミットメッセージ（RecentCommitMessagesDropDownButton）。HEAD から辿れるコミットを新しい順に出す
    /// （本文のあるものは "Print greeting..." のように件名に "..." を付ける）。
    /// </summary>
    public void LoadRecentMessages(IEnumerable<CommitRow> rows)
    {
        RecentMessages.Clear();
        foreach (var c in rows.Where(r => r.IsReachableFromHead && !r.Commit.IsStash).Select(r => r.Commit).Take(20)) RecentMessages.Add(c);
    }

    public void UseRecentMessage(CommitInfo commit)
    {
        Subject = commit.Subject;
        Description = commit.Body;
    }
}

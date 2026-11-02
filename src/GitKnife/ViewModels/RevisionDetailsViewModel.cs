using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GitKnife.Core.Diff;
using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;
using GitKnife.Core.Logging;
using GitKnife.Views;

namespace GitKnife.ViewModels;

/// <summary>Commit モードの変更ファイル 1 つ。展開すると diff を読む。</summary>
public sealed partial class CommitFileEntry(RevisionDetailsViewModel owner, FileListItem item) : ObservableObject
{
    public FileListItem Item { get; } = item;
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private FileDiff? _diff;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value && Diff is null) _ = owner.LoadEntryDiffAsync(this);
    }
}

/// <summary>
/// コミット詳細。
/// Commit：変更ファイルを展開できる一覧。Changes：ファイル一覧と diff。File Tree：その時点の全ファイルと中身。
/// </summary>
public sealed partial class RevisionDetailsViewModel(RepositoryViewModel repo, IRevisionDetailsHost? host = null) : ObservableObject
{
    /// <summary>表示するモード（Commit / Changes / File Tree）を持つ側。メインの詳細ペインはリポジトリ、Space のポップアップは自分で持つ。</summary>
    private readonly IRevisionDetailsHost _host = host ?? repo;
    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _diffCts;
    private IReadOnlyList<StatusEntry> _changes = [];
    private List<FileListItem> _changeRoots = [];
    private List<FileListItem> _treeRoots = [];
    private string? _treeLoadedFor;

    public ObservableCollection<CommitFileEntry> Entries { get; } = [];
    public ObservableCollection<FileListItem> ChangeRows { get; } = [];
    public ObservableCollection<FileListItem> TreeRows { get; } = [];

    [ObservableProperty] private CommitRow? _commit;
    [ObservableProperty] private FileListItem? _selectedChange;
    [ObservableProperty] private FileDiff? _diff;
    [ObservableProperty] private string _diffTitle = "";
    [ObservableProperty] private FileListItem? _selectedTreeFile;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private string _previewTitle = "";

    private RevisionReader? Reader => repo.RevisionReader;

    /// <summary>選んだコミットが変わった。変更ファイルを読み、表示中のモードの中身を用意する。</summary>
    /// <summary>
    /// 2 つ目のコミット（一覧で 2 つ選んだとき）。Commit が古い方、Other が新しい方（既定は古い → 新しい）。
    /// 比べている間は Changes モードだけ（Commit / File Tree は押せない）、最初はファイルを選ばない。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsComparing), nameof(IsNotComparing))] private CommitRow? _other;
    public bool IsComparing => Other is not null || CompareToLocal;
    public bool IsNotComparing => !IsComparing;
    /// <summary>Compare to Local Changes（2 行目が "Local Changes"）。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsComparing), nameof(IsNotComparing), nameof(CanSwap))] private bool _compareToLocal;
    public bool CanSwap => Other is not null;

    /// <summary>コミットと作業ツリーを比べる（`diff-index -z -M &lt;sha&gt;`）。Changes モードだけ、選んでいたファイルがあればそのまま。</summary>
    public async Task LoadCompareToLocalAsync(CommitRow row)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var keepPath = SelectedChange?.Path;
        Commit = row;
        Other = null;
        CompareToLocal = false;
        CompareToLocal = true;
        _host.DetailsMode = DetailsMode.Changes;
        Entries.Clear();
        ChangeRows.Clear();
        _changes = [];
        Diff = null; DiffTitle = "";
        if (Reader is not { } reader) return;
        try
        {
            var changes = await reader.ReadChangesToWorkTreeAsync(row.Commit.Sha, cts.Token);
            if (cts.IsCancellationRequested) return;
            _changes = changes;
            _changeRoots = BuildChangeRoots();
            RebuildChangeRows();
            SelectedChange = ChangeRows.FirstOrDefault(r => !r.IsFolder && r.Path == keepPath) ?? ChangeRows.FirstOrDefault(r => !r.IsFolder);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error($"compare {row.Commit.ShortSha} to local changes failed: {ex.Message}"); }
    }

    public async Task LoadCompareAsync(CommitRow older, CommitRow newer)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Commit = older;
        Other = newer;
        CompareToLocal = false;
        _host.DetailsMode = DetailsMode.Changes;
        Entries.Clear();
        ChangeRows.Clear();
        _changes = [];
        Diff = null; DiffTitle = "";
        SelectedChange = null;
        if (Reader is not { } reader) return;
        try
        {
            var changes = await reader.ReadChangesBetweenAsync(older.Commit.Sha, newer.Commit.Sha, cts.Token);
            if (cts.IsCancellationRequested) return;
            _changes = changes;
            _changeRoots = BuildChangeRoots();
            RebuildChangeRows();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error($"compare {older.Commit.ShortSha}..{newer.Commit.ShortSha} failed: {ex.Message}"); }
    }

    /// <summary>SwapRevisionsButton：比べる向きを入れ替える。</summary>
    [RelayCommand]
    private Task SwapRevisions() => Commit is { } a && Other is { } b ? LoadCompareAsync(b, a) : Task.CompletedTask;

    /// <summary>最初に選ぶファイル（別ウィンドウはポップアップで選んでいたファイルを引き継ぐ）。</summary>
    public string? PreferredChangePath { get; set; }

    public async Task LoadAsync(CommitRow? row)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        Commit = row;
        Other = null;
        var keepPath = SelectedChange?.Path ?? PreferredChangePath;
        Entries.Clear();
        ChangeRows.Clear();
        _changes = [];
        Diff = null; DiffTitle = "";
        if (row is null || Reader is not { } reader) return;
        try
        {
            var changes = await reader.ReadChangesAsync(row.Commit, cts.Token);
            if (cts.IsCancellationRequested) return;
            _changes = changes;
            // Commit の一覧は git の並び（大文字が先。"README.md" が "docs/todo.md" より前）
            foreach (var item in FileListItem.Build(changes, FileListMode.List, staged: true).OrderBy(i => i.Path, StringComparer.Ordinal))
                Entries.Add(new CommitFileEntry(this, item));
            _changeRoots = BuildChangeRoots();
            RebuildChangeRows();
            // 同じパスがあれば選び直す。無ければ最初のファイル
            SelectedChange = ChangeRows.FirstOrDefault(r => !r.IsFolder && r.Path == keepPath) ?? ChangeRows.FirstOrDefault(r => !r.IsFolder);
            if (_host.DetailsMode == DetailsMode.FileTree) await LoadTreeAsync();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error($"Load revision {row.Commit.ShortSha} failed: {ex.Message}"); }
    }

    /// <summary>
    /// Changes のファイル一覧の絞り込み（虫眼鏡で出す FilterTextBox。Local Changes と同じく大文字小文字を区別せず、パスの途中でも当たる）。
    /// </summary>
    [ObservableProperty] private string _changeFilter = "";

    partial void OnChangeFilterChanged(string value)
    {
        var keep = SelectedChange?.Path;
        _changeRoots = BuildChangeRoots();
        RebuildChangeRows();
        SelectedChange = ChangeRows.FirstOrDefault(r => !r.IsFolder && r.Path == keep) ?? ChangeRows.FirstOrDefault(r => !r.IsFolder);
    }

    private List<FileListItem> BuildChangeRoots()
    {
        var filter = ChangeFilter.Trim();
        var changes = filter.Length == 0 ? _changes : _changes.Where(e => e.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || e.OriginalPath?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true).ToList();
        return FileListItem.Build(changes, (FileListMode)repo.Settings.FileListMode, staged: true);
    }

    private void RebuildChangeRows()
    {
        ChangeRows.Clear();
        foreach (var r in FileListItem.Flatten(_changeRoots)) ChangeRows.Add(r);
    }

    /// <summary>フォルダ行の開閉（Gk.FileRowTemplate の矢印）。</summary>
    [RelayCommand]
    private void ToggleFolder(FileListItem folder)
    {
        if (TreeRows.Contains(folder)) ToggleTreeFolder(folder); else ToggleChangeFolder(folder);
    }

    public void ToggleChangeFolder(FileListItem folder)
    {
        folder.IsExpanded = !folder.IsExpanded;
        RebuildChangeRows();
    }

    partial void OnSelectedChangeChanged(FileListItem? value) => _ = LoadDiffAsync();

    /// <summary>Changes モードの diff を読み直す（見出しの設定を変えたときも）。</summary>
    public async Task LoadDiffAsync()
    {
        _diffCts?.Cancel();
        var cts = _diffCts = new CancellationTokenSource();
        if (Commit is not { } row || SelectedChange?.Entry is not { } entry || Reader is not { } reader) { Diff = null; DiffTitle = ""; return; }
        try
        {
            var diff = Other is { } other
                ? await reader.ReadDiffBetweenAsync(row.Commit.Sha, other.Commit.Sha, entry, FileDiffPanel.ContextLines, cts.Token)
                : CompareToLocal
                    ? await reader.ReadDiffToWorkTreeAsync(row.Commit.Sha, entry, FileDiffPanel.ContextLines, cts.Token)
                    : await reader.ReadDiffAsync(row.Commit, entry, FileDiffPanel.ContextLines, cts.Token);
            if (cts.IsCancellationRequested) return;
            DiffTitle = entry.Path;
            Diff = diff;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error($"diff {entry.Path} failed: {ex.Message}"); }
    }

    public async Task LoadEntryDiffAsync(CommitFileEntry e)
    {
        if (Commit is not { } row || e.Item.Entry is not { } entry || Reader is not { } reader) return;
        try { e.Diff = await reader.ReadDiffAsync(row.Commit, entry, FileDiffPanel.ContextLines); }
        catch (Exception ex) { Log.Error($"diff {entry.Path} failed: {ex.Message}"); }
    }

    /// <summary>Expand All / Collapse All（Commit モードの ExpandAllButton）。</summary>
    [RelayCommand]
    private void ToggleExpandAll()
    {
        var expand = Entries.Any(e => !e.IsExpanded);
        foreach (var e in Entries) e.IsExpanded = expand;
        OnPropertyChanged(nameof(ExpandAllText));
    }

    public string ExpandAllText => Entries.Count > 0 && Entries.All(e => e.IsExpanded) ? "Collapse All" : "Expand All";

    // ------------------------------------------------------------------ File Tree

    /// <summary>File Tree モードを開いたとき（またはそのモードでコミットが変わったとき）にファイル一覧を読む。</summary>
    public async Task LoadTreeAsync()
    {
        if (Commit is not { } row || Reader is not { } reader || _treeLoadedFor == row.Commit.Sha) return;
        try
        {
            var files = await reader.ReadFilesAsync(row.Commit.Sha);
            _treeLoadedFor = row.Commit.Sha;
            var changed = _changes.ToDictionary(c => c.Path, c => c.Index);
            var entries = files.Select(f => new StatusEntry(f, changed.GetValueOrDefault(f, FileChange.Unmodified), FileChange.Unmodified));
            _treeRoots = FileListItem.Build(entries, FileListMode.Tree, staged: true);
            // File Tree は最初は畳んだ状態
            foreach (var f in FileListItem.Flatten(_treeRoots).Where(f => f.IsFolder)) f.IsExpanded = false;
            RebuildTreeRows();
        }
        catch (Exception ex) { Log.Error($"ls-tree failed: {ex.Message}"); }
    }

    public RepositoryViewModel Repository => repo;

    /// <summary>Show in File Tree：File Tree モードにして、そのファイルまでフォルダを開いて選ぶ。</summary>
    public async Task ShowInFileTreeAsync(string path)
    {
        _host.DetailsMode = DetailsMode.FileTree;
        await LoadTreeAsync();
        FileListItem? found = null;
        bool Open(IEnumerable<FileListItem> items)
        {
            foreach (var i in items)
            {
                if (!i.IsFolder && i.Path == path) { found = i; return true; }
                if (i.IsFolder && path.StartsWith(i.Path + "/", StringComparison.Ordinal) && Open(i.Children)) { i.IsExpanded = true; return true; }
            }
            return false;
        }
        if (!Open(_treeRoots)) return;
        RebuildTreeRows();
        SelectedTreeFile = found;
    }

    private void RebuildTreeRows()
    {
        TreeRows.Clear();
        foreach (var r in FileListItem.Flatten(_treeRoots)) TreeRows.Add(r);
    }

    public void ToggleTreeFolder(FileListItem folder)
    {
        folder.IsExpanded = !folder.IsExpanded;
        RebuildTreeRows();
    }

    partial void OnSelectedTreeFileChanged(FileListItem? value)
    {
        // 閉じたフォルダの行をクリックすると開く
        if (value is { IsFolder: true, IsExpanded: false })
        {
            ToggleTreeFolder(value);
            SelectedTreeFile = value;
            return;
        }
        _ = LoadPreviewAsync(value);
    }

    private async Task LoadPreviewAsync(FileListItem? item)
    {
        if (item is null || item.IsFolder || Commit is not { } row || Reader is not { } reader) { PreviewText = null; PreviewTitle = ""; return; }
        var text = await reader.ReadFileTextAsync(row.Commit.Sha, item.Path);
        PreviewTitle = item.Path;
        PreviewText = text ?? "(binary file)";
    }
}

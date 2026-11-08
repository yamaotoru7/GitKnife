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

    /// <summary>Commit モードに出す変更ファイル（多いときは先頭の CommitEntryPageSize 件から。残りは Show more で足す）。</summary>
    public BulkObservableCollection<CommitFileEntry> Entries { get; } = [];
    public BulkObservableCollection<FileListItem> ChangeRows { get; } = [];
    public BulkObservableCollection<FileListItem> TreeRows { get; } = [];

    /// <summary>Commit モードで一度に並べるファイルの数。1 万件を超えるコミットで行を全部作ると UI が止まる。</summary>
    public const int CommitEntryPageSize = 300;
    private List<CommitFileEntry> _allEntries = [];

    /// <summary>Commit モードでまだ並べていないファイルの数。</summary>
    public int RemainingEntryCount => _allEntries.Count - Entries.Count;
    public bool HasRemainingEntries => RemainingEntryCount > 0;
    public string ShowMoreEntriesText => $"Show {Math.Min(CommitEntryPageSize, RemainingEntryCount):N0} more files ({RemainingEntryCount:N0} remaining)";

    /// <summary>読み込み中（時間がかかったときだけ詳細ペインに出す）。</summary>
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _loadingText = "";

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
        IsLoading = false;
        var keepPath = SelectedChange?.Path;
        Commit = row;
        Other = null;
        CompareToLocal = false;
        CompareToLocal = true;
        _host.DetailsMode = DetailsMode.Changes;
        ClearEntries();
        ChangeRows.Clear();
        _changes = [];
        Diff = null; DiffTitle = "";
        if (Reader is not { } reader) return;
        try
        {
            using var busy = BeginLoading(cts);
            var changes = await reader.ReadChangesToWorkTreeAsync(row.Commit.Sha, cts.Token);
            if (cts.IsCancellationRequested) return;
            busy.Report(changes.Count);
            _changes = changes;
            await BuildChangeRowsAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
            SelectedChange = ChangeRows.FirstOrDefault(r => !r.IsFolder && r.Path == keepPath) ?? ChangeRows.FirstOrDefault(r => !r.IsFolder);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error($"compare {row.Commit.ShortSha} to local changes failed: {ex.Message}"); }
    }

    public async Task LoadCompareAsync(CommitRow older, CommitRow newer)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsLoading = false;
        Commit = older;
        Other = newer;
        CompareToLocal = false;
        _host.DetailsMode = DetailsMode.Changes;
        ClearEntries();
        ChangeRows.Clear();
        _changes = [];
        Diff = null; DiffTitle = "";
        SelectedChange = null;
        if (Reader is not { } reader) return;
        try
        {
            using var busy = BeginLoading(cts);
            var changes = await reader.ReadChangesBetweenAsync(older.Commit.Sha, newer.Commit.Sha, cts.Token);
            if (cts.IsCancellationRequested) return;
            busy.Report(changes.Count);
            _changes = changes;
            await BuildChangeRowsAsync(cts.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Log.Error($"compare {older.Commit.ShortSha}..{newer.Commit.ShortSha} failed: {ex.Message}"); }
    }

    /// <summary>SwapRevisionsButton：比べる向きを入れ替える。</summary>
    [RelayCommand]
    private Task SwapRevisions() => Commit is { } a && Other is { } b ? LoadCompareAsync(b, a) : Task.CompletedTask;

    /// <summary>最初に選ぶファイル（別ウィンドウはポップアップで選んでいたファイルを引き継ぐ）。</summary>
    public string? PreferredChangePath { get; set; }

    /// <summary>
    /// 一覧を読み直して同じコミットの行が作り直されたとき（自動 fetch など）：行だけ差し替え、ファイル一覧と diff は読み直さない
    /// （表示のちらつきと、diff のスクロール位置が先頭に戻るのを避ける。コミットの中身は変わらない）。差し替えたら true。
    /// </summary>
    public bool ReplaceRow(CommitRow? row)
    {
        if (row is null || Commit is not { } current || IsComparing || current.Commit.Sha != row.Commit.Sha) return false;
        Commit = row;
        return true;
    }

    public async Task LoadAsync(CommitRow? row)
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        IsLoading = false;
        Commit = row;
        Other = null;
        var keepPath = SelectedChange?.Path ?? PreferredChangePath;
        ClearEntries();
        ChangeRows.Clear();
        _changes = [];
        Diff = null; DiffTitle = "";
        if (row is null || Reader is not { } reader) return;
        try
        {
            using var busy = BeginLoading(cts);
            var changes = await reader.ReadChangesAsync(row.Commit, cts.Token);
            if (cts.IsCancellationRequested) return;
            busy.Report(changes.Count);
            _changes = changes;
            // Commit の一覧は git の並び（大文字が先。"README.md" が "docs/todo.md" より前）。行は UI の外で作る
            var entries = await Task.Run(() => FileListItem.Build(changes, FileListMode.List, staged: true)
                .OrderBy(i => i.Path, StringComparer.Ordinal).Select(i => new CommitFileEntry(this, i)).ToList(), cts.Token);
            if (cts.IsCancellationRequested) return;
            _allEntries = entries;
            Entries.ReplaceAll(entries.Take(CommitEntryPageSize));
            NotifyEntryCounts();
            await BuildChangeRowsAsync(cts.Token);
            if (cts.IsCancellationRequested) return;
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
        _changeRoots = BuildChangeRoots(_changes, value, (FileListMode)repo.Settings.FileListMode);
        RebuildChangeRows();
        SelectedChange = ChangeRows.FirstOrDefault(r => !r.IsFolder && r.Path == keep) ?? ChangeRows.FirstOrDefault(r => !r.IsFolder);
    }

    private static List<FileListItem> BuildChangeRoots(IReadOnlyList<StatusEntry> all, string filterText, FileListMode mode)
    {
        var filter = filterText.Trim();
        var changes = filter.Length == 0 ? all : all.Where(e => e.Path.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || e.OriginalPath?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true).ToList();
        return FileListItem.Build(changes, mode, staged: true);
    }

    /// <summary>Changes の行を UI の外で組み立ててから、一覧をまとめて入れ替える。</summary>
    private async Task BuildChangeRowsAsync(CancellationToken ct)
    {
        var (all, filter, mode) = (_changes, ChangeFilter, (FileListMode)repo.Settings.FileListMode);
        var (roots, rows) = await Task.Run(() =>
        {
            var r = BuildChangeRoots(all, filter, mode);
            return (r, FileListItem.Flatten(r).ToList());
        }, ct);
        if (ct.IsCancellationRequested) return;
        _changeRoots = roots;
        ChangeRows.ReplaceAll(rows);
    }

    private void RebuildChangeRows() => ChangeRows.ReplaceAll(FileListItem.Flatten(_changeRoots));

    private void ClearEntries()
    {
        _allEntries = [];
        Entries.Clear();
        NotifyEntryCounts();
    }

    private void NotifyEntryCounts()
    {
        OnPropertyChanged(nameof(RemainingEntryCount));
        OnPropertyChanged(nameof(HasRemainingEntries));
        OnPropertyChanged(nameof(ShowMoreEntriesText));
        OnPropertyChanged(nameof(ExpandAllText));
    }

    /// <summary>Commit モードの残りのファイルを次の CommitEntryPageSize 件だけ並べる。</summary>
    [RelayCommand]
    private void ShowMoreEntries()
    {
        Entries.AddRange(_allEntries.Skip(Entries.Count).Take(CommitEntryPageSize));
        NotifyEntryCounts();
    }

    /// <summary>読み込みの間の表示を始める。using で抜けたら消す。</summary>
    private LoadingScope BeginLoading(CancellationTokenSource cts) => new(this, cts);

    /// <summary>
    /// 読み込み中の表示。速く終わるときにちらつかないよう、少し待ってから IsLoading を立てる。
    /// 次の読み込みが始まっていれば（_cts が替わっていれば）表示に触らない。
    /// </summary>
    private sealed class LoadingScope : IDisposable
    {
        private const int DelayMs = 150;
        private readonly RevisionDetailsViewModel _owner;
        private readonly CancellationTokenSource _cts;
        private bool _done;

        public LoadingScope(RevisionDetailsViewModel owner, CancellationTokenSource cts)
        {
            _owner = owner;
            _cts = cts;
            owner.LoadingText = "Loading changes...";
            _ = ShowLaterAsync();
        }

        private async Task ShowLaterAsync()
        {
            await Task.Delay(DelayMs);
            if (!_done && _owner._cts == _cts) _owner.IsLoading = true;
        }

        /// <summary>ファイルの数が分かった（行を組み立てている間に出す）。</summary>
        public void Report(int fileCount)
        {
            if (_owner._cts == _cts) _owner.LoadingText = $"Preparing {fileCount:N0} files...";
        }

        public void Dispose()
        {
            _done = true;
            if (_owner._cts == _cts) _owner.IsLoading = false;
        }
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
            if (Commit != row) return;
            var changes = _changes;
            // 行の組み立ては UI の外で（全ファイルなので数万になることがある）
            var roots = await Task.Run(() =>
            {
                var changed = new Dictionary<string, FileChange>(StringComparer.Ordinal);
                foreach (var c in changes) changed[c.Path] = c.Index;
                var entries = files.Select(f => new StatusEntry(f, changed.GetValueOrDefault(f, FileChange.Unmodified), FileChange.Unmodified));
                var r = FileListItem.Build(entries, FileListMode.Tree, staged: true);
                // File Tree は最初はすべてのフォルダを畳んだ状態（Flatten は開いたフォルダにしか入らないので、Children を全部たどる）
                Collapse(r);
                return r;

                static void Collapse(List<FileListItem> items)
                {
                    foreach (var f in items.Where(f => f.IsFolder)) { f.IsExpanded = false; Collapse(f.Children); }
                }
            });
            if (Commit != row) return;
            _treeLoadedFor = row.Commit.Sha;
            _treeRoots = roots;
            SelectedTreeFile = null;   // 前のコミットの行は引き継がない
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

    /// <summary>
    /// 行を並べ直す。選んでいた行はそのまま選び、閉じたフォルダの中に隠れたらそのフォルダを選ぶ
    /// （並べ直すと ListView が選択を外すので、その間の選択の変化は無視する）。
    /// </summary>
    private void RebuildTreeRows()
    {
        var selected = SelectedTreeFile;
        _rebuildingTree = true;
        try { TreeRows.ReplaceAll(FileListItem.Flatten(_treeRoots)); }
        finally { _rebuildingTree = false; }
        if (selected is null) return;
        var visible = TreeRows.Contains(selected) ? selected : TreeRows.Where(r => r.IsFolder && selected.Path.StartsWith(r.Path + "/", StringComparison.Ordinal)).FirstOrDefault(r => !r.IsExpanded);
        SelectedTreeFile = visible;
    }
    private bool _rebuildingTree;

    public void ToggleTreeFolder(FileListItem folder)
    {
        folder.IsExpanded = !folder.IsExpanded;
        RebuildTreeRows();
    }

    /// <summary>
    /// File Tree のキー操作（エクスプローラーと同じ）。→：閉じたフォルダは 1 階層だけ開き、開いたフォルダは最初の子へ。
    /// ←：開いたフォルダは閉じ、それ以外は親のフォルダへ。動いたら true。
    /// </summary>
    public bool TreeKey(bool right)
    {
        if (SelectedTreeFile is not { } item) return false;
        if (right)
        {
            if (!item.IsFolder) return false;
            if (!item.IsExpanded) { ToggleTreeFolder(item); return true; }
            if (item.Children.Count == 0) return false;
            SelectedTreeFile = item.Children[0];
            return true;
        }
        if (item is { IsFolder: true, IsExpanded: true }) { ToggleTreeFolder(item); return true; }
        var slash = item.Path.LastIndexOf('/');
        if (slash < 0) return false;
        var parentPath = item.Path[..slash];
        if (TreeRows.FirstOrDefault(r => r.IsFolder && r.Path == parentPath) is not { } parent) return false;
        SelectedTreeFile = parent;
        return true;
    }

    partial void OnSelectedTreeFileChanged(FileListItem? value)
    {
        if (_rebuildingTree) return;
        // フォルダは選んだだけでは開かない（矢印か → で開く）
        _ = LoadPreviewAsync(value);
    }

    private FileListItem? _previewFor;

    private async Task LoadPreviewAsync(FileListItem? item)
    {
        if (item is not null && item == _previewFor) return;
        _previewFor = item;
        if (item is null || item.IsFolder || Commit is not { } row || Reader is not { } reader) { PreviewText = null; PreviewTitle = ""; return; }
        var text = await reader.ReadFileTextAsync(row.Commit.Sha, item.Path);
        PreviewTitle = item.Path;
        PreviewText = text ?? "(binary file)";
    }
}

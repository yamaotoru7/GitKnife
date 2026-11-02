using System.Collections.ObjectModel;
using System.IO;
using GitKnife.Core.Logging;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;
using GitKnife.Core.Git.Model;

namespace GitKnife.ViewModels;

/// <summary>
/// サイドバーの Search タブ。"Search Commits"、検索欄（左に最近の検索、右に消すボタン）、
/// 種類と範囲のボタン（"Commit Message ▾"）、"3 results"、結果（コミット。Diff Content / Path は一致したファイルも下に）。
/// Enter で検索し（種類や範囲を変えただけでは検索しない）、config.toml の recent_revision_search_queries の先頭に入れる。
/// </summary>
public sealed partial class SearchViewModel(RepositoryViewModel repo) : ObservableObject
{
    private const int MaxRecent = 10;
    private CancellationTokenSource? _cts;
    private IReadOnlyList<RevisionSearchResult> _results = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TypeLabel), nameof(Placeholder))] private RevisionSearchType _type = RevisionSearchType.Message;
    /// <summary>コミット一覧でも色を付ける検索語（Commit Message の検索のとき。一覧の件名の一致もオレンジにする）。</summary>
    [ObservableProperty] private string _highlightTerm = "";
    [ObservableProperty] private RevisionSearchScope _scope = RevisionSearchScope.Repository;
    [ObservableProperty] private string _resultText = "";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private SearchRow? _selectedRow;

    public ObservableCollection<SearchRow> Rows { get; } = [];

    /// <summary>SearchDropdownButton の文字。</summary>
    public string TypeLabel => TypeName(Type);
    /// <summary>空の検索欄の説明文（Commit Message なら "Commit message"）。</summary>
    public string Placeholder => TypeLabel.Length > 1 ? TypeLabel[..1] + TypeLabel[1..].ToLowerInvariant() : TypeLabel;

    public static string TypeName(RevisionSearchType t) => t switch
    {
        RevisionSearchType.Author => "Author",
        RevisionSearchType.DiffPath => "Path",
        RevisionSearchType.DiffContent => "Diff Content",
        _ => "Commit Message",
    };

    /// <summary>最近の検索のメニューの短い名前（"Message"、"Author"、"Path"、"Diff"）。</summary>
    public static string RecentName(RevisionSearchType t) => t switch
    {
        RevisionSearchType.Author => "Author",
        RevisionSearchType.DiffPath => "Path",
        RevisionSearchType.DiffContent => "Diff",
        _ => "Message",
    };

    public IReadOnlyList<RevisionSearchQuery> Recent
        => repo.CommonGitDirectory is { } dir ? Core.Settings.RepositoryConfig.Load(dir).RecentRevisionSearchQueries : [];

    partial void OnSearchTextChanged(string value)
    {
        if (value.Length > 0) return;
        _cts?.Cancel();
        _results = [];
        Rows.Clear();
        ResultText = "";
        HighlightTerm = "";
    }

    partial void OnSelectedRowChanged(SearchRow? value)
    {
        if (value is not null) repo.NavigateToCommit(value.Sha);
    }

    public void ApplyRecent(RevisionSearchQuery q)
    {
        SearchText = q.SearchString;
        Type = q.Type;
        Scope = q.Scope;
        _ = RunAsync();
    }

    public void ClearRecents() => SaveRecent(_ => []);

    public async Task RunAsync()
    {
        var text = SearchText.Trim();
        if (text.Length == 0 || repo.Git is not { } git) return;
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var q = new RevisionSearchQuery(Type, Scope, text);
        SaveRecent(list => [q, .. list.Where(x => x != q)]);
        IsBusy = true;
        try
        {
            var results = await RevisionSearch.RunAsync(git, repo.Path, q, cts.Token);
            if (cts.IsCancellationRequested) return;
            _results = results;
            _term = q.Type is RevisionSearchType.Message ? text : "";
            HighlightTerm = _term;
            ResultText = results.Count > 0 ? $"{results.Count} results" : "";   // 0 件のときは何も出さない
            Rebuild();
            SelectedRow = Rows.FirstOrDefault();   // 最初の結果を選び、一覧もそこへ動く
        }
        catch (OperationCanceledException) { }
        finally { if (_cts == cts) IsBusy = false; }
    }

    private void SaveRecent(Func<IReadOnlyList<RevisionSearchQuery>, IReadOnlyList<RevisionSearchQuery>> change)
    {
        if (repo.CommonGitDirectory is not { } dir) return;
        var c = Core.Settings.RepositoryConfig.Load(dir);
        c.EnsureDefaults();
        c.RecentRevisionSearchQueries = change(c.RecentRevisionSearchQueries).Take(MaxRecent).ToList();
        try { c.Save(); } catch (IOException ex) { Log.Warning($"Saving repository config failed: {ex.Message}"); }
    }

    /// <summary>コミットの行の開閉（ファイルの行を出す・隠す）。</summary>
    public void Toggle(SearchRow row)
    {
        row.IsExpanded = !row.IsExpanded;
        Rebuild();
    }

    private readonly HashSet<string> _collapsed = [];
    /// <summary>結果の件名で色を付ける文字（Commit Message のときだけ）。</summary>
    private string _term = "";

    private void Rebuild()
    {
        foreach (var r in Rows.Where(r => !r.IsFile && !r.IsExpanded)) _collapsed.Add(r.Sha);
        foreach (var r in Rows.Where(r => !r.IsFile && r.IsExpanded)) _collapsed.Remove(r.Sha);
        Rows.Clear();
        foreach (var r in _results)
        {
            var row = new SearchRow(r, null) { IsExpanded = !_collapsed.Contains(r.Sha), HighlightTerm = _term };
            Rows.Add(row);
            if (row.IsExpanded) foreach (var f in r.Files) Rows.Add(new SearchRow(r, f));
        }
    }
}

/// <summary>検索結果の 1 行（コミットか、その下のファイル）。</summary>
public sealed partial class SearchRow(RevisionSearchResult result, RevisionSearchFile? file) : ObservableObject
{
    public RevisionSearchResult Result => result;
    public RevisionSearchFile? File => file;
    public bool IsFile => file is not null;
    public bool HasFiles => file is null && result.Files.Count > 0;
    public string Sha => result.Sha;
    public string Subject => result.Subject;
    public string AuthorName => result.AuthorName;
    public string AuthorEmail => result.AuthorEmail;
    public string DateText => result.AuthorTime.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
    public string FilePath => file?.Path ?? "";
    public FileChange Change => file?.Change ?? FileChange.Modified;
    /// <summary>状態の文字（Gk.ChangeBadge の色分けに使う）。</summary>
    public string ChangeLetter => Change switch
    {
        FileChange.Added or FileChange.Copied => "+",
        FileChange.Deleted => "−",
        FileChange.Renamed => "R",
        FileChange.TypeChanged => "T",
        _ => "M",
    };
    [ObservableProperty] private bool _isExpanded = true;
    public string HighlightTerm { get; init; } = "";
}

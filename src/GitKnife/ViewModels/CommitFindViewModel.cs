using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;

namespace GitKnife.ViewModels;

/// <summary>
/// コミット一覧の Ctrl+F の検索パネル。
/// 打つたびに読み込んだ一覧の中を探して今の行から下の一致を選ぶ。Enter で次、Shift+Enter で前（端では止まる）、Esc で閉じる。
/// 一致した文字は一覧で色を付け、一致した行は右端に印を出す。
/// </summary>
public sealed partial class CommitFindViewModel(RepositoryViewModel repo) : ObservableObject
{
    private List<int> _matches = [];

    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string _query = "";
    /// <summary>"4 matches"（何も打っていないときは空）。</summary>
    [ObservableProperty] private string _matchesText = "";
    /// <summary>一覧で色を付ける文字（閉じているときは空）。</summary>
    [ObservableProperty] private string _term = "";

    /// <summary>一致した行の番号（昇順）。</summary>
    public IReadOnlyList<int> Matches => _matches;

    /// <summary>一致が変わった（右端の印を描き直す）。</summary>
    public event Action? MatchesChanged;

    public void Open()
    {
        IsOpen = true;
        Refresh(select: false);
    }

    public void Close()
    {
        IsOpen = false;
        Refresh(select: false);
    }

    partial void OnQueryChanged(string value) => Refresh(select: true);

    public void Next(bool forward) => Select(CommitListFind.Step(_matches, CurrentIndex, forward));

    /// <summary>一覧を読み直したあと（開いていれば一致を数え直す。選択は動かさない）。</summary>
    public void Refresh(bool select)
    {
        var q = IsOpen ? Query : "";
        _matches = q.Length == 0 ? [] : repo.Commits
            .Select((row, i) => (row, i))
            .Where(x => CommitListFind.Matches(x.row.Commit, x.row.Labels.Select(l => l.Text), q))
            .Select(x => x.i)
            .ToList();
        MatchesText = q.Length == 0 ? "" : CommitListFind.CountText(_matches.Count);
        Term = q;
        MatchesChanged?.Invoke();
        if (select) Select(CommitListFind.Start(_matches, CurrentIndex));
    }

    private int CurrentIndex => repo.SelectedCommit is { } s ? Math.Max(0, repo.Commits.IndexOf(s)) : 0;

    private void Select(int index)
    {
        if (index >= 0 && index < repo.Commits.Count) repo.SelectedCommit = repo.Commits[index];
    }
}

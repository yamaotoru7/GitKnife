using CommunityToolkit.Mvvm.ComponentModel;

namespace GitKnife.ViewModels;

/// <summary>コミット詳細（RevisionDetailsView）を出す側。メインの詳細ペイン（RepositoryViewModel）、Space のポップアップ、別ウィンドウ。</summary>
public interface IRevisionDetailsHost
{
    RepositoryViewModel Repository { get; }
    RevisionDetailsViewModel Details { get; }
    DetailsMode DetailsMode { get; set; }
    CommitRow? SelectedCommit { get; }
    /// <summary>PARENTS のリンクを押した（一覧でそのコミットを選ぶ）。</summary>
    void NavigateToCommit(string sha);
}

/// <summary>
/// 1 つのコミットを出し続けるコミット詳細。Space のポップアップと別ウィンドウ。
/// 一覧の選択は追わない。モードは窓ごと（ポップアップは毎回 Commit から。別ウィンドウはポップアップのモードと選んでいたファイルを引き継ぐ）。
/// </summary>
public sealed partial class RevisionWindowViewModel : ObservableObject, IRevisionDetailsHost
{
    public RevisionWindowViewModel(RepositoryViewModel repo, CommitRow row, DetailsMode mode = DetailsMode.Commit, string? selectedPath = null)
    {
        Repository = repo;
        SelectedCommit = row;
        Details = new RevisionDetailsViewModel(repo, this) { PreferredChangePath = selectedPath };
        _detailsMode = mode;
        _ = Load(row);
    }

    private async Task Load(CommitRow row)
    {
        await Details.LoadAsync(row);
        if (DetailsMode == DetailsMode.FileTree) await Details.LoadTreeAsync();
    }

    public RepositoryViewModel Repository { get; }
    public RevisionDetailsViewModel Details { get; }
    public CommitRow? SelectedCommit { get; }
    [ObservableProperty] private DetailsMode _detailsMode;

    /// <summary>窓の題名 "7cabd6e Add changelog"。</summary>
    public string Title => SelectedCommit is { } r ? $"{r.Commit.ShortSha} {r.Commit.Subject}" : "";

    partial void OnDetailsModeChanged(DetailsMode value)
    {
        if (value == DetailsMode.FileTree) _ = Details.LoadTreeAsync();
    }

    public void NavigateToCommit(string sha) => Repository.NavigateToCommit(sha);
}

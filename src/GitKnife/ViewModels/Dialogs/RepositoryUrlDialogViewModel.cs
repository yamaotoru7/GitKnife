using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// 更新とバグ報告の送り先のリポジトリ（File > Check for Updates... で、ダウンロード元が分からなかったとき。Preferences > Updates の Change... から）。
/// 入れた URL は設定の Update と BugReport の両方に入る（AppSettings.SetRepository）。
/// </summary>
public sealed partial class RepositoryUrlDialogViewModel : DialogViewModel
{
    private readonly bool _changing;

    /// <param name="changing">Preferences から変えるとき（検出できなかったときの文言を出さない）。</param>
    public RepositoryUrlDialogViewModel(bool changing = false)
    {
        _changing = changing;
        var s = App.Settings.Update;
        _repositoryUrl = Distribution.RepositoryUrl(s.ServerUrl, s.Repository);
    }

    public override string Title => "GitKnife Repository";
    public override string Subtitle => _changing ? "The repository GitKnife checks for updates." : "Where did you download GitKnife from? It could not be detected.";
    public override string SubmitText => _changing ? "Save" : "Continue";
    public override double DialogWidth => 560;

    public string NoteText => _changing
        ? "The repository is used to check for updates and to report bugs."
        : "The repository is used to check for updates and to report bugs. It is asked only once.";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _repositoryUrl;
    public override bool CanSubmit => Distribution.ParseRepositoryUrl(RepositoryUrl) is not null;

    public override Task SubmitAsync(CancellationToken ct)
    {
        var (server, repo) = Distribution.ParseRepositoryUrl(RepositoryUrl)!.Value;
        App.Settings.SetRepository(server, repo);
        App.SaveSettings();
        return Task.CompletedTask;
    }
}

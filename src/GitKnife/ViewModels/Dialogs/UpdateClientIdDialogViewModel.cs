using CommunityToolkit.Mvvm.ComponentModel;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// 更新のサーバー（Forgejo）がサインインを求めたが、OAuth2 の Client ID が無いとき（File > Check for Updates... から）。
/// ここでは保存しない。続けてブラウザでサインインし、できたときに設定の Update.OAuthClientId に残す（<see cref="Updater"/>）。
/// </summary>
public sealed partial class UpdateClientIdDialogViewModel : DialogViewModel
{
    private readonly string _serverUrl;

    /// <param name="initial">前にサインインできなかった値（入れ直しやすいように最初から入れておく）。</param>
    public UpdateClientIdDialogViewModel(string serverUrl, string? initial = null)
    {
        _serverUrl = serverUrl;
        ClientId = initial ?? "";
    }

    public override string Title => "Sign In to Update Server";
    public override string Subtitle => $"{_serverUrl.Trim().TrimEnd('/')} requires sign-in to read the releases.";
    public override string SubmitText => "Sign In";
    public override double DialogWidth => 560;

    public string NoteText => "Enter the Client ID of the OAuth2 application registered for GitKnife on this server (ask the server administrator). It is asked only once.";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _clientId = "";
    public override bool CanSubmit => ClientId.Trim().Length > 0;

    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

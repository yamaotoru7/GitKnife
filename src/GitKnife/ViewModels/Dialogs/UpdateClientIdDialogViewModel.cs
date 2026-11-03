using CommunityToolkit.Mvvm.ComponentModel;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// 更新のサーバー（Forgejo）がサインインを求めたが、OAuth2 の Client ID が無いとき（File > Check for Updates... から）。
/// 入れた ID は設定の Update.OAuthClientId に残し、続けてブラウザでサインインする。
/// </summary>
public sealed partial class UpdateClientIdDialogViewModel(string serverUrl) : DialogViewModel
{
    public override string Title => "Sign In to Update Server";
    public override string Subtitle => $"{serverUrl.Trim().TrimEnd('/')} requires sign-in to read the releases.";
    public override string SubmitText => "Sign In";
    public override double DialogWidth => 560;

    public string NoteText => "Enter the Client ID of the OAuth2 application registered for GitKnife on this server (ask the server administrator). It is asked only once.";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _clientId = "";
    public override bool CanSubmit => ClientId.Trim().Length > 0;

    public override Task SubmitAsync(CancellationToken ct)
    {
        App.Settings.Update.OAuthClientId = ClientId.Trim();
        App.SaveSettings();
        return Task.CompletedTask;
    }
}

using System.IO;
using System.Net.Http;
using GitKnife.Core;
using GitKnife.Core.Logging;
using GitKnife.Core.Update;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// "Update Available"（File > Check for Updates... と起動時の確認）。
/// リリースノートを見せ、"Update and Restart" でインストーラを落として照合する。閉じたあと Updater がインストーラを起動して GitKnife を終える。
/// 落とせなかったときはダイアログを閉じずにフッターに理由を出す。
/// </summary>
public sealed class UpdateDialogViewModel(UpdateClient client, ReleaseInfo release, string notes) : DialogViewModel
{
    public override string Title => "Update Available";
    public override string Subtitle => $"GitKnife {release.Version} is available. You have {ProductInfo.Version}.";
    public override string SubmitText => "Update and Restart";
    public override string CancelText => "Later";
    public override double DialogWidth => 590;

    /// <summary>今の版から最新までのリリースノート（Markdown。MarkdownView が表示する）。</summary>
    public string Notes => notes.Trim().Length > 0 ? notes.Trim() : "(No release notes)";

    /// <summary>落として照合したインストーラ。Submit が成功したときだけ入る。</summary>
    public string? InstallerPath { get; private set; }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var asset = release.Installer ?? throw new InvalidOperationException("The release has no installer.");
        try
        {
            StatusMessage = $"Downloading {asset.Name}...";
            var progress = new Progress<double>(p => StatusMessage = $"Downloading {asset.Name}... {p:P0}");
            var dir = Path.Combine(Path.GetTempPath(), $"{ProductInfo.Name}-update");
            InstallerPath = await client.DownloadAsync(release, asset, dir, progress, ct);
            StatusMessage = "Starting the installer...";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or OAuthException or SignInRequiredException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Log.Error($"Downloading the update failed: {ex}");
            StatusMessage = ex.Message.Split('\n')[0];
            StatusIsWarning = true;
            throw new OperationCanceledException(ex.Message);
        }
    }
}

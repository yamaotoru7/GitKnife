using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using GitKnife.Core;
using GitKnife.Core.Logging;
using GitKnife.Core.Settings;
using GitKnife.Core.Update;
using GitKnife.Views;

namespace GitKnife;

/// <summary>
/// 更新（配布先の Forgejo、GitHub、GitLab のリリースから入れる。サーバーの種類は初めて確かめるときに判定して覚える。サインインが要るサーバーはブラウザで認証）。
/// - File > Check for Updates...：必要ならブラウザでサインインしてもらい、新しい版があれば "Update Available" を出す。
///   サーバーがサインインを求めているのに OAuth2 の Client ID が無ければ（インストーラに組み込まないので最初の 1 回）入れてもらう。
/// - 起動時：インストールした GitKnife で、前の確認から 1 日たっていれば、ブラウザを開かずに確かめる（トークンが無ければログに書いて何もしない）。
/// - "Update and Restart"：インストーラを落として照合し、黙って入れる引数で起動して GitKnife を閉じる。インストーラが終わったら起動し直す（GitKnife.iss の /RELAUNCH=1）。
/// </summary>
internal static class Updater
{
    /// <summary>GitKnife.iss の AppId（Inno Setup はアンインストール情報を "&lt;AppId&gt;_is1" に書く）。</summary>
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{18E2FE2B-8D93-46A7-A9E1-F73233F0476C}_is1";
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);
    /// <summary>確認中（ブラウザの許可待ちを含む）。もう一度押されたら取り消してやり直す。</summary>
    private static CancellationTokenSource? _pending;
    private static bool _dialogOpen;

    /// <summary>インストーラが入れた場所（入っていなければ null）。</summary>
    public static string? InstalledDirectory
    {
        get
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(UninstallKey);
            return key?.GetValue("InstallLocation") as string;
        }
    }

    /// <summary>今動いているのがインストールした GitKnife か（開発中の bin\Debug などは false）。</summary>
    public static bool IsInstalledCopy =>
        InstalledDirectory is { } dir && string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(AppContext.BaseDirectory).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    public static async void CheckOnStartup()
    {
        try
        {
            var s = App.Settings.Update;
            if (!s.CheckAutomatically) return;
            if (!IsInstalledCopy) { Log.Debug($"Update check on startup skipped: not the installed copy ({AppContext.BaseDirectory})"); return; }
            if (DateTime.TryParse(App.Settings.LastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var last) && DateTime.Now - last < Interval)
            {
                Log.Info($"Last update check: {last:G}. Next one: {last + Interval:G}");
                return;
            }
            // 起動の邪魔をしない
            await Task.Delay(TimeSpan.FromSeconds(10));
            await CheckAsync(interactive: false);
        }
        catch (Exception ex) { Log.Error($"Update check on startup failed: {ex}"); }
    }

    /// <param name="interactive">
    /// メニューから。サインインにブラウザを開き、「最新です」や失敗も知らせる。サーバーがサインインを求めているのに Client ID が無ければ入れてもらって続ける。
    /// 自動（false）のときは、サインインが要れば何もしない。
    /// </param>
    public static async Task CheckAsync(bool interactive)
    {
        if (await CheckOnceAsync(interactive) && interactive) await CheckOnceAsync(interactive);
    }

    /// <summary>確かめる。Client ID を入れてもらったのでもう一度確かめるときは true。</summary>
    private static async Task<bool> CheckOnceAsync(bool interactive)
    {
        if (_dialogOpen) return false;
        if (_pending is not null)
        {
            // 前の確認がブラウザの許可を待っている（許可画面でエラーになるとブラウザは戻ってこない）。押し直しはやり直しとみなす
            if (!interactive) return false;
            Log.Info("Check for Updates pressed again: canceling the previous check");
            _pending.Cancel();
        }
        // ダウンロード元が分からなかった（インストーラが読めなかった）：手動なら送り先を入れてもらう。自動なら今の値（既定は GitHub）で確かめる
        if (interactive && !App.Settings.IsRepositoryKnown())
        {
            Log.Info("Update source is not known: asking for the repository");
            _dialogOpen = true;
            try { if (!Views.Dialogs.DialogWindow.Run(new ViewModels.Dialogs.RepositoryUrlDialogViewModel(), out _)) return false; }
            finally { _dialogOpen = false; }
        }
        using var cts = new CancellationTokenSource();
        _pending = cts;
        try
        {
            var s = App.Settings.Update;
            Log.Info($"Checking for updates ({s.ServerUrl.Trim().TrimEnd('/')}/{s.Repository}, current {ProductInfo.Version}{(interactive ? "" : ", automatic")})");
            var kind = await ServerKinds.ResolveAsync(s.ServerUrl, cts.Token);
            using var client = new UpdateClient(s.ServerUrl, s.Repository, s.OAuthClientId, s.OAuthScope, new TokenStore(s), kind: kind);
            ReleaseInfo? latest;
            try { latest = await client.GetLatestReleaseAsync(interactive ? OpenBrowser : null, cts.Token); }
            catch (SignInRequiredException ex)
            {
                if (interactive && ex.NeedsClientId)
                {
                    Log.Info("Update server requires sign-in, but no OAuth2 Client ID is set: asking for it");
                    _dialogOpen = true;
                    return Views.Dialogs.DialogWindow.Run(new ViewModels.Dialogs.UpdateClientIdDialogViewModel(s.ServerUrl), out _);
                }
                Log.Info("Update check skipped: sign-in to the update server is required (File > Check for Updates...)");
                return false;
            }
            App.Settings.LastUpdateCheck = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            App.SaveSettings();
            Log.Info($"Update check: latest {(latest is null ? "(none)" : latest.TagName)}, current {ProductInfo.Version}");

            if (latest is null || !UpdateVersion.IsNewer(latest, ProductInfo.Version))
            {
                if (interactive) Alerts.ShowInfo("Check for Updates", $"GitKnife {ProductInfo.Version} is the latest version.");
                return false;
            }
            if (latest.Installer is null)
            {
                Log.Warning($"Release {latest.TagName} has no installer asset");
                if (interactive) Alerts.ShowError("Check for Updates", $"GitKnife {latest.Version} is available, but the release has no installer.\n{latest.HtmlUrl}");
                return false;
            }

            var notes = await NotesSinceCurrentAsync(client, latest, cts.Token);
            _pending = null;
            _dialogOpen = true;
            var vm = new ViewModels.Dialogs.UpdateDialogViewModel(client, latest, notes);
            if (!Views.Dialogs.DialogWindow.Run(vm, out _) || vm.InstallerPath is null) return false;
            Install(vm.InstallerPath);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { Log.Info("Update check canceled"); }
        catch (Exception ex) when (ex is HttpRequestException or OAuthException or OperationCanceledException or InvalidDataException or IOException or ArgumentException or JsonException)
        {
            Log.Error($"Update check failed: {ex}");
            if (interactive) Alerts.ShowError("Check for Updates", ex.Message);
        }
        finally
        {
            if (_pending == cts) _pending = null;
            _dialogOpen = false;
        }
        return false;
    }

    /// <summary>今の版から最新までのリリースノート。途中の版が読めなくても更新は止めず、最新のノートだけを出す。</summary>
    private static async Task<string> NotesSinceCurrentAsync(UpdateClient client, ReleaseInfo latest, CancellationToken ct)
    {
        try { return UpdateVersion.CombineNotes(await client.GetReleasesSinceAsync(ProductInfo.Version, ct), latest, ProductInfo.Version); }
        catch (Exception ex) when (ex is HttpRequestException or OAuthException or SignInRequiredException or JsonException or InvalidOperationException)
        {
            Log.Warning($"Reading the skipped releases failed: {ex.Message}");
            return latest.Body;
        }
    }

    /// <summary>インストーラを黙って入れる引数で起動し、GitKnife を閉じる（設定と窓の位置はふだんの終了で保存される）。</summary>
    private static void Install(string installer)
    {
        var log = Path.Combine(ProductInfo.AppDataDirectory, "logs", "update-setup.log");
        var args = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH=1 /LOG=\"{log}\"";
        Log.Info($"Starting the installer: {installer} {args}");
        Process.Start(new ProcessStartInfo(installer, args) { UseShellExecute = false });
        Application.Current.MainWindow?.Close();
    }

    private static void OpenBrowser(string url)
    {
        var q = url.IndexOf('?');
        Log.Info($"Opening {(q < 0 ? url : url[..q])} in the browser");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    /// <summary>OAuth のトークンを資格情報マネージャーに JSON で置く（"GitKnife:update:&lt;サーバーの URL&gt;"）。</summary>
    private sealed class TokenStore(UpdateSettings s) : ITokenStore
    {
        private readonly string _target = $"{ProductInfo.Name}:update:{s.ServerUrl.Trim().TrimEnd('/')}";

        public OAuthTokens? Load()
        {
            try { return CredentialStore.Read(_target) is { Length: > 0 } json ? JsonSerializer.Deserialize<OAuthTokens>(json) : null; }
            catch (JsonException) { return null; }
        }

        public void Save(OAuthTokens? tokens)
        {
            if (tokens is null) CredentialStore.Delete(_target);
            else CredentialStore.Write(_target, JsonSerializer.Serialize(tokens));
        }
    }
}

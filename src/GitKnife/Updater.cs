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
///   入れた ID はサインインできてから保存し、サインインできなかった ID（保存したものも）は捨てて次の確認で入れ直してもらう。
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
    /// <summary>確認中の確認がブラウザの許可を待っているときの Client ID。</summary>
    private static string? _browserClientId;
    /// <summary>サインインできなかった Client ID（次に入力画面を出すときに最初から入れておく）。</summary>
    private static string? _rejectedClientId;
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
        if (await CheckOnceAsync(interactive, null) is { } entered && interactive) await CheckOnceAsync(interactive, entered);
    }

    /// <summary>確かめる。</summary>
    /// <param name="enteredClientId">入力画面で入れてもらった Client ID（まだ保存していない。サインインできたら保存する）。</param>
    /// <returns>Client ID を入れてもらったので、それでもう一度確かめるときはその値。</returns>
    private static async Task<string?> CheckOnceAsync(bool interactive, string? enteredClientId)
    {
        if (_dialogOpen) return null;
        if (_pending is not null)
        {
            // 前の確認がブラウザの許可を待っている（許可画面でエラーになるとブラウザは戻ってこない）。押し直しはやり直しとみなす
            if (!interactive) return null;
            Log.Info("Check for Updates pressed again: canceling the previous check");
            // 許可画面から戻ってこなかった Client ID は使い続けない（登録されていない ID だと、毎回同じエラーになる）
            if (_browserClientId is { } waiting) RejectClientId(waiting);
            _browserClientId = null;
            _pending.Cancel();
        }
        // ダウンロード元が分からなかった（インストーラが読めなかった）：手動なら送り先を入れてもらう。自動なら今の値（既定は GitHub）で確かめる
        if (interactive && !App.Settings.IsRepositoryKnown())
        {
            Log.Info("Update source is not known: asking for the repository");
            _dialogOpen = true;
            try { if (!Views.Dialogs.DialogWindow.Run(new ViewModels.Dialogs.RepositoryUrlDialogViewModel(), out _)) return null; }
            finally { _dialogOpen = false; }
        }
        using var cts = new CancellationTokenSource();
        _pending = cts;
        var s = App.Settings.Update;
        var clientId = enteredClientId ?? s.OAuthClientId.Trim();
        var browserOpened = false;
        try
        {
            Log.Info($"Checking for updates ({s.ServerUrl.Trim().TrimEnd('/')}/{s.Repository}, current {ProductInfo.Version}{(interactive ? "" : ", automatic")})");
            var kind = await ServerKinds.ResolveAsync(s.ServerUrl, cts.Token);
            using var client = new UpdateClient(s.ServerUrl, s.Repository, clientId, s.OAuthScope, new TokenStore(s), kind: kind);
            Action<string>? openBrowser = interactive ? url =>
            {
                browserOpened = true;
                if (_pending == cts) _browserClientId = clientId;
                OpenBrowser(url);
            } : null;
            ReleaseInfo? latest;
            try { latest = await client.GetLatestReleaseAsync(openBrowser, cts.Token); }
            catch (SignInRequiredException ex)
            {
                if (interactive && ex.NeedsClientId)
                {
                    Log.Info("Update server requires sign-in, but no OAuth2 Client ID is set: asking for it");
                    _dialogOpen = true;
                    var idVm = new ViewModels.Dialogs.UpdateClientIdDialogViewModel(s.ServerUrl, _rejectedClientId);
                    return Views.Dialogs.DialogWindow.Run(idVm, out _) ? idVm.ClientId.Trim() : null;
                }
                Log.Info("Update check skipped: sign-in to the update server is required (File > Check for Updates...)");
                return null;
            }
            // サインインできた（またはサインインが要らなかった）ので、入れてもらった Client ID を残す
            if (_pending == cts) _browserClientId = null;
            if (enteredClientId is not null)
            {
                s.OAuthClientId = enteredClientId;
                _rejectedClientId = null;
                Log.Info("OAuth2 Client ID for the update server saved");
            }
            App.Settings.LastUpdateCheck = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            App.SaveSettings();
            Log.Info($"Update check: latest {(latest is null ? "(none)" : latest.TagName)}, current {ProductInfo.Version}");

            if (latest is null || !UpdateVersion.IsNewer(latest, ProductInfo.Version))
            {
                if (interactive) Alerts.ShowInfo("Check for Updates", $"GitKnife {ProductInfo.Version} is the latest version.");
                return null;
            }
            if (latest.Installer is null)
            {
                Log.Warning($"Release {latest.TagName} has no installer asset");
                if (interactive) Alerts.ShowError("Check for Updates", $"GitKnife {latest.Version} is available, but the release has no installer.\n{latest.HtmlUrl}");
                return null;
            }

            var notes = await NotesSinceCurrentAsync(client, latest, cts.Token);
            _pending = null;
            _dialogOpen = true;
            var vm = new ViewModels.Dialogs.UpdateDialogViewModel(client, latest, notes);
            if (!Views.Dialogs.DialogWindow.Run(vm, out _) || vm.InstallerPath is null) return null;
            Install(vm.InstallerPath);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested) { Log.Info("Update check canceled"); }
        catch (Exception ex) when (ex is HttpRequestException or OAuthException or OperationCanceledException or InvalidDataException or IOException or ArgumentException or JsonException)
        {
            Log.Error($"Update check failed: {ex}");
            // ブラウザでのサインインに失敗した（時間切れ、拒否、トークンを出してもらえない）Client ID は使い続けない
            if (ex is OAuthException && browserOpened) RejectClientId(clientId);
            if (interactive) Alerts.ShowError("Check for Updates", ex.Message);
        }
        finally
        {
            if (_pending == cts)
            {
                _pending = null;
                _browserClientId = null;
            }
            _dialogOpen = false;
        }
        return null;
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

    /// <summary>
    /// サインインできなかった Client ID を捨てる。保存してあれば消すので、次の手動の確認で入力画面がもう一度出る
    /// （登録されていない ID だと、許可画面がエラーになりブラウザが戻ってこない。保存したままだと毎回同じエラーになっていた）。
    /// </summary>
    private static void RejectClientId(string clientId)
    {
        if (clientId.Length == 0) return;
        _rejectedClientId = clientId;
        var s = App.Settings.Update;
        if (!string.Equals(s.OAuthClientId.Trim(), clientId, StringComparison.Ordinal)) return;
        s.OAuthClientId = "";
        App.SaveSettings();
        Log.Info("Sign-in with the saved OAuth2 Client ID did not complete: cleared it (asked again on the next Check for Updates)");
    }

    /// <summary>更新のサーバーにサインインしている（トークンか Client ID が残っている）。Preferences > Updates の Sign Out を出すか。</summary>
    public static bool HasSignIn
    {
        get
        {
            var s = App.Settings.Update;
            return s.OAuthClientId.Trim().Length > 0 || new TokenStore(s).Load() is not null;
        }
    }

    /// <summary>更新のサーバーのトークンと Client ID を消す（次の手動の確認でサインインし直す）。</summary>
    public static void SignOut()
    {
        var s = App.Settings.Update;
        new TokenStore(s).Save(null);
        s.OAuthClientId = "";
        App.SaveSettings();
        Log.Info("Signed out of the update server (token and OAuth2 Client ID cleared)");
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

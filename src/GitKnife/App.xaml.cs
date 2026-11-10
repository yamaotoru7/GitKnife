using Microsoft.Win32;
﻿using System.Windows;
using GitKnife.Core;
using GitKnife.Core.Git;
using GitKnife.Core.Logging;
using GitKnife.Core.Settings;
using GitKnife.ViewModels;
using GitKnife.Views;

namespace GitKnife;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();
    /// <summary>Preferences を閉じたとき（表示に効く設定が変わったかもしれない）。</summary>
    public static event EventHandler? SettingsChanged;
    public static void RaiseSettingsChanged() => SettingsChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>すぐに settings.json に書く（リポジトリの一覧など、落ちても失いたくない変更の後。終了時にも書く）。</summary>
    public static void SaveSettings()
    {
        try { SettingsStore.Save(Settings); }
        catch (Exception ex) { Log.Error($"Saving settings failed: {ex.Message}"); }
    }

    private static System.Threading.Mutex? _instanceMutex;
    /// <summary>1 つ目の起動か（2 つ目は設定を読まないので、終わるときに書かない）。</summary>
    private static bool _isPrimary;
    private static string PipeName => $"{ProductInfo.Name}-{Environment.UserName}-open";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // 画面の言語を OS の言語に（WPF の既定は en-US）。日本語の Windows ではパスの "\" を "¥" で描き、ツールチップの日付も
        // "2025/03/19 (水)" の形になる。
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentCulture.IetfLanguageTag)));
        // 単一インスタンス：2 つ目の起動は渡されたパスを 1 つ目に送って終わる
        _instanceMutex = new System.Threading.Mutex(true, $"{ProductInfo.Name}-{Environment.UserName}-instance", out var first);
        _isPrimary = first;
        if (!first)
        {
            SendToFirstInstance(e.Args.Where(a => !a.StartsWith('-')).Select(System.IO.Path.GetFullPath).ToList());
            Shutdown(0);
            return;
        }
        Log.UseFile();
        // 未処理の例外はログに残す（A20 のクラッシュレポートの土台）
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("Unhandled exception: " + ex.Exception);
            MessageBox.Show(ex.Exception.Message, ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => Log.Error("Unhandled exception (domain): " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) => { Log.Error("Unobserved task exception: " + ex.Exception); ex.SetObserved(); };
        Log.Info($"{ProductInfo.Name} {ProductInfo.Version} starting");
        BugReporter.Register();
        Settings = SettingsStore.Load();
        // Repository Manager の一覧は repositories.toml（T110）。無ければ settings.json の RepositoryManager（前の版の形）から作る
        try { Core.Settings.RepositoryList.CreateFrom(Settings.RepositoryManager); }
        catch (System.IO.IOException ex) { Log.Warning($"Creating repositories.toml failed: {ex.Message}"); }
        ThemeManager.Apply(Settings);
        // FollowSystemTheme のときは Windows のアプリの配色を変えたらすぐ合わせる（ツールバーの Appearance > System で選ぶ）
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category == UserPreferenceCategory.General && Settings.FollowSystemTheme)
                Dispatcher.BeginInvoke(() => ThemeManager.Apply(Settings));
        };

        // \\gitknife-ssh\<名前>\… のリポジトリは Preferences > Hosts に登録した接続先で（T1301）
        GitHost.SshHostResolver = name => Settings.SshHosts.FirstOrDefault(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase))?.ToHost();
        // SSH の接続のパスワード・パスフレーズ・知らないホストの確認は GitKnife.AskPass から画面で聞く（T1305）
        SshAuth.Start(Dispatcher);
        GitProcess git;
        try { git = new GitProcess(GitExecutable.Resolve(Settings.GitInstancePath)); }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, ProductInfo.Name, MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        Log.Info($"Git: {git.Git.Path}");

        var vm = new MainViewModel(Settings, git);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
        // 画面のスレッドの止まりをログに残す
        ResponsivenessMonitor.Start(Dispatcher);
        _ = ListenForOtherInstancesAsync(vm, window);

        // 前回のワークスペースのタブを復元し、起動引数のリポジトリ（A07）はその後ろに足す。
        // 引数のリポジトリだけを開くと、ワークスペースのタブの一覧がそれ 1 つで上書き保存される（2026-11-04 に Tools のタブが消えた）
        var paths = e.Args.Where(a => !a.StartsWith('-')).ToList();
        var ws = Settings.Workspaces.All.FirstOrDefault(w => w.Name == Settings.Workspaces.ActiveWorkspace);
        if (ws is not null)
        {
            Log.Info($"Restore workspace '{ws.Name}' with {ws.Repositories.Count} tabs");
            await vm.RestoreTabsAsync(ws.Repositories.Where(MainViewModel.CanRestore).ToList(), ws.ActiveRepository);
        }
        foreach (var p in paths) await vm.OpenAsync(System.IO.Path.GetFullPath(p));
    }

    /// <summary>2 つ目の起動から渡されたパスを 1 行ずつ受け取って開く（窓を前に出す）。</summary>
    private static async Task ListenForOtherInstancesAsync(MainViewModel vm, Window window)
    {
        while (true)
        {
            try
            {
                using var server = new System.IO.Pipes.NamedPipeServerStream(PipeName, System.IO.Pipes.PipeDirection.In, 1, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync();
                using var reader = new System.IO.StreamReader(server);
                var lines = new List<string>();
                while (await reader.ReadLineAsync() is { } line) if (line.Length > 0) lines.Add(line);
                await window.Dispatcher.InvokeAsync(async () =>
                {
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                    foreach (var p in lines.Where(MainViewModel.CanRestore)) await vm.OpenAsync(p);
                });
            }
            catch (Exception ex) { Log.Warning($"Instance pipe failed: {ex.Message}"); await Task.Delay(1000); }
        }
    }

    private static void SendToFirstInstance(IReadOnlyList<string> paths)
    {
        try
        {
            using var client = new System.IO.Pipes.NamedPipeClientStream(".", PipeName, System.IO.Pipes.PipeDirection.Out);
            client.Connect(3000);
            using var writer = new System.IO.StreamWriter(client);
            foreach (var p in paths) writer.WriteLine(p);
            writer.WriteLine("");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_isPrimary) { base.OnExit(e); return; }
        try { SettingsStore.Save(Settings); Log.Debug("Saving settings"); }
        catch (Exception ex) { Log.Error($"Saving settings failed: {ex.Message}"); }
        SshGitHost.CloseSessions();   // 常駐の ssh（T1307）
        SshAuth.Stop();
        base.OnExit(e);
    }
}

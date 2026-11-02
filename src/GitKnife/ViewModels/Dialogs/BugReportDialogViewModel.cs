using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Logging;
using GitKnife.Core.Reporting;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>撮った画像 1 枚。プレビューの左上のチェックで添付するかを選ぶ（#8）。</summary>
public sealed partial class BugReportImageChoice(BugReportImage image, BitmapSource preview) : ObservableObject
{
    public BugReportImage Image { get; } = image;
    public BitmapSource Preview { get; } = preview;
    [ObservableProperty] private bool _isSelected = true;
}

/// <summary>
/// "Report a Bug"（Ctrl+Shift+F12）。撮った画面の縮小、題名、説明、ログと環境を付けるか、送り先（Forgejo か GitHub の URL・リポジトリ・トークン）。
/// Send で %LocalAppData%\GitKnife\bug-reports\ に保存してから Issue を作り、作った Issue をブラウザで開く。
/// GitHub なら保存してから、題名と本文を入れた New issue の画面と、画像を保存したフォルダを開く（Open in Browser）。
/// 送り先の URL が空なら保存だけしてフォルダを開く。送れなかったときはダイアログを閉じずにフッターに理由を出す（保存したものは残る）。
/// </summary>
public sealed partial class BugReportDialogViewModel : DialogViewModel
{
    private readonly string _activeWindowTitle;
    private string? _savedDirectory;

    public BugReportDialogViewModel(IReadOnlyList<BugReportImage> images, string activeWindowTitle)
    {
        _activeWindowTitle = activeWindowTitle;
        Images = images.Select(i => new BugReportImageChoice(i, ToBitmap(i.Png))).ToList();
        foreach (var c in Images) c.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IncludeScreenshots));
        var s = App.Settings.BugReport;
        _serverUrl = s.ServerUrl;
        _repository = s.Repository;
        _allowUntrustedCertificate = s.AllowUntrustedCertificate;
        _assignIssue = s.AssignIssue;
        try { _token = BugReportToken.Read(s.ServerUrl) ?? ""; }
        catch (Exception ex) { Log.Warning($"Reading bug report token failed: {ex.Message}"); _token = ""; }
    }

    public override string Title => "Report a Bug";
    public override string Subtitle => "The screenshot, the environment and the end of the log are attached.";
    public override string SubmitText => ServerUrl.Trim().Length == 0 ? "Save" : IsGitHub ? "Open in Browser" : "Send";

    /// <summary>送り先が GitHub（ブラウザで New issue を開く。トークン・担当・証明書の欄は使わない）。</summary>
    public bool IsGitHub => Core.Distribution.IsGitHub(ServerUrl);
    public override double DialogWidth => 640;

    public IReadOnlyList<BugReportImageChoice> Images { get; }

    /// <summary>
    /// "Attach screenshot"：画像ごとのチェックと連動する（全部なら true、どれもなければ false、一部なら null）。押すと全部を入れる／外す。
    /// 一部のときに押すと全部を入れる（CheckBox は null から false にするので、ここで向きを変える）。
    /// </summary>
    public bool? IncludeScreenshots
    {
        get => Images.All(i => i.IsSelected) ? true : Images.Any(i => i.IsSelected) ? null : false;
        set
        {
            var select = value != false || IncludeScreenshots is null;
            foreach (var i in Images) i.IsSelected = select;
        }
    }

    /// <summary>"Assign to &lt;Assignee&gt;"：作った Issue の担当にする（#8）。</summary>
    public string AssignText => $"Assign to {App.Settings.BugReport.Assignee}";
    public bool CanAssign => App.Settings.BugReport.Assignee.Trim().Length > 0;
    [ObservableProperty] private bool _assignIssue;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _issueTitle = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _includeLog = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SubmitText), nameof(IsGitHub))] private string _serverUrl;
    [ObservableProperty] private string _repository;
    /// <summary>PasswordBox から入れる（束縛できないので View が PasswordChanged で書く）。</summary>
    public string Token { get => _token; set => _token = value; }
    private string _token;
    [ObservableProperty] private bool _allowUntrustedCertificate;

    public override bool CanSubmit => IssueTitle.Trim().Length > 0;

    private static BitmapSource ToBitmap(byte[] png)
    {
        var b = new BitmapImage();
        b.BeginInit();
        b.StreamSource = new System.IO.MemoryStream(png);
        b.CacheOption = BitmapCacheOption.OnLoad;
        b.DecodePixelWidth = 480;
        b.EndInit();
        b.Freeze();
        return b;
    }

    private BugReport Build()
    {
        var report = new BugReport { Title = IssueTitle.Trim(), Description = Description };
        report.Images.AddRange(Images.Where(i => i.IsSelected).Select(i => i.Image));
        if (AssignIssue && CanAssign && !IsGitHub) report.Assignees.Add(App.Settings.BugReport.Assignee.Trim());
        var main = App.Current.MainWindow?.DataContext as MainViewModel;
        var tab = main?.SelectedTab;
        void Env(string k, string? v) { if (!string.IsNullOrEmpty(v)) report.Environment.Add(new(k, v)); }
        Env("GitKnife", Core.ProductInfo.Version);
        Env("OS", Environment.OSVersion.VersionString);
        Env(".NET", Environment.Version.ToString());
        Env("git", main?.Git.Git.Path);
        Env("Theme", $"{App.Settings.Theme}{(App.Settings.FollowSystemTheme ? " (follow system)" : "")}");
        Env("Window", _activeWindowTitle);
        Env("Workspace", App.Settings.Workspaces.ActiveWorkspace);
        if (tab is { IsManager: false })
        {
            Env("Repository", tab.Path);
            Env("Branch", tab.CurrentBranchText);
        }
        Env("Time", report.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        if (IncludeLog) report.LogTail = BugReport.ReadLogTail(Log.DefaultPath);
        return report;
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var report = Build();
        // 送り先の設定は送る前に覚える（送れなくても次に入れ直さなくてよい）
        var s = App.Settings.BugReport;
        s.ServerUrl = ServerUrl.Trim();
        s.Repository = Repository.Trim();
        s.AllowUntrustedCertificate = AllowUntrustedCertificate;
        s.AssignIssue = AssignIssue;
        App.SaveSettings();
        if (s.ServerUrl.Length > 0 && Token.Trim().Length > 0)
        {
            try { BugReportToken.Write(s.ServerUrl, Token.Trim()); }
            catch (Exception ex) { Log.Warning($"Saving bug report token failed: {ex.Message}"); }
        }

        // 送り直しのときも保存は 1 回だけ
        _savedDirectory ??= await Task.Run(() => report.Save(), ct);
        Log.Info($"Bug report saved to {_savedDirectory}");
        if (s.ServerUrl.Length == 0)
        {
            Open(_savedDirectory);
            return;
        }
        if (IsGitHub)
        {
            // トークンなしで Issue は作れないので、題名と本文を入れた New issue をブラウザで開く。画像は保存したフォルダから貼ってもらう
            if (s.Repository.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2) Fail("Repository must be 'owner/repo'.");
            Log.Info($"Bug report: opening a new GitHub issue for {s.Repository}");
            if (report.Images.Count > 0) Open(_savedDirectory);
            Open(report.GitHubNewIssueUrl(s.ServerUrl, s.Repository));
            return;
        }
        if (Token.Trim().Length == 0) Fail("Enter an access token (Forgejo > Settings > Applications, scope write:issue).");

        StatusMessage = "Sending...";
        try
        {
            using var handler = new System.Net.Http.HttpClientHandler();
            if (s.AllowUntrustedCertificate) handler.ServerCertificateCustomValidationCallback = System.Net.Http.HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
            using var client = new ForgejoIssueClient(s.ServerUrl, s.Repository, Token, handler);
            var issue = await report.SendAsync(client, ct);
            Log.Info($"Bug report sent: #{issue.Number} {issue.HtmlUrl}");
            if (issue.HtmlUrl.Length > 0) Open(issue.HtmlUrl);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or ArgumentException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            Log.Error($"Sending bug report failed: {ex}");
            Fail($"{(ex.InnerException ?? ex).Message.Split('\n')[0]} (saved to {_savedDirectory})");
        }
    }

    /// <summary>フッターに理由を出し、ダイアログは開いたままにする（DialogWindow は OperationCanceledException では閉じない）。</summary>
    private void Fail(string message)
    {
        StatusMessage = message;
        StatusIsWarning = true;
        throw new OperationCanceledException(message);
    }

    private static void Open(string target)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { Log.Warning($"Opening {target} failed: {ex.Message}"); }
    }
}

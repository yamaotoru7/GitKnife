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
/// "Report a Bug"（Ctrl+Shift+F12）。撮った画面の縮小、題名、説明、ログと環境を付けるか、送り先（GitHub か Forgejo の URL とリポジトリ）。
/// Open in Browser で %LocalAppData%\GitKnife\bug-reports\ に保存してから、題名と本文を入れた New issue の画面と、画像を保存したフォルダを開く。
/// Issue は報告する人が自分のアカウントで作る（画像と log.txt はドラッグして貼る。担当も画面で選ぶ）。送り先の URL が空なら保存だけしてフォルダを開く。
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
    }

    public override string Title => "Report a Bug";
    public override string Subtitle => "Opens a new issue in the browser with the environment and the end of the log filled in.";
    public override string SubmitText => ServerUrl.Trim().Length == 0 ? "Save" : "Open in Browser";
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

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _issueTitle = "";
    [ObservableProperty] private string _description = "";
    [ObservableProperty] private bool _includeLog = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(SubmitText))] private string _serverUrl;
    [ObservableProperty] private string _repository;

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
        App.SaveSettings();
        if (s.ServerUrl.Length > 0)
        {
            if (!Uri.TryCreate(s.ServerUrl, UriKind.Absolute, out var u) || u.Scheme is not ("https" or "http")) Fail("Server must be a URL like https://github.com/.");
            if (s.Repository.Split('/', StringSplitOptions.RemoveEmptyEntries).Length != 2) Fail("Repository must be 'owner/repo'.");
        }

        // もう一度押したときも保存は 1 回だけ
        _savedDirectory ??= await Task.Run(() => report.Save(), ct);
        Log.Info($"Bug report saved to {_savedDirectory}");
        if (s.ServerUrl.Length == 0)
        {
            Open(_savedDirectory);
            return;
        }
        // 題名と本文を入れた New issue をブラウザで開く。画像と log.txt は保存したフォルダから貼ってもらう
        Log.Info($"Bug report: opening a new issue for {s.Repository} on {s.ServerUrl}");
        var url = report.NewIssueUrl(s.ServerUrl, s.Repository);
        // 貼るものがあるときだけフォルダを開く（画像か、本文に入りきらなかったログ。そのときは本文が log.txt を案内する）
        if (report.Images.Count > 0 || url.Contains(BugReport.LogFileName, StringComparison.Ordinal)) Open(_savedDirectory);
        Open(url);
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

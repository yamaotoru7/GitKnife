using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>場所の 1 行（この PC、WSL のディストリビューション、登録した SSH のホスト）。</summary>
public sealed record RepositoryLocation(GitHost Host)
{
    public bool IsLocal => Host is LocalGitHost;
    public string Label => IsLocal ? "This PC" : Host.DisplayName;
    public override string ToString() => Label;
}

/// <summary>
/// 場所（This PC／WSL／SSH）を選んでフォルダを決めるダイアログの共通部分（Open Repository と Clone）。
/// This PC は Windows のパス（システムのフォルダ選択）、WSL と SSH は接続先のパス（/home/…）とその下のフォルダの一覧。
/// WSL と SSH はホームのフォルダから始める。New Host... で SSH のホストを足せる。
/// </summary>
public abstract partial class HostFolderDialogViewModel : DialogViewModel
{
    protected readonly AppSettings Settings;
    protected readonly Func<string, GitProcess> GitFor;
    private readonly string _lastLocation;
    private readonly string _localDefault;

    /// <param name="lastLocation">最後に使った場所の Label（最初の選択）。</param>
    /// <param name="localDefault">This PC のときに最初に入れるパス。</param>
    protected HostFolderDialogViewModel(AppSettings settings, Func<string, GitProcess> gitFor, string lastLocation, string localDefault)
    {
        Settings = settings;
        GitFor = gitFor;
        _lastLocation = lastLocation;
        _localDefault = localDefault;
        Locations.Add(new RepositoryLocation(GitHost.Local));
        foreach (var h in settings.SshHosts.Where(h => h.Name.Length > 0)) Locations.Add(new RepositoryLocation(h.ToHost()));
        _selectedLocation = Locations.FirstOrDefault(l => l.Label == lastLocation) ?? Locations[0];
        _folderPath = IsLocal ? localDefault : "";
        _ = LoadWslAsync();
        _ = StartAsync();
    }

    public ObservableCollection<RepositoryLocation> Locations { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsLocal), nameof(IsRemote))] private RepositoryLocation _selectedLocation;
    /// <summary>入力欄のパス（This PC は Windows の形、WSL と SSH は接続先の形）。</summary>
    [ObservableProperty] private string _folderPath;

    public bool IsLocal => SelectedLocation.IsLocal;
    public bool IsRemote => !IsLocal;

    /// <summary>パスの下のフォルダ（先頭は ".."）。選ぶとそのフォルダに入る。WSL と SSH だけ（This PC はシステムのフォルダ選択）。</summary>
    public ObservableCollection<string> Folders { get; } = [];
    [ObservableProperty] private string? _selectedFolder;
    private CancellationTokenSource? _loading;

    /// <summary>入力欄のパスを、この場所を指す Windows の形にしたもの（\\wsl.localhost\…、\\gitknife-ssh\…）。</summary>
    protected string LocalFolder => SelectedLocation.Host.BrowsePath(FolderPath.Trim());

    /// <summary>パスの形が場所に合っている（This PC は絶対パス、WSL と SSH は / で始まる）。</summary>
    protected bool FolderPathIsValid => FolderPath.Trim() is { Length: > 0 } p && (IsLocal ? Path.IsPathFullyQualified(p) : p.StartsWith('/'));

    /// <summary>システムのフォルダ選択の最初の場所：入っているパス、無ければ This PC の既定。</summary>
    public virtual string BrowseStart => Directory.Exists(FolderPath.Trim()) ? FolderPath.Trim() : _localDefault;

    partial void OnSelectedLocationChanged(RepositoryLocation value)
    {
        StatusMessage = null;
        StatusIsWarning = false;
        FolderPath = value.IsLocal ? _localDefault : "";   // パスの形が場所ごとに違う
        Folders.Clear();
        RaiseCanSubmit();
        _ = StartAsync();
    }

    partial void OnFolderPathChanged(string value)
    {
        RaiseCanSubmit();
        _ = LoadFoldersAsync();
    }

    partial void OnSelectedFolderChanged(string? value)
    {
        if (value is null) return;
        var path = FolderPath.Trim().TrimEnd('/');
        FolderPath = value == ".." ? (path.LastIndexOf('/') is var i and > 0 ? path[..i] : "/") : path + "/" + value;
    }

    /// <summary>WSL のディストリビューションを This PC の次に足す（`wsl -l -q` を待たずに開く）。最後に使った場所が WSL ならここで選ぶ。</summary>
    private async Task LoadWslAsync()
    {
        var names = await Task.Run(WslGitHost.Distributions);
        var at = 1;
        foreach (var n in names) Locations.Insert(at++, new RepositoryLocation(new WslGitHost(n)));
        if (IsLocal && FolderPath == _localDefault && Locations.FirstOrDefault(l => !l.IsLocal && l.Label == _lastLocation) is { } last)
            SelectedLocation = last;
    }

    /// <summary>WSL と SSH はホームのフォルダから始める。ここで初めて接続するので、失敗はフッターに出す。</summary>
    private async Task StartAsync()
    {
        var location = SelectedLocation;
        if (location.IsLocal) return;
        StatusMessage = "Connecting...";
        try
        {
            var home = await Task.Run(location.Host.HomeDirectory);
            if (SelectedLocation != location) return;
            StatusMessage = null;
            if (home is not null && FolderPath.Trim().Length == 0) FolderPath = home;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            if (SelectedLocation != location) return;
            StatusIsWarning = true;
            StatusMessage = FirstLine(ex.Message);
        }
    }

    protected static string FirstLine(string text) => text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "Connection failed";

    private async Task LoadFoldersAsync()
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        var location = SelectedLocation;
        var path = FolderPath.Trim();
        if (location.IsLocal || !path.StartsWith('/')) { Folders.Clear(); return; }
        try
        {
            await Task.Delay(250, cts.Token);   // 打っている間は読まない
            var local = location.Host.BrowsePath(path);
            var names = await Task.Run(() => HostDirectory.EnumerateDirectories(local).Select(Path.GetFileName).OfType<string>()
                .OrderBy(n => n.StartsWith('.')).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), cts.Token);
            if (cts.IsCancellationRequested) return;
            Folders.Clear();
            if (path != "/") Folders.Add("..");
            foreach (var n in names) Folders.Add(n);
            StatusIsWarning = false;
            StatusMessage = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (cts.IsCancellationRequested) return;
            Folders.Clear();
            StatusIsWarning = MissingFolderMessage is not null;
            StatusMessage = MissingFolderMessage;
        }
    }

    /// <summary>入力欄のフォルダが無いときにフッターに出す警告（null なら出さない）。</summary>
    protected virtual string? MissingFolderMessage => "Folder not found";

    public AddSshHostDialogViewModel CreateAddHost() => new(Settings, GitFor("").Git);

    /// <summary>New Host... で足したホストを登録して選ぶ。</summary>
    public void AddHost(SshHostSettings host)
    {
        Settings.SshHosts.Add(host);
        App.SaveSettings();
        var location = new RepositoryLocation(host.ToHost());
        Locations.Add(location);
        SelectedLocation = location;
    }
}

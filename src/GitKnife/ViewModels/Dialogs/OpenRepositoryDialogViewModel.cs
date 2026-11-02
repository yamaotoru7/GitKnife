using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>Open Repository の場所の 1 行（この PC、WSL のディストリビューション、登録した SSH のホスト）。</summary>
public sealed record RepositoryLocation(GitHost Host)
{
    public bool IsLocal => Host is LocalGitHost;
    public string Label => IsLocal ? "This PC" : Host.DisplayName;
    public override string ToString() => Label;
}

/// <summary>
/// File > Open Repository...（Ctrl+O）。WSL と SSH も同じ入口から開くので、フォルダ選択の前に場所を選ぶ（A23）。
/// This PC はシステムのフォルダ選択（Browse...。選んだらそのまま開く）かパスの入力、WSL と SSH はパスの入力とその下のフォルダの一覧。
/// 開く前に `git rev-parse --show-toplevel` で確かめ、リポジトリの一番上を開く。リポジトリでなければ init を聞く（呼んだ側）。
/// </summary>
public sealed partial class OpenRepositoryDialogViewModel : DialogViewModel
{
    private readonly AppSettings _settings;
    private readonly Func<string, GitProcess> _gitFor;
    private readonly string _sourceFolder;

    public OpenRepositoryDialogViewModel(AppSettings settings, Func<string, GitProcess> gitFor, string sourceFolder)
    {
        _settings = settings;
        _gitFor = gitFor;
        _sourceFolder = sourceFolder;
        Locations.Add(new RepositoryLocation(GitHost.Local));
        foreach (var h in settings.SshHosts.Where(h => h.Name.Length > 0)) Locations.Add(new RepositoryLocation(h.ToHost()));
        _selectedLocation = Locations.FirstOrDefault(l => l.Label == settings.OpenRepositoryLocation) ?? Locations[0];
        _ = LoadWslAsync();
        _ = StartAsync();
    }

    public override string Title => "Open Repository";
    public override string Subtitle => "Choose where the repository is and open it";
    public override string SubmitText => "Open";

    public ObservableCollection<RepositoryLocation> Locations { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsLocal), nameof(IsRemote))] private RepositoryLocation _selectedLocation;
    [ObservableProperty] private string _repositoryPath = "";

    public bool IsLocal => SelectedLocation.IsLocal;
    public bool IsRemote => !IsLocal;

    /// <summary>開くパス（呼んだ側が OpenAsync する）。</summary>
    public string? OpenedPath { get; private set; }
    /// <summary>false なら <see cref="OpenedPath"/> はリポジトリではない（init するか聞く）。</summary>
    public bool IsRepository { get; private set; }

    /// <summary>パスの下のフォルダ（先頭は ".."）。選ぶとそのフォルダに入る。WSL と SSH だけ（This PC はシステムのフォルダ選択）。</summary>
    public ObservableCollection<string> Folders { get; } = [];
    [ObservableProperty] private string? _selectedFolder;
    private CancellationTokenSource? _loading;

    /// <summary>システムのフォルダ選択の最初の場所：入っているパス、無ければ Source Code Folder の先頭。</summary>
    public string BrowseStart => Directory.Exists(RepositoryPath.Trim()) ? RepositoryPath.Trim() : _sourceFolder;

    partial void OnSelectedLocationChanged(RepositoryLocation value)
    {
        StatusMessage = null;
        StatusIsWarning = false;
        RepositoryPath = "";   // パスの形が場所ごとに違う
        Folders.Clear();
        RaiseCanSubmit();
        _ = StartAsync();
    }

    partial void OnRepositoryPathChanged(string value)
    {
        RaiseCanSubmit();
        _ = LoadFoldersAsync();
    }

    partial void OnSelectedFolderChanged(string? value)
    {
        if (value is null) return;
        var path = RepositoryPath.Trim().TrimEnd('/');
        RepositoryPath = value == ".." ? (path.LastIndexOf('/') is var i and > 0 ? path[..i] : "/") : path + "/" + value;
    }

    /// <summary>WSL のディストリビューションを This PC の次に足す（`wsl -l -q` を待たずに開く）。最後に開いた場所が WSL ならここで選ぶ。</summary>
    private async Task LoadWslAsync()
    {
        var names = await Task.Run(WslGitHost.Distributions);
        var at = 1;
        foreach (var n in names) Locations.Insert(at++, new RepositoryLocation(new WslGitHost(n)));
        if (IsLocal && RepositoryPath.Length == 0 && Locations.FirstOrDefault(l => !l.IsLocal && l.Label == _settings.OpenRepositoryLocation) is { } last)
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
            if (home is not null && RepositoryPath.Trim().Length == 0) RepositoryPath = home;
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            if (SelectedLocation != location) return;
            StatusIsWarning = true;
            StatusMessage = FirstLine(ex.Message);
        }
    }

    private static string FirstLine(string text) => text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "Connection failed";

    private async Task LoadFoldersAsync()
    {
        _loading?.Cancel();
        var cts = _loading = new CancellationTokenSource();
        var location = SelectedLocation;
        var path = RepositoryPath.Trim();
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
            StatusIsWarning = true;
            StatusMessage = "Folder not found";
        }
    }

    public override bool CanSubmit => RepositoryPath.Trim() is { Length: > 0 } p && (IsLocal ? Path.IsPathFullyQualified(p) : p.StartsWith('/'));

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var host = SelectedLocation.Host;
        var local = host.BrowsePath(RepositoryPath.Trim());
        if (SelectedLocation.IsLocal && !Directory.Exists(local)) throw new GitErrorMessageException($"Folder not found: {local}");
        var git = _gitFor(local);
        var r = await git.RunAsync(new GitRequest(local, ["rev-parse", "--show-toplevel"]), ct);
        // LC_ALL=C.UTF-8 で動かすので文言は英語
        IsRepository = r.Succeeded || !r.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);
        if (IsRepository) r.ThrowIfFailed();
        OpenedPath = IsRepository ? Path.GetFullPath(git.Host.ToLocalPath(r.StandardOutput.Trim())) : local;
        _settings.OpenRepositoryLocation = SelectedLocation.Label;
    }

    public AddSshHostDialogViewModel CreateAddHost() => new(_settings, _gitFor("").Git);

    /// <summary>New Host... で足したホストを登録して選ぶ。</summary>
    public void AddHost(SshHostSettings host)
    {
        _settings.SshHosts.Add(host);
        App.SaveSettings();
        var location = new RepositoryLocation(host.ToHost());
        Locations.Add(location);
        SelectedLocation = location;
    }
}

/// <summary>
/// Open Repository の New Host...：SSH のホストを足す（登録の中身は Preferences > Hosts と同じ。変更と削除はそちら）。
/// 認証は今は鍵と ssh-agent だけ（BatchMode。T1305）なので説明に書く。Test Connection は任意で、失敗していても足せる。
/// </summary>
public sealed partial class AddSshHostDialogViewModel(AppSettings settings, GitExecutable git) : DialogViewModel
{
    public override string Title => "New SSH Host";
    public override string Subtitle => "Connects with an SSH key or ssh-agent. Passwords are not supported yet";
    public override string SubmitText => "Add";

    [ObservableProperty] private string _destination = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _gitPath = "";

    /// <summary>登録するホスト（<see cref="SubmitAsync"/> の後）。</summary>
    public SshHostSettings? Added { get; private set; }

    private SshHostSettings Current => new()
    {
        Name = SshHostSettings.CleanName(Name) is { Length: > 0 } n ? n : SshHostSettings.NameFromDestination(Destination.Trim()),
        Destination = Destination.Trim(),
        GitPath = GitPath.Trim(),
    };

    private bool NameIsTaken => settings.SshHosts.Any(h => string.Equals(h.Name, Current.Name, StringComparison.OrdinalIgnoreCase));

    partial void OnDestinationChanged(string value) => Validate();
    partial void OnNameChanged(string value) => Validate();

    private void Validate()
    {
        StatusIsSuccess = false;
        StatusIsWarning = NameIsTaken;
        StatusMessage = NameIsTaken ? $"A host named '{Current.Name}' already exists" : null;
        RaiseCanSubmit();
    }

    public override bool CanSubmit => Destination.Trim().Length > 0 && Current.Name.Length > 0 && !NameIsTaken;

    public async Task TestAsync()
    {
        if (Destination.Trim().Length == 0) return;
        StatusIsSuccess = StatusIsWarning = false;
        StatusMessage = "Testing...";
        var (ok, text) = await SshHostEntry.TestAsync(git, Current.ToHost());
        StatusIsSuccess = ok;
        StatusIsWarning = !ok;
        StatusMessage = text;
    }

    public override Task SubmitAsync(CancellationToken ct)
    {
        Added = Current;
        return Task.CompletedTask;
    }
}

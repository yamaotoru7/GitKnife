using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// File > Clone...。題名 "Clone" / "Clone a remote repository into a local folder"、
/// RepositoryUrlTextBox、TestButton "Test Connection"、LocationComboBox（This PC／WSL／SSH。Open Repository と同じ）、
/// ParentDirectoryTextBox + フォルダ選択（This PC）かフォルダの一覧（WSL と SSH）、RepositoryNameTextBox、Submit "Clone"。
/// `git clone --recurse-submodules &lt;url&gt; &lt;親フォルダ\名前&gt; --progress --verbose` を、親フォルダの接続先の git で動かす。終わったら新しいタブで開く。
/// Test Connection は `ls-remote --heads &lt;url&gt;`。HTTPS / SSH の切り替えとアカウントは未実装。
/// </summary>
public sealed partial class CloneDialogViewModel : HostFolderDialogViewModel
{
    private bool _nameEdited;
    private bool _settingName;

    /// <param name="parentFolder">This PC のときの親フォルダの既定（末尾に "\" を付けて出す："D:\Proj\"）。</param>
    public CloneDialogViewModel(AppSettings settings, Func<string, GitProcess> gitFor, string parentFolder)
        : base(settings, gitFor, settings.CloneLocation,
            parentFolder.Length > 0 && !Path.EndsInDirectorySeparator(parentFolder) ? parentFolder + Path.DirectorySeparatorChar : parentFolder)
    {
        // クリップボードにリモートの URL があれば先に入れる（名前も URL から決まる。#14）
        Url = RemoteEditDialogViewModel.ClipboardRemoteUrl();
    }

    /// <summary>Test Connection を押せる（URL が空でない）。</summary>
    public bool HasUrl => Url.Trim().Length > 0;

    public override string Title => "Clone";

    public override double DialogWidth => 590;
    public override string Subtitle => "Clone a remote repository into a local folder";
    public override string SubmitText => "Clone";

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow), nameof(HasUrl))] private string _url = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _name = "";
    [ObservableProperty] private string _connectionStatus = "";

    /// <summary>できあがるフォルダの Windows の形（閉じた後でタブを開くのに使う。WSL と SSH は \\wsl.localhost\…、\\gitknife-ssh\…）。</summary>
    public string TargetPath => IsLocal
        ? Path.Combine(FolderPath.Trim(), Name.Trim())
        : SelectedLocation.Host.BrowsePath(FolderPath.Trim().TrimEnd('/') + "/" + Name.Trim());

    /// <summary>親フォルダは clone のときに作るので、無くても警告しない。</summary>
    protected override string? MissingFolderMessage => null;

    // WSL と SSH は接続先に聞くことになるので、既にあるかは Clone を押してから確かめる
    public override bool CanSubmit => Url.Trim().Length > 0 && FolderPathIsValid && Name.Trim().Length > 0 && (IsRemote || !Directory.Exists(TargetPath));

    // URL の最後の部分から名前を決める（自分で名前を変えたらそれ以降は触らない）
    partial void OnUrlChanged(string value)
    {
        ConnectionStatus = "";
        if (_nameEdited) return;
        var last = value.Trim().TrimEnd('/', '\\').Split('/', '\\', ':').LastOrDefault() ?? "";
        if (last.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) last = last[..^4];
        _settingName = true;
        Name = last;
        _settingName = false;
    }

    partial void OnNameChanged(string value)
    {
        if (!_settingName) _nameEdited = true;
    }

    /// <summary>Test Connection。</summary>
    public async Task TestConnectionAsync()
    {
        ConnectionStatus = "Testing...";
        // 接続の確かめは clone する先と同じ git で（WSL・SSH の中の鍵や資格情報を使う）
        var parent = LocalFolder;
        var fallback = IsLocal ? Environment.CurrentDirectory : SelectedLocation.Host.BrowsePath("/");
        var dir = FolderPathIsValid && await Task.Run(() => HostDirectory.Exists(parent)) ? parent : fallback;
        var r = await GitFor(dir).RunAsync(new GitRequest(dir, ["ls-remote", "--heads", Url.Trim()]));
        ConnectionStatus = r.Succeeded ? "Connection successful" : FirstLine(r.StandardError);
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var parent = LocalFolder;
        var target = TargetPath;
        if (IsRemote && await Task.Run(() => HostDirectory.Exists(target), ct)) throw new GitErrorMessageException($"Folder already exists: {FolderPath.Trim().TrimEnd('/')}/{Name.Trim()}");
        await Task.Run(() => HostDirectory.CreateDirectory(parent), ct);
        var r = await GitFor(parent).RunAsync(new GitRequest(parent, ["clone", "--recurse-submodules", Url.Trim(), target, "--progress", "--verbose"]) { OnProgress = Progress }, ct);
        r.ThrowIfFailed();
        Settings.CloneLocation = SelectedLocation.Label;
    }
}

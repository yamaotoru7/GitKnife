using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// File > Clone...。題名 "Clone" / "Clone a remote repository into a local folder"、
/// RepositoryUrlTextBox、TestButton "Test Connection"、ParentDirectoryTextBox + フォルダ選択、RepositoryNameTextBox、Submit "Clone"。
/// `git clone --recurse-submodules &lt;url&gt; &lt;親フォルダ\名前&gt; --progress --verbose`。終わったら新しいタブで開く。
/// Test Connection の git コマンドはトレースに出ない（GitKnife は `ls-remote --heads &lt;url&gt;`）。HTTPS / SSH の切り替えとアカウントは未実装。
/// </summary>
public sealed partial class CloneDialogViewModel : DialogViewModel
{
    /// <summary>パスに合った接続先の git（WSL のフォルダへの clone はその WSL の git で。T1301）。</summary>
    private readonly Func<string, GitProcess> _gitFor;
    private bool _nameEdited;
    private bool _settingName;

    public CloneDialogViewModel(Func<string, GitProcess> gitFor, string parentFolder)
    {
        _gitFor = gitFor;
        // 末尾に "\" を付けて出す（"D:\Proj\"）
        _parentFolder = parentFolder.Length > 0 && !Path.EndsInDirectorySeparator(parentFolder) ? parentFolder + Path.DirectorySeparatorChar : parentFolder;
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
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _parentFolder;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _name = "";
    [ObservableProperty] private string _connectionStatus = "";

    /// <summary>できあがるフォルダ（閉じた後でタブを開くのに使う）。</summary>
    public string TargetPath => Path.Combine(ParentFolder.Trim(), Name.Trim());

    public override bool CanSubmit => Url.Trim().Length > 0 && ParentFolder.Trim().Length > 0 && Name.Trim().Length > 0 && !Core.Git.HostDirectory.Exists(TargetPath);

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
        // 接続の確かめは clone する先と同じ git で（WSL の中の鍵や資格情報を使う）
        var parent = ParentFolder.Trim();
        var dir = Core.Git.HostDirectory.Exists(parent) ? parent : Environment.CurrentDirectory;
        var r = await _gitFor(dir).RunAsync(new GitRequest(dir, ["ls-remote", "--heads", Url.Trim()]));
        ConnectionStatus = r.Succeeded ? "Connection successful" : FirstLine(r.StandardError);
    }

    private static string FirstLine(string s) => s.Replace("\r", "").Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "Connection failed";

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var parent = ParentFolder.Trim();
        Core.Git.HostDirectory.CreateDirectory(parent);
        var r = await _gitFor(parent).RunAsync(new GitRequest(parent, ["clone", "--recurse-submodules", Url.Trim(), TargetPath, "--progress", "--verbose"]) { OnProgress = Progress }, ct);
        r.ThrowIfFailed();
    }
}

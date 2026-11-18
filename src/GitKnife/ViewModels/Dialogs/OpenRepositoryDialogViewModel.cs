using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// File > Open Repository...（Ctrl+O）。WSL と SSH も同じ入口から開くので、フォルダ選択の前に場所を選ぶ（A23）。
/// This PC はシステムのフォルダ選択（Browse...。選んだらそのまま開く）かパスの入力、WSL と SSH はパスの入力とその下のフォルダの一覧。
/// 開く前に `git rev-parse --show-toplevel` で確かめ、リポジトリの一番上を開く。リポジトリでなければ init を聞く（呼んだ側）。
/// </summary>
public sealed class OpenRepositoryDialogViewModel(AppSettings settings, Func<string, GitProcess> gitFor, string sourceFolder)
    : HostFolderDialogViewModel(settings, gitFor, settings.OpenRepositoryLocation, "")
{
    public override string Title => "Open Repository";
    public override string Subtitle => "Choose where the repository is and open it";
    public override string SubmitText => "Open";

    /// <summary>開くパス（呼んだ側が OpenAsync する）。</summary>
    public string? OpenedPath { get; private set; }
    /// <summary>false なら <see cref="OpenedPath"/> はリポジトリではない（init するか聞く）。</summary>
    public bool IsRepository { get; private set; }

    /// <summary>システムのフォルダ選択の最初の場所：入っているパス、無ければ Source Code Folder の先頭。</summary>
    public override string BrowseStart => Directory.Exists(FolderPath.Trim()) ? FolderPath.Trim() : sourceFolder;

    public override bool CanSubmit => FolderPathIsValid;

    public override async Task SubmitAsync(CancellationToken ct)
    {
        var local = LocalFolder;
        if (IsLocal && !Directory.Exists(local)) throw new GitErrorMessageException($"Folder not found: {local}");
        var git = GitFor(local);
        var r = await git.RunAsync(new GitRequest(local, ["rev-parse", "--show-toplevel"]), ct);
        // LC_ALL=C.UTF-8 で動かすので文言は英語
        IsRepository = r.Succeeded || !r.StandardError.Contains("not a git repository", StringComparison.OrdinalIgnoreCase);
        if (IsRepository) r.ThrowIfFailed();
        OpenedPath = IsRepository ? Path.GetFullPath(git.Host.ToLocalPath(r.StandardOutput.Trim())) : local;
        Settings.OpenRepositoryLocation = SelectedLocation.Label;
    }
}

/// <summary>
/// Open Repository の New Host...：SSH のホストを足す（登録の中身は Preferences > Hosts と同じ。変更と削除はそちら）。
/// 認証は鍵、ssh-agent、パスワード（接続するときに聞き、資格情報マネージャーに保存できる。T1305）。Test Connection は任意で、失敗していても足せる。
/// </summary>
public sealed partial class AddSshHostDialogViewModel(AppSettings settings, GitExecutable git) : DialogViewModel
{
    public override string Title => "New SSH Host";
    public override string Subtitle => "Connects with an SSH key, ssh-agent or a password (asked when connecting)";
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

using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Preferences（File > Preferences...、Ctrl+,）。
/// タブ General / Commit / Git / Integration / Custom Commands / Updates。Submit は無く、変えたらすぐ settings に入る（Close で閉じるだけ）。
/// global の user.name / user.email は閉じるときに `git config --global` で書く（変えたときだけ）。
/// </summary>
public sealed partial class PreferencesViewModel : DialogViewModel
{
    private readonly AppSettings _s;
    private readonly GitProcess _git;
    private string _loadedName = "";
    private string _loadedEmail = "";
    private bool _loadingInstances;

    /// <summary>Custom Commands タブ（グローバルの custom-commands.json。閉じたら保存）。</summary>
    public CustomCommandsEditorViewModel CustomCommands { get; } = new(null, null);

    public PreferencesViewModel(AppSettings settings, GitProcess git)
    {
        _s = settings;
        _git = git;
        // Source Code Folder は repositories.toml の source_dirs（末尾に区切りが付いた形でそのまま出す）
        foreach (var d in Core.Settings.RepositoryList.Load().SourceDirectories) SourceDirectories.Add(d);
        MergeTools = Views.ExternalTools.Catalog.Where(t => t.MergeArguments is not null).Select(t => new ExternalToolEntry(t.Name, t.Path, t.MergeArguments!)).ToList();
        DiffTools = Views.ExternalTools.Catalog.Where(t => t.DiffArguments is not null).Select(t => new ExternalToolEntry(t.Name, t.Path, t.DiffArguments!)).ToList();
        _selectedMergeTool = MergeTools.FirstOrDefault();
        _selectedDiffTool = DiffTools.FirstOrDefault();
    }

    public override string Title => "Preferences";
    public override string Subtitle => "";
    public override string SubmitText => "Close";
    public override bool ShowSubmit => false;
    public override string CancelText => "Close";
    public override bool ShowHeader => false;
    public override double DialogWidth => 574;
    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;

    // ------------------------------------------------------------------ General

    public ObservableCollection<string> SourceDirectories { get; } = [];

    public void AddSourceDirectory(string path)
    {
        path = Core.Settings.RepositoryList.WithSeparator(path);
        if (SourceDirectories.Contains(path, StringComparer.OrdinalIgnoreCase)) return;
        SourceDirectories.Add(path);
        SaveSourceDirectories();
    }

    public void RemoveSourceDirectory(string path)
    {
        SourceDirectories.Remove(path);
        SaveSourceDirectories();
    }

    private void SaveSourceDirectories()
    {
        try { Core.Settings.RepositoryList.Load().SourceDirectories = [.. SourceDirectories]; }
        catch (IOException ex) { Core.Logging.Log.Warning($"Saving repositories.toml failed: {ex.Message}"); }
    }

    public bool ShowChangeMarks { get => _s.DiffShowChangeMarks; set => _s.DiffShowChangeMarks = value; }
    public int CodeEditorFontSize { get => _s.CodeEditorFontSize; set => _s.CodeEditorFontSize = Math.Clamp(value, 6, 40); }
    public bool DisableSyntaxHighlighting { get => _s.DisableSyntaxHighlighting; set => _s.DisableSyntaxHighlighting = value; }
    public bool ShowBugtrackerLinks { get => _s.ShowBugtrackerLinks; set => _s.ShowBugtrackerLinks = value; }
    /// <summary>"Sort commits:"（RevisionSortOrder。0 = Topologically、1 = By date）。</summary>
    public string SortOrder { get => _s.RevisionSortOrder == 0 ? "Topological" : "Date"; set => _s.RevisionSortOrder = value == "Topological" ? 0 : 1; }
    public bool FetchRemotesAutomatically { get => _s.FetchRemotesAutomatically; set => _s.FetchRemotesAutomatically = value; }
    public bool FetchAllTags { get => _s.FetchAllTags; set => _s.FetchAllTags = value; }
    /// <summary>"Tab indicator for uncommitted changes"。対応する設定キーは未確認（AutomaticStatusUpdateInterval が 0 で止まると推定）。</summary>
    public bool UpdateRepoStatusAutomatically { get => _s.AutomaticStatusUpdateInterval > 0; set => _s.AutomaticStatusUpdateInterval = value ? 60 : 0; }
    public bool UpdateSubmodulesOnCheckout { get => _s.UpdateSubmodulesOnCheckout; set => _s.UpdateSubmodulesOnCheckout = value; }
    /// <summary>"Space character in branch names:"（ReferenceSpaceCharacterReplacement。ブランチ名に打った空白をこれに置き換える）。</summary>
    public IReadOnlyList<string> SpaceCharacters { get; } = ["-", "_", "."];
    public string SpaceCharacter { get => _s.ReferenceSpaceCharacterReplacement; set => _s.ReferenceSpaceCharacterReplacement = value; }
    public bool PushAutomaticallyOnCommit { get => _s.PushAutomaticallyOnCommit; set => _s.PushAutomaticallyOnCommit = value; }
    public bool CompactBranchLabels { get => _s.CompactBranchLabels; set => _s.CompactBranchLabels = value; }

    // ------------------------------------------------------------------ Commit

    public int CommitSubjectLowLimit { get => _s.CommitSubjectLowLimit; set => _s.CommitSubjectLowLimit = Math.Max(1, value); }
    public int CommitSubjectHighLimit { get => _s.CommitSubjectHighLimit; set => _s.CommitSubjectHighLimit = Math.Max(1, value); }
    /// <summary>"Spell checking:"（CommitSpellCheckingMode。0 = Disable、1 = System (ja-JP)（OS の言語）、2 = English）。</summary>
    public IReadOnlyList<string> SpellCheckingModes { get; } = ["Disable", $"System ({System.Globalization.CultureInfo.CurrentUICulture.Name})", "English"];
    public string SpellCheckingMode
    {
        get => SpellCheckingModes[Math.Clamp(_s.CommitSpellCheckingMode, 0, SpellCheckingModes.Count - 1)];
        set => _s.CommitSpellCheckingMode = Math.Max(0, SpellCheckingModes.ToList().IndexOf(value));
    }
    public int PageGuideLinePosition { get => _s.PageGuideLinePosition; set => _s.PageGuideLinePosition = Math.Max(0, value); }

    // ------------------------------------------------------------------ Git

    /// <summary>Git Instance の選択肢（開くと候補の全部に `git version` を実行して "2.50.1.windows.1 - GitKnife git instance" のように出す）。</summary>
    public ObservableCollection<GitInstanceItem> GitInstances { get; } = [];
    [ObservableProperty] private GitInstanceItem? _selectedGitInstance;
    public bool VerboseGitOutput { get => _s.VerboseGitOutput; set => _s.VerboseGitOutput = value; }
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _email = "";

    partial void OnSelectedGitInstanceChanged(GitInstanceItem? value)
    {
        if (value is null || _loadingInstances) return;
        _s.GitInstancePath = value.Path;   // 次の起動から使う
    }

    /// <summary>Git ページの中身を読む（git のバージョンと global の user.name / user.email）。</summary>
    public async Task LoadGitAsync()
    {
        _loadingInstances = true;
        try
        {
            var current = _git.Git.Path;
            foreach (var path in GitCandidates(current))
            {
                string version;
                try { version = await new GitExecutable(path).GetVersionAsync(); }
                catch { continue; }
                var kind = path.StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase) ? "GitKnife git instance" : "System git instance";
                var item = new GitInstanceItem(path, $"{version} - {kind}");
                GitInstances.Add(item);
                if (string.Equals(path, current, StringComparison.OrdinalIgnoreCase)) SelectedGitInstance = item;
            }
        }
        finally { _loadingInstances = false; }

        _loadedName = UserName = await GlobalConfigAsync("user.name");
        _loadedEmail = Email = await GlobalConfigAsync("user.email");
    }

    private async Task<string> GlobalConfigAsync(string key)
    {
        try { return (await _git.RunAsync(new GitRequest(Environment.CurrentDirectory, ["config", "--global", key]))).StandardOutput.Trim(); }
        catch (GitException) { return ""; }
    }

    /// <summary>閉じるときに global の user.name / user.email を書く（変えたときだけ）。</summary>
    public async Task SaveGitAsync()
    {
        if (UserName.Trim() != _loadedName && UserName.Trim().Length > 0)
            await _git.RunAsync(new GitRequest(Environment.CurrentDirectory, ["config", "--global", "user.name", UserName.Trim()]));
        if (Email.Trim() != _loadedEmail && Email.Trim().Length > 0)
            await _git.RunAsync(new GitRequest(Environment.CurrentDirectory, ["config", "--global", "user.email", Email.Trim()]));
    }

    private static IEnumerable<string> GitCandidates(string current)
    {
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new[]
        {
            current,
            Path.Combine(AppContext.BaseDirectory, "git", "cmd", "git.exe"),
            Path.Combine(pf, "Git", "cmd", "git.exe"),
            Path.Combine(local, "Programs", "Git", "cmd", "git.exe"),
        }.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ Integration

    public IReadOnlyList<ExternalToolEntry> MergeTools { get; }
    public IReadOnlyList<ExternalToolEntry> DiffTools { get; }
    [ObservableProperty] private ExternalToolEntry? _selectedMergeTool;
    [ObservableProperty] private ExternalToolEntry? _selectedDiffTool;
    public IReadOnlyList<string> Shells { get; } = ["Default", "Windows Terminal", "Command Prompt", "PowerShell", "Git Bash"];
    public string Shell { get => _s.ShellTool?.Type is { Length: > 0 } t ? t : "Default"; set => (_s.ShellTool ??= new()).Type = value; }
    public string ShellPath { get => _s.ShellTool?.ApplicationPath ?? ""; set => (_s.ShellTool ??= new()).ApplicationPath = value; }
    public string ShellArguments { get => _s.ShellTool?.Arguments ?? ""; set => (_s.ShellTool ??= new()).Arguments = value; }

    // ------------------------------------------------------------------ Hosts（GitKnife だけ。T1301）

    /// <summary>登録した SSH のホスト（settings.json の SshHosts をそのまま書き換える）。</summary>
    public ObservableCollection<SshHostEntry> SshHosts => _sshHosts ??= new(_s.SshHosts.Select(h => new SshHostEntry(h)));
    private ObservableCollection<SshHostEntry>? _sshHosts;
    [ObservableProperty] private SshHostEntry? _selectedSshHost;
    [ObservableProperty] private string _sshTestStatus = "";
    /// <summary>この PC の WSL（`wsl -l -q`）。パスで決まるので登録は要らない。</summary>
    [ObservableProperty] private string _wslDistributions = "";

    partial void OnSelectedSshHostChanged(SshHostEntry? value) => SshTestStatus = "";

    public void AddSshHost()
    {
        var name = "host";
        for (var i = 2; SshHosts.Any(h => string.Equals(h.Name, name, StringComparison.OrdinalIgnoreCase)); i++) name = "host" + i;
        var s = new SshHostSettings { Name = name };
        _s.SshHosts.Add(s);
        var e = new SshHostEntry(s);
        SshHosts.Add(e);
        SelectedSshHost = e;
    }

    public void RemoveSshHost()
    {
        if (SelectedSshHost is not { } e) return;
        _s.SshHosts.Remove(e.Settings);
        SshHosts.Remove(e);
        SelectedSshHost = SshHosts.FirstOrDefault();
    }

    /// <summary>Test Connection：ssh で入って git version を動かす。</summary>
    public async Task TestSshHostAsync()
    {
        if (SelectedSshHost is not { } e) return;
        SshTestStatus = "Testing...";
        SshTestStatus = (await SshHostEntry.TestAsync(_git.Git, e.Settings.ToHost())).Text;
    }

    public async Task LoadWslAsync()
    {
        if (!System.IO.File.Exists(WslGitHost.WslExe)) { WslDistributions = "(WSL is not installed)"; return; }
        var names = await Task.Run(WslGitHost.Distributions);
        WslDistributions = names.Count > 0 ? string.Join(", ", names) : "(none)";
    }

    // ------------------------------------------------------------------ Updates

    /// <summary>"Automatic Updates:"（ApplicationUpdateType。0 = "Develop Channel"）。GitKnife に自動更新は無い。</summary>
    public IReadOnlyList<string> UpdateTypes { get; } = ["Develop Channel", "Stable Channel", "Disabled"];
    public string UpdateType
    {
        get => UpdateTypes[Math.Clamp(_s.ApplicationUpdateType, 0, UpdateTypes.Count - 1)];
        set => _s.ApplicationUpdateType = Math.Max(0, UpdateTypes.ToList().IndexOf(value));
    }
}

/// <summary>Hosts の 1 行（SshHostSettings をそのまま書き換える）。名前はパス \\gitknife-ssh\&lt;名前&gt;\… に入るので / と \ を除く。</summary>
public sealed class SshHostEntry(SshHostSettings settings) : ObservableObject
{
    public SshHostSettings Settings { get; } = settings;

    /// <summary>Test Connection：ssh で入って git version を動かす。</summary>
    public static async Task<(bool Ok, string Text)> TestAsync(GitExecutable git, SshGitHost host)
    {
        var r = await new GitProcess(git, host).RunAsync(new GitRequest(host.ToLocalPath("/"), ["version"]));
        return r.Succeeded
            ? (true, $"Connection successful ({r.StandardOutput.Trim()})")
            : (false, r.StandardError.Trim().Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? $"Failed (exit {r.ExitCode})");
    }

    public string Name
    {
        get => Settings.Name;
        set { Settings.Name = SshHostSettings.CleanName(value); OnPropertyChanged(); OnPropertyChanged(nameof(Label)); }
    }
    public string Destination { get => Settings.Destination; set { Settings.Destination = value.Trim(); OnPropertyChanged(); OnPropertyChanged(nameof(Label)); } }
    public string GitPath { get => Settings.GitPath; set { Settings.GitPath = value.Trim(); OnPropertyChanged(); } }
    public string Label => Settings.Destination.Length > 0 && Settings.Destination != Settings.Name ? $"{Settings.Name}  ({Settings.Destination})" : Settings.Name;
    public override string ToString() => Label;
}

public sealed record GitInstanceItem(string Path, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Integration の外部ツールの 1 行（名前、場所、引数）。</summary>
public sealed partial class ExternalToolEntry(string name, string path, string arguments) : ObservableObject
{
    public string Name { get; } = name;
    [ObservableProperty] private string _path = path;
    [ObservableProperty] private string _arguments = arguments;
    /// <summary>一覧の行のアイコン（入っていれば exe のアイコン）。</summary>
    public System.Windows.Media.ImageSource? Icon => Controls.ShellIconConverter.ForFile(Path);
    public override string ToString() => Name;
}

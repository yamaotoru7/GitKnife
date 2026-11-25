using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Repository Settings（Repository > Settings for This Repository...）。
/// タブ General / Commit / Issue Tracker / Custom Commands。Submit は無く Close だけ。
/// - "Use global git credentials" をオン → `git config --local --unset user.name` と `--unset user.email`。オフ → 今の名前とメールを --local に書く（推定）
/// - "Use global git configuration file" をオフ → `git config --local commit.template <gitdir>\commit_msg_template.txt` → `git config --local commit.template`
/// - Tab Width、No fast-forward、Signed-off-by は .git/gitknife/config.toml（tab_width、lean_branching_no_fast_forward、sign_off）
/// 名前・メール・テンプレートの本文は閉じるときに書く（打つたびには git を呼ばない）。
/// </summary>
public sealed partial class RepositorySettingsViewModel : DialogViewModel
{
    public const string TemplateFileName = "commit_msg_template.txt";

    private readonly RepositoryViewModel _repo;
    private readonly RepositoryConfig _config;
    private string _loadedName = "";
    private string _loadedEmail = "";
    private string _loadedTemplate = "";
    private bool _loading = true;

    /// <summary>Custom Commands タブ（Local / Shared。閉じたら保存）。</summary>
    public CustomCommandsEditorViewModel CustomCommands { get; }
    public IssueTrackerEditorViewModel IssueRules { get; }

    /// <summary>開く前に読む設定（config.toml、カスタムコマンド、Issue Tracker のルール）。</summary>
    public sealed record Loaded(RepositoryConfig Config, CustomCommandsEditorViewModel.RepositoryCommands Commands, IReadOnlyList<IssueTrackerRule> Rules);

    /// <summary>設定を読んでから作る（SSH の先では往復があるので、読む間は UI のスレッドを止めず <see cref="Busy"/> を出す）。</summary>
    public static async Task<RepositorySettingsViewModel> CreateAsync(RepositoryViewModel repo)
    {
        var path = repo.Path;
        var common = repo.CommonGitDirectory ?? Path.Combine(repo.Path, ".git");
        var loaded = await Busy.IoAsync(null, "Loading repository settings...", () => new Loaded(
            RepositoryConfig.Load(common),
            CustomCommandsEditorViewModel.LoadRepository(path, common),
            IssueTracker.Load(common, path)));
        return new RepositorySettingsViewModel(repo, loaded);
    }

    private RepositorySettingsViewModel(RepositoryViewModel repo, Loaded loaded)
    {
        _repo = repo;
        var common = repo.CommonGitDirectory ?? Path.Combine(repo.Path, ".git");
        _config = loaded.Config;
        CustomCommands = new CustomCommandsEditorViewModel(repo.Path, loaded.Commands);
        IssueRules = new IssueTrackerEditorViewModel(repo.Path, common, loaded.Rules);
        _config.EnsureDefaults();
        _tabWidth = _config.TabWidth;
        _noFastForward = _config.LeanBranchingNoFastForward;
        _signOff = _config.SignOff;
        _showBugtrackerLinks = _config.ShowBugtrackerLinks;
        // 既定のメインブランチ：main、なければ master（表示は "default (main)"）
        LocalBranches = repo.References?.References.Where(r => r.Kind == Core.Git.Model.ReferenceKind.LocalBranch).Select(r => r.ShortName).ToList() ?? [];
        DefaultMainBranchText = $"default ({RepositoryViewModel.DefaultMainBranch(LocalBranches)})";
        _mainBranch = _config.LeanBranchingMainBranch;
    }

    public IReadOnlyList<string> LocalBranches { get; }
    public string DefaultMainBranchText { get; }

    /// <summary>選んだメインブランチ（null は既定）。</summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(MainBranchText))] private string? _mainBranch;
    partial void OnMainBranchChanged(string? value) => SaveConfig(c => c.LeanBranchingMainBranch = value);

    public override string Title => "Repository Settings";
    public override string Subtitle => "";
    public override string SubmitText => "Close";
    public override bool ShowSubmit => false;
    public override string CancelText => "Close";
    public override bool ShowHeader => false;
    public override double DialogWidth => 496;
    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;

    // ------------------------------------------------------------------ General

    [ObservableProperty] private bool _useGlobalCredentials;
    [ObservableProperty] private string _userName = "";
    [ObservableProperty] private string _email = "";
    /// <summary>Lean Branching の "Main Branch:" の表示（既定なら "default (main)"）。</summary>
    public string MainBranchText => MainBranch ?? DefaultMainBranchText;
    [ObservableProperty] private bool _noFastForward;
    [ObservableProperty] private int _tabWidth;

    async partial void OnUseGlobalCredentialsChanged(bool value)
    {
        if (_loading) return;
        try
        {
            if (value)
            {
                await UnsetAsync("user.name");
                await UnsetAsync("user.email");
                UserName = await ConfigAsync("user.name");
                Email = await ConfigAsync("user.email");
            }
            else
            {
                if (UserName.Trim().Length > 0) await _repo.RunGitAsync(["config", "--local", "user.name", UserName.Trim()]);
                if (Email.Trim().Length > 0) await _repo.RunGitAsync(["config", "--local", "user.email", Email.Trim()]);
            }
            _loadedName = UserName;
            _loadedEmail = Email;
        }
        catch (GitException ex) { Views.Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
    }

    partial void OnNoFastForwardChanged(bool value) => SaveConfig(c => c.LeanBranchingNoFastForward = value);
    partial void OnTabWidthChanged(int value) => SaveConfig(c => c.TabWidth = Math.Clamp(value, 1, 16));

    // ------------------------------------------------------------------ Commit

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanEditTemplate))] private bool _useGlobalTemplate = true;
    [ObservableProperty] private string _templatePath = "";
    [ObservableProperty] private string _templateText = "";
    [ObservableProperty] private bool _signOff;
    public bool CanEditTemplate => !UseGlobalTemplate;

    async partial void OnUseGlobalTemplateChanged(bool value)
    {
        if (_loading) return;
        try
        {
            if (value)
            {
                await UnsetAsync("commit.template");
                await LoadTemplateAsync(await ConfigAsync("commit.template"));
            }
            else
            {
                var path = Path.Combine(_repo.GitDirectory ?? Path.Combine(_repo.Path, ".git"), TemplateFileName);
                await _repo.RunGitAsync(["config", "--local", "commit.template", path]);
                await LoadTemplateAsync(await ConfigAsync("commit.template", local: true));
            }
        }
        catch (GitException ex) { Views.Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
    }

    partial void OnSignOffChanged(bool value) => SaveConfig(c => c.SignOff = value);

    // ------------------------------------------------------------------ Issue Tracker

    [ObservableProperty] private bool _showBugtrackerLinks;
    partial void OnShowBugtrackerLinksChanged(bool value) => SaveConfig(c => c.ShowBugtrackerLinks = value);

    // ------------------------------------------------------------------ 読み書き

    /// <summary>開いたときに git config を読む（--local の有無で "Use global" のチェックを決める）。</summary>
    public async Task LoadAsync()
    {
        try
        {
            var localName = await ConfigAsync("user.name", local: true);
            var localEmail = await ConfigAsync("user.email", local: true);
            UseGlobalCredentials = localName.Length == 0 && localEmail.Length == 0;
            _loadedName = UserName = await ConfigAsync("user.name");
            _loadedEmail = Email = await ConfigAsync("user.email");

            var localTemplate = await ConfigAsync("commit.template", local: true);
            UseGlobalTemplate = localTemplate.Length == 0;
            await LoadTemplateAsync(localTemplate.Length > 0 ? localTemplate : await ConfigAsync("commit.template"));
            _loadedTemplate = TemplateText;
        }
        finally { _loading = false; }
    }

    /// <summary>閉じるとき：--local の名前・メールと、自分のテンプレートの本文を書く（変えたときだけ）。</summary>
    public async Task SaveAsync()
    {
        if (!UseGlobalCredentials)
        {
            if (UserName.Trim() != _loadedName.Trim() && UserName.Trim().Length > 0) await _repo.RunGitAsync(["config", "--local", "user.name", UserName.Trim()]);
            if (Email.Trim() != _loadedEmail.Trim() && Email.Trim().Length > 0) await _repo.RunGitAsync(["config", "--local", "user.email", Email.Trim()]);
        }
        if (!UseGlobalTemplate && TemplatePath.Length > 0 && TemplateText != _loadedTemplate)
        {
            var path = TemplatePath;
            var text = TemplateText.Replace("\r\n", "\n");
            await HostIO.RunAsync(() =>
            {
                HostDirectory.CreateDirectory(Path.GetDirectoryName(path)!);
                HostFile.WriteAllText(path, text);
            });
        }
    }

    private async Task LoadTemplateAsync(string path)
    {
        TemplatePath = path.Replace('\\', '/');
        var full = path.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[1..].TrimStart('/', '\\')) : path;
        TemplateText = full.Length == 0 ? "" : await HostIO.RunAsync(() => HostFile.Exists(full) ? HostFile.ReadAllText(full) : "");
    }

    private async Task<string> ConfigAsync(string key, bool local = false)
    {
        try { return (await _repo.RunGitAsync(local ? ["config", "--local", key] : ["config", key])).StandardOutput.Trim(); }
        catch (GitException) { return ""; }   // 無いキーは exit 1
    }

    private async Task UnsetAsync(string key)
    {
        try { await _repo.RunGitAsync(["config", "--local", "--unset", key]); }
        catch (GitException ex) when (ex.Result.ExitCode == 5) { }   // もともと無い
    }

    /// <summary>config.toml を変えて書く（UI のスレッドの外で、順に）。</summary>
    private void SaveConfig(Action<RepositoryConfig> change)
    {
        _configSaves = _configSaves.ContinueWith(_ =>
        {
            try
            {
                change(_config);
                _config.Save();
            }
            catch (IOException ex) { Core.Logging.Log.Warning($"Saving repository config failed: {ex.Message}"); }
        }, TaskScheduler.Default);
    }
    private Task _configSaves = Task.CompletedTask;

    /// <summary>並んでいる config.toml の書き込みが終わるまで待つ。</summary>
    public Task FlushConfigAsync() => _configSaves;
}

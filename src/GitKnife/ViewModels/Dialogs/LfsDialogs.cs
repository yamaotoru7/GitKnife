using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Git LFS > Add Track Pattern...。"Add tracking patterns to Git LFS" / "Add file path patterns to .gitattributes"、
/// PatternTextBox（1 行に 1 つ）、PreviewTextBox と "0 files match"、Submit "Track" → `lfs track &lt;pattern&gt;...`。
/// プレビューは GitKnife の選択（`ls-files -z --cached --others --exclude-standard -- &lt;:(glob)pattern&gt;`）。
/// </summary>
public sealed partial class LfsTrackDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private CancellationTokenSource? _cts;

    public LfsTrackDialogViewModel(RepositoryViewModel repo)
    {
        _repo = repo;
        _previewLabel = "0 files match";
    }

    public override string Title => "Add tracking patterns to Git LFS";
    public override string Subtitle => "Add file path patterns to .gitattributes";
    public override string SubmitText => "Track";
    public override double DialogWidth => 640;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _pattern = "";
    [ObservableProperty] private string _preview = "";
    [ObservableProperty] private string _previewLabel;

    private List<string> Patterns => Pattern.Replace("\r\n", "\n").Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
    public override bool CanSubmit => Patterns.Count > 0;

    partial void OnPatternChanged(string value) => _ = UpdatePreviewAsync();

    private async Task UpdatePreviewAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var patterns = Patterns;
        if (patterns.Count == 0) { Preview = ""; PreviewLabel = "0 files match"; return; }
        try
        {
            await Task.Delay(150, cts.Token);
            string[] args = ["ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", .. patterns.Select(p => ":(glob)" + (p.Contains('/') ? p : "**/" + p))];
            var r = await _repo.RunGitAsync(args, ct: cts.Token);
            var files = r.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            Preview = string.Join(Environment.NewLine, files);
            PreviewLabel = files.Length == 1 ? "1 file matches" : $"{files.Length} files match";
        }
        catch (OperationCanceledException) { }
        catch (Core.Git.GitException) { Preview = ""; PreviewLabel = "0 files match"; }
    }

    public override Task SubmitAsync(CancellationToken ct) => _repo.RunGitAsync(["lfs", "track", .. Patterns], Progress, ct);
}

/// <summary>
/// Git LFS > Fetch... / Pull...。"Fetch" / "Download Git LFS objects from the specified remotes"、
/// "Pull" / "Download Git LFS objects for the currently checked out ref, and update the working directory with the downloaded content if required"、
/// RemotesComboBox → `lfs fetch &lt;remote&gt;` / `lfs pull &lt;remote&gt;`。
/// </summary>
public sealed partial class LfsRemoteDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private readonly bool _pull;

    public LfsRemoteDialogViewModel(RepositoryViewModel repo, bool pull)
    {
        _repo = repo;
        _pull = pull;
        Remotes = repo.References?.Remotes.Select(r => r.Name).ToList() ?? [];
        _selectedRemote = Remotes.FirstOrDefault(r => r == "origin") ?? Remotes.FirstOrDefault();
    }

    public override string Title => _pull ? "Pull" : "Fetch";
    public override string Subtitle => _pull
        ? "Download Git LFS objects for the currently checked out ref, and update the working directory with the downloaded content if required"
        : "Download Git LFS objects from the specified remotes";
    public override string SubmitText => _pull ? "Pull" : "Fetch";
    public IReadOnlyList<string> Remotes { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string? _selectedRemote;
    public override bool CanSubmit => SelectedRemote is not null;

    public override Task SubmitAsync(CancellationToken ct) => _repo.RunGitAsync(["lfs", _pull ? "pull" : "fetch", SelectedRemote!], Progress, ct);
}

/// <summary>
/// Git LFS > Status (Locks)...。"LFS Files Status"、Filter、ファイルの一覧（`lfs ls-files` と `lfs locks`）、
/// 下に鍵のボタン（ロック・解除。未実装）、フッターに "Refreshing LFS locks..."、Close だけ。
/// </summary>
public sealed partial class LfsStatusDialogViewModel : DialogViewModel
{
    private readonly RepositoryViewModel _repo;
    private List<string> _all = [];

    public LfsStatusDialogViewModel(RepositoryViewModel repo)
    {
        _repo = repo;
        _ = LoadAsync();
    }

    public override string Title => "LFS Files Status";
    public override string Subtitle => "";
    public override string SubmitText => "Close";
    public override bool ShowSubmit => false;
    public override string CancelText => "Close";
    public override double DialogWidth => 700;
    public ObservableCollection<string> Files { get; } = [];
    [ObservableProperty] private string _filter = "";

    partial void OnFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        Files.Clear();
        foreach (var f in _all.Where(f => Filter.Length == 0 || f.Contains(Filter, StringComparison.OrdinalIgnoreCase))) Files.Add(f);
    }

    private async Task LoadAsync()
    {
        IsBusy = true;
        StatusMessage = "Refreshing LFS locks...";
        try
        {
            var files = await _repo.RunGitAsync(["lfs", "ls-files"]);
            var locks = await _repo.RunGitAsync(["lfs", "locks"]);
            var locked = locks.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Split('\t')[0].Trim()).ToHashSet();
            // `lfs ls-files` の行は "&lt;oid&gt; * &lt;path&gt;" か "&lt;oid&gt; - &lt;path&gt;"
            _all = files.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Select(l => l.Length > 13 ? l[13..].TrimStart('*', '-', ' ') : l)
                .Select(p => locked.Contains(p) ? p + "  (locked)" : p).ToList();
            ApplyFilter();
            StatusMessage = null;
        }
        catch (Core.Git.GitException ex) { StatusMessage = ex.Result.StandardError.Trim().Split('\n')[0]; }
        finally { IsBusy = false; }
    }

    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

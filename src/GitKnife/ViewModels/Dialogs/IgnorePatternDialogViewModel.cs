using CommunityToolkit.Mvvm.ComponentModel;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// "Add Pattern to .gitignore"（Ignore > Custom Pattern... / Ignore All .txt Files... / Ignore all files in '...'...）。
/// PatternTextBox（1 行に 1 つ。最初は "notes.txt" や "*.txt"）、PreviewTextBox（当てはまるファイル）と PreviewLabelTextBlock "1 file matches"、Submit "Add to .gitignore"。
/// プレビューは `ls-files --others --ignored --exclude=&lt;pattern&gt;`（untracked）で出す。
/// 追加は .gitignore の末尾に足すだけ。既に追跡しているファイルを外す処理は未実装。
/// </summary>
public sealed partial class IgnorePatternDialogViewModel : DialogViewModel
{
    private readonly LocalChangesViewModel _local;
    private CancellationTokenSource? _cts;

    public IgnorePatternDialogViewModel(LocalChangesViewModel local, string pattern)
    {
        _local = local;
        _pattern = pattern;
        _ = UpdatePreviewAsync();
    }

    public override string Title => "Add Pattern to .gitignore";
    public override string Subtitle => "A gitignore file specifies intentionally untracked files that Git should ignore. Files already tracked by Git will be untracked.";
    public override string SubmitText => "Add to .gitignore";
    public override double DialogWidth => 670;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanSubmit), nameof(CanSubmitNow))] private string _pattern;
    [ObservableProperty] private string _preview = "";
    [ObservableProperty] private string _previewLabel = "";

    private IEnumerable<string> Patterns => Pattern.Replace("\r\n", "\n").Split('\n').Select(p => p.Trim()).Where(p => p.Length > 0 && !p.StartsWith('#'));
    public override bool CanSubmit => Patterns.Any();

    partial void OnPatternChanged(string value) => _ = UpdatePreviewAsync();

    private async Task UpdatePreviewAsync()
    {
        _cts?.Cancel();
        var cts = _cts = new CancellationTokenSource();
        var patterns = Patterns.ToList();
        if (patterns.Count == 0) { Preview = ""; PreviewLabel = "0 files match"; return; }
        try
        {
            await Task.Delay(150, cts.Token);
            string[] args = ["ls-files", "-z", "--others", "--ignored", .. patterns.Select(p => "--exclude=" + p)];
            var r = await _local.Repository.RunGitAsync(args, ct: cts.Token);
            if (cts.IsCancellationRequested) return;
            var files = r.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            Preview = string.Join(Environment.NewLine, files);
            PreviewLabel = files.Length == 1 ? "1 file matches" : $"{files.Length} files match";
        }
        catch (OperationCanceledException) { }
        catch (Core.Git.GitException) { Preview = ""; PreviewLabel = ""; }
    }

    public override async Task SubmitAsync(CancellationToken ct)
    {
        foreach (var p in Patterns.ToList()) await _local.IgnoreAsync(p);
    }
}

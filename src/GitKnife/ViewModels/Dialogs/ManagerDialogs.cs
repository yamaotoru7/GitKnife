using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Repository Manager > Rescan Repositories...。題名 "Rescan repositories in source folder"、
/// "Do you want to rescan repositories in "D:\Proj\"?"、ResetDeletedCheckbox "Reset previously deleted repository entries"、Submit "Rescan"。
/// 見つけたものを repositories.toml に足す。Remove したもの（ignore）は足さず、チェックを付けると ignore を空にしてから足す（推定）。
/// </summary>
public sealed partial class RescanRepositoriesDialogViewModel : DialogViewModel
{
    private readonly IReadOnlyList<string> _sources = RepositoryList.Load().SourceDirectories;
    public override string Title => "Rescan repositories in source folder";
    public override string Subtitle => $"Do you want to rescan repositories in {string.Join(", ", _sources.Select(d => "\"" + d + "\""))}?";
    public override string SubmitText => "Rescan";
    public override double DialogWidth => 570;
    [ObservableProperty] private bool _resetDeleted;
    public override bool CanSubmit => _sources.Count > 0;
    /// <summary>足したリポジトリの数（閉じた後で見る）。</summary>
    public int Added { get; private set; }

    public override Task SubmitAsync(CancellationToken ct) => Task.Run(() =>
    {
        var list = RepositoryList.Load();
        var depth = Math.Max(1, list.ScanDepth);
        var found = _sources.Where(Directory.Exists).SelectMany(d => RepositoryScanner.Scan(d, depth)).Select(f => f.Path).ToList();
        Added = list.AddScanned(found, ResetDeleted);
    }, ct);
}

/// <summary>
/// Help > About GitKnife。製品名、版、使っている git、ライセンスの注記。
/// </summary>
public sealed class AboutDialogViewModel(string gitPath, string gitVersion) : DialogViewModel
{
    public override string Title => Core.ProductInfo.Name;
    public override string Subtitle => $"Version {Core.ProductInfo.Version}";
    public override string SubmitText => "Close";
    public override bool ShowSubmit => false;
    public override string CancelText => "Close";
    public override double DialogWidth => 440;
    public string GitText => $"git {gitVersion}  ({gitPath})";
    public string Copyright => $"© {DateTime.Now.Year} {Core.ProductInfo.Name} contributors";
    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

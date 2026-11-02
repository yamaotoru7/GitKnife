using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>Benchmark の 1 行（名前、git コマンド、かかった秒）。</summary>
public sealed partial class BenchmarkItem(string name, string id, string tip, string[] args) : ObservableObject
{
    public string Name { get; } = name;
    /// <summary>秒の TextBlock の AutomationId（SystemLatencyTextBlock など）。</summary>
    public string Id { get; } = id;
    /// <summary>行に乗せたときのツールチップ（Reading commits なら "git log --oneline --all"）。</summary>
    public string Tip { get; } = tip;
    public string[] Args { get; } = args;
    [ObservableProperty] private string _seconds = "";
    public double Value { get; set; }
}

/// <summary>
/// Quick Launch > Benchmark...。題名 "Performance Benchmark" / "Measure repository performance"、フッターなし（右上の × だけ）。
/// 最初は枠の真ん中に Start（StartButton）。押すと 4 つを順に 7 回ずつ測り、行ごとの秒（緑）と SCORE を出す。
/// SCORE は 50 ÷ 4 つの合計秒。
/// </summary>
public sealed partial class BenchmarkDialogViewModel : DialogViewModel
{
    private const int Runs = 7;
    private readonly RepositoryViewModel _repo;

    public BenchmarkDialogViewModel(RepositoryViewModel repo)
    {
        _repo = repo;
        Items =
        [
            new("System latency", "SystemLatencyTextBlock", "git rev-parse HEAD", ["rev-parse", "HEAD"]),
            new("Reading changed files", "StatusTextBlock", "git status", ["--no-optional-locks", "status", "--porcelain", "-z", "--untracked-files=all"]),
            new("Reading branches and tags", "ReferencesTextBlock", "git for-each-ref",
                ["for-each-ref", "--include-root-refs", "--format=%(objectname)\t%(refname)\t%(HEAD)\t%(upstream)\t%(*objectname)\t%(committerdate:raw)\t%(*committerdate:raw)\t%(symref)"]),
            new("Reading commits", "RevisionsTextBlock", "git log --oneline --all",
                ["log", "HEAD", "--branches", "--remotes", "--tags", "--max-count=50000", "--no-show-signature", "--date-order", "--pretty=format:%H%P", "--"]),
        ];
    }

    public override string Title => "Performance Benchmark";
    public override string Subtitle => "Measure repository performance";
    public override string SubmitText => "Start";
    public override bool ShowSubmit => false;
    public override bool ShowHeader => false;
    public override bool ShowFooter => false;
    public override double DialogWidth => 480;
    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;

    public ObservableCollection<BenchmarkItem> Items { get; }
    [ObservableProperty] private bool _isStarted;
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _score = "";

    public async Task RunAsync()
    {
        if (IsRunning) return;
        IsStarted = IsRunning = true;
        Score = "";
        foreach (var item in Items) item.Seconds = "";
        foreach (var item in Items)
        {
            var times = new List<double>();
            for (var i = 0; i < Runs; i++)
            {
                var sw = Stopwatch.StartNew();
                await _repo.Git.RunAsync(new GitRequest(_repo.Path, item.Args));
                times.Add(sw.Elapsed.TotalSeconds);
            }
            times.Sort();
            item.Value = times[times.Count / 2];
            item.Seconds = $"{item.Value:0.000}s";
        }
        var total = Items.Sum(i => i.Value);
        Score = total > 0 ? $"{50 / total:0.0}" : "";
        IsRunning = false;
    }

    public string Report => string.Join(Environment.NewLine, Items.Select(i => $"{i.Name}: {i.Seconds}").Append($"Score: {Score}"));
}

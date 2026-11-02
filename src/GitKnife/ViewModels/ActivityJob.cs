using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;

namespace GitKnife.ViewModels;

public enum ActivityState { Running, Succeeded, Failed }

/// <summary>
/// Activity Manager の 1 行。名前（"Fetch all"）、結果の一言（"Already up to date"）、
/// 終わった時刻、実行した git コマンドと出力（"$ git fetch --prune --all" の行のあとに出力）。
/// </summary>
public sealed partial class ActivityJob : ObservableObject
{
    /// <summary>今の非同期の流れで動いているジョブ（ダイアログの Submit の中で git を呼ぶと、そのジョブに出力が入る）。</summary>
    private static readonly AsyncLocal<ActivityJob?> _current = new();
    public static ActivityJob? Current => _current.Value;

    private readonly StringBuilder _output = new();
    private readonly CancellationTokenSource _cts = new();

    /// <summary>ステータス欄の CancelButton で止める（fetch / pull / push の進捗とキャンセル）。ジョブの中の git はこの token で止まる。</summary>
    public CancellationToken Token => _cts.Token;
    public void Cancel() => _cts.Cancel();

    public ActivityJob(string name, bool isBackground)
    {
        Name = name;
        IsBackground = isBackground;
        StartTime = DateTime.Now;
    }

    public string Name { get; }
    public bool IsBackground { get; }
    public DateTime StartTime { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FinishTimeText), nameof(FinishText), nameof(IsRunning), nameof(IsFailed))] private ActivityState _state;
    [ObservableProperty] private string _details = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(FinishTimeText), nameof(FinishText))] private DateTime? _finishTime;

    public string OutputText => _output.ToString();
    public bool IsRunning => State == ActivityState.Running;
    public bool IsFailed => State == ActivityState.Failed;
    /// <summary>一覧の右の時刻（FinishTimeTextBlock "08:57"）。</summary>
    public string FinishTimeText => (FinishTime ?? StartTime).ToString("HH:mm");
    /// <summary>詳細の右上（JobDetailsFinishTimeTextBlock "succeeded 25 10 2026 08:57:33"）。</summary>
    public string FinishText => State switch
    {
        ActivityState.Running => "running",
        ActivityState.Succeeded => "succeeded " + FinishTime?.ToString("d M yyyy HH:mm:ss"),
        _ => "failed " + FinishTime?.ToString("d M yyyy HH:mm:ss"),
    };

    /// <summary>
    /// git の進捗の割合（"Receiving objects:  45% (9/20)" などの最後の値）。まだ無ければ null。
    /// ツールバーの真ん中の StatusProgressBar は、これがあれば割合で、無ければ左右に動く表示。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasPercent))] private double? _percent;
    public bool HasPercent => Percent is not null;

    private static readonly System.Text.RegularExpressions.Regex PercentPattern = new(@"(\d{1,3})%");

    /// <summary>stderr の進捗の 1 行から割合を拾う（段階ごとに 0〜100% をくり返す。今の段階の割合を出す）。</summary>
    public void ReportProgress(string line)
    {
        if (State != ActivityState.Running) return;
        var m = PercentPattern.Match(line);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var p) || p > 100) return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Percent = p);
    }

    /// <summary>このジョブを今の流れの「実行中」にする（Dispose で戻す）。</summary>
    public IDisposable Enter()
    {
        var previous = _current.Value;
        _current.Value = this;
        return new Scope(() => _current.Value = previous);
    }

    /// <summary>git の 1 回分を書き足す（"$ git ..." の行、標準エラー、標準出力）。</summary>
    public void Append(GitResult result)
    {
        // 終わった後（ダイアログを閉じた直後の status の読み直しなど）と、読み取りだけのコマンドは出さない（出すのは操作のコマンドだけ）
        if (State != ActivityState.Running || IsReadOnly(result.CommandLine)) return;
        if (_output.Length > 0) _output.Append('\n');
        // コマンドの行のあとに空行
        _output.Append("$ ").Append(DisplayCommand(result.CommandLine)).Append('\n').Append('\n');
        foreach (var part in new[] { result.StandardError, result.StandardOutput })
        {
            var text = part.Replace("\r\n", "\n").TrimEnd('\n');
            if (text.Length > 0) _output.Append(text).Append('\n');
        }
        OnPropertyChanged(nameof(OutputText));
    }

    public void Finish(bool succeeded, string? details)
    {
        FinishTime = DateTime.Now;
        Details = details ?? "";
        // 出力の最後に結果の一言（"Already up to date"）
        if (succeeded && Details.Length > 0 && !_output.ToString().TrimEnd().EndsWith(Details, StringComparison.Ordinal))
        {
            _output.Append(Details).Append('\n');
            OnPropertyChanged(nameof(OutputText));
        }
        State = succeeded ? ActivityState.Succeeded : ActivityState.Failed;
    }

    private static readonly string[] ReadOnlyCommands = ["status", "for-each-ref", "rev-parse", "log", "merge-base", "cat-file", "show", "diff", "diff-tree", "ls-files", "ls-tree", "merge-tree", "version"];

    /// <summary>"-c x=y" と "--no-optional-locks" などの前置きを飛ばした最初の語が読み取り系か。</summary>
    private static bool IsReadOnly(string commandLine)
    {
        var args = commandLine.Split(' ').Skip(1).ToList();   // 先頭は "git"
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == "-c") { i++; continue; }
            if (args[i].StartsWith('-')) continue;
            return ReadOnlyCommands.Contains(args[i]) || (args[i] == "config" && args.Count == i + 2);
        }
        return false;
    }

    /// <summary>表示するコマンド（"--progress" は出さない："git fetch --prune --all"）。</summary>
    private static string DisplayCommand(string commandLine)
        => string.Join(' ', commandLine.Split(' ').Where(a => a != "--progress"));

    private sealed class Scope(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Diff;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Merge Conflict Resolver のウィンドウ。左が theirs（マージしてくる側）、右が ours（今のブランチ）、下が結果。
/// 左右の競合の部分を押すと結果に入る（押した順）。全部の競合が決まると Resolve を押せる。結果は手で直してもよい（"editing allowed"）。
/// </summary>
public sealed partial class MergeResolverViewModel : DialogViewModel
{
    private readonly LocalChangesViewModel _owner;
    public MergeResolution Resolution { get; }
    public ConflictInfo Info { get; }
    public override string Title => "Merge";
    public override string Subtitle => "";
    public override string SubmitText => "Resolve";
    public override bool ShowHeader => false;
    public override double DialogWidth => 1600;

    /// <summary>"0/1"（解決した数 / 競合の数）。</summary>
    [ObservableProperty] private string _statusText = "";
    /// <summary>結果を手で編集した（以後は選択で結果を作り直さない）。</summary>
    [ObservableProperty] private bool _mergedEdited;
    /// <summary>手で直した結果のテキスト。</summary>
    public string? EditedText { get; set; }

    /// <summary>選択が変わった（ビューが 3 つのエディタを作り直す）。</summary>
    public event EventHandler? Changed;

    public MergeResolverViewModel(LocalChangesViewModel owner, ConflictInfo info, string conflictedText)
    {
        _owner = owner;
        Info = info;
        Resolution = new MergeResolution(conflictedText);
        Update();
    }

    public bool AllTheirs { get => Resolution.AllHave(MergeResolution.Side.Theirs); set { Resolution.SetAll(MergeResolution.Side.Theirs, value); Update(); } }
    public bool AllOurs { get => Resolution.AllHave(MergeResolution.Side.Ours); set { Resolution.SetAll(MergeResolution.Side.Ours, value); Update(); } }

    public void Toggle(int conflict, MergeResolution.Side side)
    {
        Resolution.Toggle(conflict, side);
        Update();
    }

    private void Update()
    {
        StatusText = $"{Resolution.ResolvedCount}/{Resolution.ConflictCount}";
        OnPropertyChanged(nameof(AllTheirs));
        OnPropertyChanged(nameof(AllOurs));
        RaiseCanSubmit();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>結果を手で直したら、未解決の印が残っていないかで判断する。</summary>
    public void SetEditedText(string text)
    {
        EditedText = text;
        MergedEdited = true;
        RaiseCanSubmit();
    }

    public override bool CanSubmit => MergedEdited ? EditedText?.Contains(MergeResolution.Placeholder) == false : Resolution.IsResolved;

    public override Task SubmitAsync(CancellationToken ct)
    {
        var text = MergedEdited ? EditedText!.Replace("\r\n", "\n").TrimEnd('\n') + "\n" : Resolution.MergedFileText();
        return _owner.SaveResolved(Info.Path, text);
    }
}

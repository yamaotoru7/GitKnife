using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Shared のカスタムコマンドを実行する前の確認。
/// `TrustThisRepositoryCheckBox` を付けて Run すると、呼んだ側が config.toml の trust_shared_commands を true にする。
/// </summary>
public sealed partial class SharedCustomCommandConfirmDialogViewModel(string repositoryName) : DialogViewModel
{
    public override string Title => $"The custom command has come from the '{repositoryName}' repository";
    public override string Subtitle => "You should only run custom commands from trustworthy repositories. Do you really want to run it?";
    public override string SubmitText => "Run";
    public override double DialogWidth => 602;
    public override bool ShowWarningBadge => true;
    public string TrustText => $"Trust custom commands in '{repositoryName}'";
    [ObservableProperty] private bool _trust;
    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// カスタムコマンドの UI モードのダイアログ。
/// 題名と説明は変数を展開済みで受け取る。ボタン 1 が SubmitButton、ボタン 2 が CancelButton。Submit のあとの動作は呼んだ側が行う。
/// </summary>
public sealed class CustomCommandUiDialogViewModel : DialogViewModel
{
    private readonly CustomCommandUi _ui;
    private readonly string _title;
    private readonly string _description;

    public CustomCommandUiDialogViewModel(CustomCommandUi ui, string title, string description, IReadOnlyList<Core.Git.Model.ReferenceInfo> references)
    {
        _ui = ui;
        _title = title;
        _description = description;
        Controls = ui.Controls.Select(c => new CustomCommandControlViewModel(c, references)).ToList();
    }

    public override string Title => _title;
    public override string Subtitle => _description;
    public override bool ShowSubmit => _ui.Buttons.Count > 0;
    public override string SubmitText => _ui.Buttons.Count > 0 ? _ui.Buttons[0].Title : "OK";
    public override string CancelText => _ui.Buttons.Count > 1 ? _ui.Buttons[1].Title : "Cancel";
    public override double DialogWidth => 550;
    public IReadOnlyList<CustomCommandControlViewModel> Controls { get; }

    /// <summary>`$1{text}` などに入れる値（コントロールの順）。</summary>
    public IReadOnlyList<string> Values => Controls.Select(c => c.Value).ToList();

    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// showOutput のコマンドの結果：題 = 名前、"名前 completed"、
/// "Details:" の下に OutputTextBox、Close。740 幅（Git Error と同じ幅だが、本文はタイトルの位置から始まる）。
/// </summary>
public sealed class CustomActionResultDialogViewModel(string name, string output) : DialogViewModel
{
    public override string Title => name;
    public override string Subtitle => $"{name} completed";
    public override string SubmitText => "Submit";
    public override bool ShowSubmit => false;
    public override string CancelText => "Close";
    public override double DialogWidth => 740;
    public string Output => output;
    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>UI ダイアログのコントロール 1 つの入力値。</summary>
public sealed partial class CustomCommandControlViewModel : ObservableObject
{
    public CustomCommandControlViewModel(CustomCommandControl control, IReadOnlyList<Core.Git.Model.ReferenceInfo> references)
    {
        Control = control;
        _text = control.TextBoxType == "filepath" ? CombinePath(control.DefaultDirectory, control.Filename) : control.Text;
        _isChecked = control.DefaultValue;
        // Branch Selector：filter に合う ref（ローカル → リモート → タグ、それぞれ名前順）。最初の行を選んでおく
        References = references.Where(r => r.Kind <= Core.Git.Model.ReferenceKind.Tag && control.MatchesFilter(r.CanonicalName))
            .OrderBy(r => r.Kind).ThenBy(r => r.ShortName, StringComparer.OrdinalIgnoreCase).Select(r => r.ShortName).ToList();
        _selectedReference = References.FirstOrDefault();
    }

    public CustomCommandControl Control { get; }
    public string Title => Control.Title;
    public bool IsText => Control.Type == "textBox" && Control.TextBoxType != "filepath";
    public bool IsFilePath => Control.Type == "textBox" && Control.TextBoxType == "filepath";
    public bool IsDropdown => Control.Type == "dropdown";
    public bool IsCheckBox => Control.Type == "checkBox";
    public IReadOnlyList<string> References { get; }

    [ObservableProperty] private string _text;
    [ObservableProperty] private bool _isChecked;
    [ObservableProperty] private string? _selectedReference;

    public string Value => Control.Type switch
    {
        "dropdown" => SelectedReference ?? "",
        "checkBox" => IsChecked ? Control.CheckedValue : Control.UncheckedValue,
        _ => Text,
    };

    private static string CombinePath(string dir, string file)
        => dir.Length == 0 ? file : file.Length == 0 ? dir : System.IO.Path.Combine(dir, file);
}

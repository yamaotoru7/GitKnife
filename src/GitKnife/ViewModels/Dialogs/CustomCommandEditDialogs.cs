using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// "Edit Action" / "Edit custom command action"。
/// `CustomCommandTypeComboBox`：Start Process / Bash Command / Open Url（UI のボタンなら Cancel も）。右に "Available variables:"。Save / Cancel。
/// </summary>
public sealed partial class EditCustomActionDialogViewModel : DialogViewModel
{
    private readonly string _target;

    public EditCustomActionDialogViewModel(CustomCommandAction action, string target, bool allowCancel)
    {
        _target = target;
        Types = allowCancel ? [.. BaseTypes, new("cancel", "Cancel", "close dialog")] : BaseTypes;
        _type = Types.Any(t => t.Id == action.Type) ? action.Type : "url";
        _url = action.Url ?? "";
        _script = action.Script ?? "";
        _path = action.Path ?? "";
        _arguments = action.Args ?? "";
        _waitForExit = action.WaitForExit;
        _showOutput = action.ShowOutput;
    }

    private static readonly Choice[] BaseTypes =
    [
        new("process", "Start Process", "start application with parameters"),
        new("sh", "Bash Command", "run bash script"),
        new("url", "Open Url", "open url in browser"),
    ];

    public override string Title => "Edit Action";
    public override string Subtitle => "Edit custom command action";
    public override string SubmitText => "Save";
    public override double DialogWidth => 860;

    public IReadOnlyList<Choice> Types { get; }
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsProcess), nameof(IsSh), nameof(IsUrl), nameof(HasRunOptions))] private string _type;
    [ObservableProperty] private string _url;
    [ObservableProperty] private string _script;
    [ObservableProperty] private string _path;
    [ObservableProperty] private string _arguments;
    [ObservableProperty] private bool _waitForExit;
    [ObservableProperty] private bool _showOutput;

    public bool IsProcess => Type == "process";
    public bool IsSh => Type == "sh";
    public bool IsUrl => Type == "url";
    public bool HasRunOptions => IsProcess || IsSh;
    public IReadOnlyList<CustomCommandVariableRow> Variables => CustomCommandHelp.Variables(_target);

    public CustomCommandAction Result => Type switch
    {
        "process" => new CustomCommandAction("process", Path: Path, Args: Arguments, ShowOutput: ShowOutput, WaitForExit: WaitForExit),
        "sh" => new CustomCommandAction("sh", Script: Script, ShowOutput: ShowOutput, WaitForExit: WaitForExit),
        "url" => new CustomCommandAction("url", Url: Url),
        _ => new CustomCommandAction(Type),
    };

    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>
/// "Edit Custom Command UI Controls"（O13 で見た入れ子の窓）：左に `ControlsListBox`（"$1"、題、種類）と + / −、
/// 右に `ControlTypeComboBox`（Text Box / Branch Selector / Check Box）と種類ごとの欄、下に説明。Save / Cancel。
/// </summary>
public sealed partial class EditCustomControlsDialogViewModel : DialogViewModel
{
    public EditCustomControlsDialogViewModel(IReadOnlyList<CustomCommandControl> controls)
    {
        foreach (var c in controls) Items.Add(new CustomControlItemViewModel(c, this));
        Renumber();
        Selected = Items.FirstOrDefault();
    }

    public override string Title => "Edit Custom Command UI Controls";
    public override string Subtitle => "";
    public override string SubmitText => "Save";
    public override double DialogWidth => 760;
    public override bool ShowHeader => false;

    public static IReadOnlyList<Choice> ControlTypes { get; } = [new("textBox", "Text Box"), new("dropdown", "Branch Selector"), new("checkBox", "Check Box")];
    public static IReadOnlyList<Choice> TextBoxTypes { get; } = [new("generic", "Generic"), new("filepath", "File Path")];
    public static IReadOnlyList<Choice> DialogTypes { get; } = [new("saveFile", "Save File"), new("openFile", "Open File"), new("openDirectory", "Open Directory")];

    public ObservableCollection<CustomControlItemViewModel> Items { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))] private CustomControlItemViewModel? _selected;
    public bool HasSelection => Selected is not null;

    /// <summary>+ のメニュー（Add Text Box / Add Branch Selector / Add Check Box）。題の既定は Text Box / Reference / Check Box。</summary>
    public void Add(string type)
    {
        var item = new CustomControlItemViewModel(new CustomCommandControl(type, type switch { "dropdown" => "Reference", "checkBox" => "Check Box", _ => "Text Box" }), this);
        Items.Add(item);
        Renumber();
        Selected = item;
    }

    public void RemoveSelected()
    {
        if (Selected is not { } s) return;
        var i = Items.IndexOf(s);
        Items.Remove(s);
        Renumber();
        Selected = Items.Count == 0 ? null : Items[Math.Min(i, Items.Count - 1)];
    }

    private void Renumber()
    {
        for (var i = 0; i < Items.Count; i++) Items[i].Index = i + 1;
    }

    public IReadOnlyList<CustomCommandControl> Result => Items.Select(i => i.ToControl()).ToList();

    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>ComboBox の選択肢（値、表示、右に淡く出す説明）。</summary>
public sealed record Choice(string Id, string Title, string Description = "");

/// <summary>編集中の UI コントロール 1 つ。</summary>
public sealed partial class CustomControlItemViewModel : ObservableObject
{
    public CustomControlItemViewModel(CustomCommandControl c, EditCustomControlsDialogViewModel owner)
    {
        _type = c.Type;
        _title = c.Title;
        _textBoxType = c.TextBoxType;
        _text = c.Text;
        _placeholder = c.Placeholder;
        _dialogType = c.DialogType;
        _defaultDirectory = c.DefaultDirectory;
        _filename = c.Filename;
        _filter = c.Filter;
        _checkedValue = c.CheckedValue;
        _uncheckedValue = c.UncheckedValue;
        _defaultValue = c.DefaultValue;
    }

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Variable), nameof(Description))] private int _index;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TypeText), nameof(IsTextBox), nameof(IsGeneric), nameof(IsFilePath), nameof(IsDropdown), nameof(IsCheckBox), nameof(Description))] private string _type;
    [ObservableProperty] private string _title;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsGeneric), nameof(IsFilePath), nameof(Description))] private string _textBoxType;
    [ObservableProperty] private string _text;
    [ObservableProperty] private string _placeholder;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasFileName))] private string _dialogType;
    [ObservableProperty] private string _defaultDirectory;
    [ObservableProperty] private string _filename;
    [ObservableProperty] private string _filter;
    [ObservableProperty] private string _checkedValue;
    [ObservableProperty] private string _uncheckedValue;
    [ObservableProperty] private bool _defaultValue;

    public string Variable => $"${Index}";
    public string TypeText => EditCustomControlsDialogViewModel.ControlTypes.FirstOrDefault(t => t.Id == Type)?.Title ?? Type;
    public bool IsTextBox => Type == "textBox";
    public bool IsGeneric => IsTextBox && TextBoxType != "filepath";
    public bool IsFilePath => IsTextBox && TextBoxType == "filepath";
    public bool IsDropdown => Type == "dropdown";
    public bool IsCheckBox => Type == "checkBox";
    /// <summary>File Name の欄（Open Directory では使わない）。</summary>
    public bool HasFileName => DialogType != "openDirectory";
    public string Description => CustomCommandHelp.ControlDescription(ToControl(), Index);

    public CustomCommandControl ToControl() => new(Type, Title)
    {
        TextBoxType = TextBoxType, Text = Text, Placeholder = Placeholder,
        DialogType = DialogType, DefaultDirectory = DefaultDirectory, Filename = Filename,
        Filter = Filter, CheckedValue = CheckedValue, UncheckedValue = UncheckedValue, DefaultValue = DefaultValue,
    };
}

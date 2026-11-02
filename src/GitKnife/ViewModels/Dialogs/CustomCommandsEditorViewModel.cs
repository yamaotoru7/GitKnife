using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Logging;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Preferences（グローバル）と Repository Settings（Local / Shared）の Custom Commands タブ。
/// 一覧は Shared → Local（Preferences はグローバルだけ）、それぞれ名前順。閉じたときに <see cref="Save"/> で書く。
/// </summary>
public sealed partial class CustomCommandsEditorViewModel : ObservableObject
{
    private readonly string? _worktree;
    private readonly string? _commonGitDirectory;
    private readonly string _globalPath;

    /// <param name="worktree">Repository Settings なら作業ツリー、Preferences なら null。</param>
    public CustomCommandsEditorViewModel(string? worktree, string? commonGitDirectory, string? globalPath = null)
    {
        _worktree = worktree;
        _commonGitDirectory = commonGitDirectory;
        _globalPath = globalPath ?? CustomCommandStore.GlobalPath;
        IEnumerable<CustomCommand> loaded = IsRepository
            ? [.. CustomCommandStore.LoadFile(SharedPath!, CustomCommandSource.Shared), .. CustomCommandStore.LoadFile(LocalPath!, CustomCommandSource.Local)]
            : CustomCommandStore.LoadFile(_globalPath, CustomCommandSource.Global);
        foreach (var c in loaded.OrderBy(c => c.Source).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
            Items.Add(CustomCommandItemViewModel.From(c, this));
        Selected = Items.FirstOrDefault();
    }

    public bool IsRepository => _worktree is not null;
    public ObservableCollection<CustomCommandItemViewModel> Items { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))] private CustomCommandItemViewModel? _selected;
    public bool HasSelection => Selected is not null;

    internal string? SharedPath => _worktree is null ? null : CustomCommandStore.SharedPath(_worktree);
    internal string? LocalPath => _commonGitDirectory is null ? null : CustomCommandStore.LocalPath(_commonGitDirectory);

    /// <summary>表示用の場所（`.git/gitknife/custom-commands.json` など。作業ツリーからの相対）。</summary>
    internal string LocationText(bool shared)
    {
        var path = shared ? SharedPath : LocalPath;
        if (path is null || _worktree is null) return "";
        var rel = System.IO.Path.GetRelativePath(_worktree, path);
        return rel.Replace('\\', '/');
    }

    /// <summary>+ のメニュー（Add Commit / Repository / File / Branch / Submodule Custom Command）。既定は Open Url。</summary>
    public void Add(string target)
    {
        var source = IsRepository ? CustomCommandSource.Local : CustomCommandSource.Global;
        var command = new CustomCommand("Custom Command", target, source)
        {
            RefTargets = target == "ref" ? ["localbranch", "remotebranch"] : [],
            Action = new CustomCommandAction("url", Url: "https://example.com"),
        };
        var item = CustomCommandItemViewModel.From(command, this);
        Items.Add(item);
        Selected = item;
    }

    public void RemoveSelected()
    {
        if (Selected is not { } s) return;
        var i = Items.IndexOf(s);
        Items.Remove(s);
        Selected = Items.Count == 0 ? null : Items[Math.Min(i, Items.Count - 1)];
    }

    public void Save()
    {
        try
        {
            var commands = Items.Select(i => i.ToCommand()).ToList();
            if (!IsRepository)
            {
                CustomCommandStore.Save(_globalPath, commands, keepEmptyFile: true);
                return;
            }
            if (SharedPath is { } shared) CustomCommandStore.Save(shared, commands.Where(c => c.Source == CustomCommandSource.Shared), keepEmptyFile: false);
            if (LocalPath is { } local) CustomCommandStore.Save(local, commands.Where(c => c.Source == CustomCommandSource.Local), keepEmptyFile: false);
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { Log.Warning($"custom commands save failed: {ex.Message}"); }
    }
}

/// <summary>編集中のカスタムコマンド 1 つ（右側の欄と一覧の 3 行）。</summary>
public sealed partial class CustomCommandItemViewModel : ObservableObject
{
    private readonly CustomCommandsEditorViewModel _owner;

    private CustomCommandItemViewModel(CustomCommandsEditorViewModel owner) => _owner = owner;

    public static IReadOnlyList<(string id, string title, string description)> Targets { get; } =
    [
        ("revision", "Commit", "visible in commit context menu"),
        ("repository", "Repository", "visible in 'Open In' dropdown"),
        ("file", "File", "visible in file context menu"),
        ("ref", "Branch", "visible in branch context menu"),
        ("submodule", "Submodule", "visible in submodule context menu"),
    ];

    /// <summary>OS（Shared のときだけ。"" = Any）。</summary>
    public static IReadOnlyList<string> OsValues { get; } = ["", "windows", "macOS"];

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title))] private string _name = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(TargetText), nameof(IsRef), nameof(Variables))] private string _target = "revision";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsLocal), nameof(LocationText))] private bool _isShared;
    [ObservableProperty] private string _os = "";
    [ObservableProperty] private bool _refLocal;
    [ObservableProperty] private bool _refRemote;
    [ObservableProperty] private bool _refTag;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAction), nameof(Details))] private bool _isUi;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Details), nameof(ActionTitle), nameof(ActionDetails))] private CustomCommandAction _action = new("url", Url: "");
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Details))] private string _uiTitle = "";
    [ObservableProperty] private string _uiDescription = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ControlsText))] private IReadOnlyList<CustomCommandControl> _controls = [];
    [ObservableProperty] private bool _button1Enabled = true;
    [ObservableProperty] private string _button1Title = "OK";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Button1ActionTitle), nameof(Button1ActionDetails))] private CustomCommandAction _button1Action = new("url", Url: "");
    [ObservableProperty] private bool _button2Enabled = true;
    [ObservableProperty] private string _button2Title = "Cancel";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Button2ActionTitle), nameof(Button2ActionDetails))] private CustomCommandAction _button2Action = new("cancel");

    public string Title => Name;
    public bool IsLocal { get => !IsShared; set => IsShared = !value; }
    public bool IsAction { get => !IsUi; set => IsUi = !value; }
    public bool IsRef => Target == "ref";
    public bool IsRepository => _owner.IsRepository;
    public string LocationText => _owner.LocationText(IsShared);
    public string TargetText => Targets.FirstOrDefault(t => t.id == Target).title ?? Target;
    /// <summary>一覧の 2 行目（UI なら "ui: 題名"、Action なら動作の説明）。</summary>
    public string Details => IsUi ? "ui: " + UiTitle : ActionDetailsOf(Action);
    public string ActionTitle => ActionTitleOf(Action);
    public string ActionDetails => ActionDetailsOf(Action);
    public string Button1ActionTitle => ActionTitleOf(Button1Action);
    public string Button1ActionDetails => ActionDetailsOf(Button1Action);
    public string Button2ActionTitle => ActionTitleOf(Button2Action);
    public string Button2ActionDetails => ActionDetailsOf(Button2Action);
    /// <summary>Controls の欄（"Message, Branch, Force"）。</summary>
    public string ControlsText => string.Join(", ", Controls.Select(c => c.Title));
    public IReadOnlyList<CustomCommandVariableRow> Variables => CustomCommandHelp.Variables(Target);

    public static string ActionTitleOf(CustomCommandAction a) => a.Type switch
    {
        "process" => "Start Process",
        "sh" => "Bash Command",
        "url" => "Open Url",
        "cancel" => "Cancel",
        _ => a.Type,
    };

    public static string ActionDetailsOf(CustomCommandAction a) => a.Type switch
    {
        "process" => a.Path ?? "",
        "sh" => "Run bash Script",
        "url" => a.Url ?? "",
        "cancel" => "close dialog",
        _ => "",
    };

    partial void OnIsUiChanged(bool value)
    {
        // UI にしたとき最初は既定（題 "Execute custom command"、OK = いまの動作、Cancel）
        if (value && UiTitle.Length == 0 && Controls.Count == 0)
        {
            UiTitle = "Execute custom command";
            UiDescription = "Are you sure you want to execute custom command?";
            Button1Action = Action;
        }
    }

    public static CustomCommandItemViewModel From(CustomCommand c, CustomCommandsEditorViewModel owner)
    {
        var vm = new CustomCommandItemViewModel(owner)
        {
            _name = c.Name,
            _target = c.Target,
            _isShared = c.Source == CustomCommandSource.Shared,
            _os = c.Os ?? "",
            _refLocal = c.RefTargets.Contains("localbranch"),
            _refRemote = c.RefTargets.Contains("remotebranch"),
            _refTag = c.RefTargets.Contains("tag"),
            _isUi = c.Ui is not null,
            _action = c.Action ?? new CustomCommandAction("url", Url: ""),
        };
        if (c.Ui is { } ui)
        {
            vm._uiTitle = ui.Title;
            vm._uiDescription = ui.Description;
            vm._controls = ui.Controls;
            vm._button1Enabled = ui.Buttons.Count > 0;
            if (ui.Buttons.Count > 0) { vm._button1Title = ui.Buttons[0].Title; vm._button1Action = ui.Buttons[0].Action; }
            vm._button2Enabled = ui.Buttons.Count > 1;
            if (ui.Buttons.Count > 1) { vm._button2Title = ui.Buttons[1].Title; vm._button2Action = ui.Buttons[1].Action; }
        }
        return vm;
    }

    public CustomCommand ToCommand()
    {
        var source = !_owner.IsRepository ? CustomCommandSource.Global : IsShared ? CustomCommandSource.Shared : CustomCommandSource.Local;
        var refs = new List<string>();
        if (RefLocal) refs.Add("localbranch");
        if (RefRemote) refs.Add("remotebranch");
        if (RefTag) refs.Add("tag");
        CustomCommandUi? ui = null;
        if (IsUi)
        {
            var buttons = new List<CustomCommandButton>();
            if (Button1Enabled) buttons.Add(new CustomCommandButton(Button1Title, Button1Action));
            if (Button2Enabled) buttons.Add(new CustomCommandButton(Button2Title, Button2Action));
            ui = new CustomCommandUi(UiTitle, UiDescription, Controls, buttons);
        }
        return new CustomCommand(Name, Target, source)
        {
            // OS の欄は Shared のときだけ出るが、値は Local でも残す（Local の "os" : "macOS" も書き直しで消さない）
            Os = Os.Length > 0 ? Os : null,
            RefTargets = Target == "ref" ? refs : [],
            Action = IsUi ? null : Action,
            Ui = ui,
        };
    }
}

/// <summary>右の "Available variables:" の文（一覧。target ごと）。</summary>
public static class CustomCommandHelp
{
    public static IReadOnlyList<CustomCommandVariableRow> Variables(string target)
    {
        var current = new List<(string, string)> { ("${repo:name}", "repository name") };
        var obsolete = new List<(string, string)> { ("$repository", "repository name") };
        void Sha()
        {
            current.Add(("${sha}", "revision sha"));
            current.Add(("${sha:abbr}", "revision sha (abbreviated)"));
            obsolete.Add(("$SHA", "revision sha"));
            obsolete.Add(("$sha", "revision sha (abbreviated)"));
        }
        switch (target)
        {
            case "revision":
                Sha();
                break;
            case "file":
                current.Add(("${file}", "file path"));
                current.Add(("${file:name}", "file name"));
                Sha();
                obsolete.Add(("$filepath", "file path"));
                obsolete.Add(("$filename", "file name"));
                break;
            case "ref":
                Sha();
                current.Add(("${ref}", "branch name"));
                current.Add(("${ref:short}", "branch w/o remote prefix"));
                current.Add(("${ref:full}", "branch full reference"));
                obsolete.Add(("$name", "branch name"));
                obsolete.Add(("$shortname", "branch w/o remote prefix"));
                obsolete.Add(("$fullreference", "branch full reference"));
                break;
            case "submodule":
                current.Add(("${submodule}", "submodule"));
                obsolete.Clear();
                obsolete.Add(("$path", "submodule"));
                obsolete.Add(("$name", "submodule"));
                break;
        }
        // 空の行と "Obsolete:" の見出しも行として並べる（表示は 2 列の表）
        return [.. current.Select(r => new CustomCommandVariableRow(r.Item1, r.Item2)), new("", ""), new("Obsolete:", ""),
            .. obsolete.Select(r => new CustomCommandVariableRow(r.Item1, r.Item2))];
    }

    /// <summary>UI のコントロールの説明（Edit Custom Command UI Controls の右）。</summary>
    public static string ControlDescription(CustomCommandControl c, int index) => (c.Type, c.TextBoxType) switch
    {
        ("textBox", "filepath") => $"File Path Text Box\n\nLets the user pick a path with the system file dialog. You can set the default folder and the file name.\n\nUse the selected path in the 'OK' action as ${index}{{path}}.",
        ("textBox", _) => $"Generic Text Box\n\nAsks for a string: a new branch name, a commit message, an extra argument for a git command.\n\nYou can set the initial text and the placeholder.\n\nUse the entered text in the 'OK' action as ${index}{{text}}.",
        ("dropdown", _) => $"Branch Drop Down\n\nLets the user pick a branch or a tag.\n\nFilter by full reference prefixes separated by spaces:\n- refs/heads - local branches\n- refs/remotes/origin - branches of the origin remote\n- refs/tags - tags\n\nUse the selected name in the 'OK' action as ${index}{{ref}}.",
        _ => $"Check Box\n\nPasses one of two values depending on the check box state (either can be empty), e.g. --force when checked and nothing otherwise.\n\nUse the value in the 'OK' action as ${index}{{value}}.",
    };
}

public sealed record CustomCommandVariableRow(string Name, string Text);

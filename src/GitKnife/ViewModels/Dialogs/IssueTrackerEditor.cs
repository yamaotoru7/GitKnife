using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// Repository Settings > Issue Tracker のルールの編集。
/// 左に BugTrackerRulesListBox（名前と Local / Shared）、+ のメニュー（Add New Rule / Add Sample Github Rule / Add Sample Jira Rule / Add Sample Jira Multiproject Rule）と −、
/// 右に Name、Local / Shared と置き場所、Parsing Regular Expression（valid / invalid）、Result Url、Test Message とリンクの見本。閉じたときに書く。
/// </summary>
public sealed partial class IssueTrackerEditorViewModel : ObservableObject
{
    private readonly string _gitDirectory;
    private readonly string _worktree;

    /// <param name="rules">読んでおいたルール（<see cref="IssueTracker.Load"/>。SSH の先では UI のスレッドの外で読む）。</param>
    public IssueTrackerEditorViewModel(string worktree, string gitDirectory, IEnumerable<IssueTrackerRule> rules)
    {
        _worktree = worktree;
        _gitDirectory = gitDirectory;
        foreach (var r in rules) Rules.Add(new IssueRuleItemViewModel(r, this));
        Selected = Rules.FirstOrDefault();
    }

    public ObservableCollection<IssueRuleItemViewModel> Rules { get; } = [];
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection))] private IssueRuleItemViewModel? _selected;
    public bool HasSelection => Selected is not null;

    /// <summary>Test Message（ルールを替えても残る）。</summary>
    [ObservableProperty] private string _testMessage = IssueTracker.SampleMessage;

    public void Add(IssueTrackerRule rule)
    {
        var item = new IssueRuleItemViewModel(rule, this);
        Rules.Add(item);
        Selected = item;
    }

    public void AddNew() => Add(new IssueTrackerRule(IssueTracker.NewRuleName, "", "", false));

    public void RemoveSelected()
    {
        if (Selected is not { } s) return;
        var i = Rules.IndexOf(s);
        Rules.Remove(s);
        Selected = Rules.Count == 0 ? null : Rules[Math.Min(i, Rules.Count - 1)];
    }

    /// <summary>閉じたとき（Local は .git/issuetracker、Shared は .issuetracker。完成したルールだけ書く）。</summary>
    public void Save() => CreateSave()();

    /// <summary>書き込み（今のルールを取っておき、書くのは返した処理。SSH の先では UI のスレッドの外で動かす）。</summary>
    public Action CreateSave()
    {
        var rules = Rules.Select(r => r.ToRule()).ToList();
        var gitDirectory = _gitDirectory;
        var worktree = _worktree;
        return () =>
        {
            try { IssueTracker.Save(gitDirectory, worktree, rules); }
            catch (System.IO.IOException ex) { Core.Logging.Log.Warning($"Saving issue tracker rules failed: {ex.Message}"); }
        };
    }

    internal void RaisePreview() => OnPropertyChanged(nameof(TestMessage));
}

public sealed partial class IssueRuleItemViewModel : ObservableObject
{
    private readonly IssueTrackerEditorViewModel _owner;

    public IssueRuleItemViewModel(IssueTrackerRule rule, IssueTrackerEditorViewModel owner)
    {
        _owner = owner;
        _name = rule.Name;
        _regex = rule.Regex;
        _url = rule.Url;
        _isShared = rule.Shared;
    }

    [ObservableProperty] private string _name;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValid), nameof(StatusText))] private string _regex;
    [ObservableProperty] private string _url;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsLocal), nameof(LocationText), nameof(KindText))] private bool _isShared;

    public bool IsLocal { get => !IsShared; set { if (value) IsShared = false; } }
    public string KindText => IsShared ? "Shared" : "Local";
    /// <summary>RuleLocationTextBlock（".git/issuetracker" / ".issuetracker"）。</summary>
    public string LocationText => IsShared ? IssueTracker.SharedFileName : ".git/" + IssueTracker.LocalFileName;
    public bool IsValid => ToRule().IsValid;
    public string StatusText => IsValid ? "valid" : "invalid";

    partial void OnRegexChanged(string value) => _owner.RaisePreview();
    partial void OnUrlChanged(string value) => _owner.RaisePreview();

    public IssueTrackerRule ToRule() => new(Name, Regex, Url, IsShared);
}

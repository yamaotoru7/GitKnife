using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Settings;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>Workspaces の 1 行。普段は名前の文字だけで、Rename / F2 のあいだだけ入力欄（<see cref="IsEditing"/>）。</summary>
public sealed partial class WorkspaceItem(Workspace workspace) : ObservableObject
{
    public Workspace Workspace { get; } = workspace;
    [ObservableProperty] private string _name = workspace.Name;
    [ObservableProperty] private bool _isEditing;
    /// <summary>編集中の文字（Enter か外へ出たら Name に入れ、Esc なら捨てる）。</summary>
    [ObservableProperty] private string _editName = "";
}

/// <summary>
/// Workspaces > Configure...。"Workspaces" / "Use '/' as path separator to create folders"、
/// 枠つきの WorkspacesListBox と下の帯の AddWorkspaceButton / RemoveWorkspaceButton、ShowWorkspaceInTitleCheckBox、Close だけ。窓は 330×370 で大きさを変えられる。
/// 一覧の右クリックは Add New Workspace / --- / Rename / Delete...。Rename と F2 でその場の入力欄、Esc で取り消し（ダブルクリックでは何もしない）。
/// + は末尾に "New Workspace" を足して選び、名前の入力欄にする。- と Delete... は確認してから消す。開いたときは先頭の行を選ぶ。閉じるときに settings に書く（今のワークスペースの名前を変えたら追う）。
/// </summary>
public sealed partial class WorkspacesDialogViewModel : DialogViewModel
{
    private readonly MainViewModel _main;

    public WorkspacesDialogViewModel(MainViewModel main)
    {
        _main = main;
        var all = main.Settings.Workspaces.All;
        if (all.Count == 0) all.Add(new Workspace { Name = main.Settings.Workspaces.ActiveWorkspace });
        foreach (var w in all) Items.Add(new WorkspaceItem(w));
        _selected = Items.FirstOrDefault();
        _showInTitle = main.Settings.Workspaces.ShowInTitle;
    }

    public override string Title => "Workspaces";
    public override string Subtitle => "Use '/' as path separator to create folders";
    public override string SubmitText => "Submit";
    public override bool ShowSubmit => false;
    public override string CancelText => "Close";
    public override double DialogWidth => 330;
    public override double DialogHeight => 370;
    public override bool CanResize => true;
    public override bool ShowHeader => false;
    public ObservableCollection<WorkspaceItem> Items { get; } = [];
    [ObservableProperty] private WorkspaceItem? _selected;
    [ObservableProperty] private bool _showInTitle;

    /// <summary>
    /// 末尾に "New Workspace" を足して選ぶ。画面はそのまま名前の入力欄にする（Issue #3）。
    /// 同じ名前があるときは " 2"、" 3" … を付ける。
    /// </summary>
    public WorkspaceItem Add()
    {
        const string baseName = "New Workspace";
        var name = baseName;
        for (var n = 2; Items.Any(i => i.Name == name); n++) name = $"{baseName} {n}";
        var item = new WorkspaceItem(new Workspace { Name = name });
        Items.Add(item);
        Selected = item;
        return item;
    }

    /// <summary>消せるか。今のワークスペースは消せない。</summary>
    public bool CanRemove(WorkspaceItem item) => item.Workspace.Name != _main.Settings.Workspaces.ActiveWorkspace;

    public void Remove(WorkspaceItem item)
    {
        if (!CanRemove(item)) return;
        var index = Items.IndexOf(item);
        Items.Remove(item);
        Selected = Items.Count == 0 ? null : Items[Math.Min(index, Items.Count - 1)];
    }

    /// <summary>行をドラッグで並べ替える（#5）。並びは閉じるときに settings の順に書き、ツールバーの一覧もその順になる。</summary>
    public void Move(WorkspaceItem item, int index)
    {
        var from = Items.IndexOf(item);
        index = Math.Clamp(index, 0, Items.Count - 1);
        if (from < 0 || from == index) return;
        Items.Move(from, index);
        Selected = item;
    }

    public void BeginRename(WorkspaceItem item)
    {
        foreach (var i in Items.Where(i => i.IsEditing && i != item)) CommitRename(i);
        Selected = item;
        item.EditName = item.Name;
        item.IsEditing = true;
    }

    /// <summary>空なら元の名前のまま。</summary>
    public void CommitRename(WorkspaceItem item)
    {
        if (!item.IsEditing) return;
        var name = item.EditName.Trim();
        if (name.Length > 0) item.Name = name;
        item.IsEditing = false;
    }

    public void CancelRename(WorkspaceItem item) => item.IsEditing = false;

    /// <summary>閉じるときに settings へ書く。</summary>
    public void Apply()
    {
        foreach (var i in Items) CommitRename(i);
        var ws = _main.Settings.Workspaces;
        foreach (var i in Items)
        {
            if (i.Workspace.Name == ws.ActiveWorkspace) ws.ActiveWorkspace = i.Name;
            i.Workspace.Name = i.Name;
        }
        ws.All = Items.Select(i => i.Workspace).ToList();
        ws.ShowInTitle = ShowInTitle;
        _main.NotifyWorkspaceChanged();
        App.SaveSettings();
    }

    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

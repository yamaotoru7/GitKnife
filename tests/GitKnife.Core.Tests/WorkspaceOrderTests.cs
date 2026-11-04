using GitKnife.Core.Settings;

namespace GitKnife.Core.Tests;

public class WorkspaceOrderTests
{
    private static List<Workspace> Make(params string[] names) => names.Select(n => new Workspace { Name = n }).ToList();
    private static string[] Names(List<Workspace> all) => all.Select(w => w.Name).ToArray();

    [Fact]
    public void MovesDownAndUpAmongTopLevel()
    {
        var all = Make("Home", "Work", "Game");
        Assert.True(WorkspaceOrder.Move(all, "Home", "Game"));
        Assert.Equal(["Work", "Game", "Home"], Names(all));
        Assert.True(WorkspaceOrder.Move(all, "Home", "Work"));
        Assert.Equal(["Home", "Work", "Game"], Names(all));
    }

    [Fact]
    public void KeepsTheSameWorkspaceObjects()
    {
        var all = Make("A", "B");
        var a = all[0];
        WorkspaceOrder.Move(all, "A", "B");
        Assert.Same(a, all[1]);
    }

    [Fact]
    public void MovesInsideAFolder()
    {
        var all = Make("Home", "UFO/WiFi", "UFO/Link", "UFO/Pi");
        Assert.True(WorkspaceOrder.Move(all, "UFO/Pi", "UFO/WiFi"));
        Assert.Equal(["Home", "UFO/Pi", "UFO/WiFi", "UFO/Link"], Names(all));
    }

    [Fact]
    public void MovesAWholeFolderAndGroupsItsMembers()
    {
        // フォルダはメニューで最初の項目の位置に出る。動かすとフォルダの中身がまとまって並ぶ
        var all = Make("UFO/WiFi", "Home", "UFO/Link", "Work");
        Assert.True(WorkspaceOrder.Move(all, "UFO/", "Work"));
        Assert.Equal(["Home", "Work", "UFO/WiFi", "UFO/Link"], Names(all));
    }

    [Fact]
    public void DoesNotMoveAcrossFolders()
    {
        var all = Make("Home", "UFO/WiFi", "Work");
        Assert.False(WorkspaceOrder.Move(all, "UFO/WiFi", "Home"));
        Assert.False(WorkspaceOrder.Move(all, "Home", "Missing"));
        Assert.False(WorkspaceOrder.Move(all, "Home", "Home"));
        Assert.Equal(["Home", "UFO/WiFi", "Work"], Names(all));
    }

    [Fact]
    public void NestedFolderKeys()
    {
        Assert.Equal("a/", WorkspaceOrder.FolderKey(WorkspaceOrder.Parts("a/b/c"), 1));
        Assert.Equal("a/b/", WorkspaceOrder.FolderKey(WorkspaceOrder.Parts("a//b/c"), 2));
        var all = Make("a/b/x", "a/c", "a/b/y");
        Assert.True(WorkspaceOrder.Move(all, "a/c", "a/b/"));
        Assert.Equal(["a/c", "a/b/x", "a/b/y"], Names(all));
    }
}

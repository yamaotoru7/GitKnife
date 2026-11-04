using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitKnife.Core.Settings;
using GitKnife.ViewModels;

namespace GitKnife.Views;

/// <summary>
/// ツールバーの Workspaces のメニューで、ワークスペース（とフォルダ）の項目を縦にドラッグして並べ替える。
/// 押して縦に少し動かすとドラッグを始め、カーソルの下の同じフォルダの項目の位置にその場で入れ替える。
/// ドラッグした後に離しても切り替えない。並べ替えたらメニューを閉じるときに settings.json に書く。
/// 項目のキーは <see cref="MenuItem.Tag"/>（<see cref="ToolbarMenus.AddWorkspaces"/> が付ける）。
/// </summary>
internal static class WorkspaceMenuDrag
{
    public static void Attach(ContextMenu menu, MainViewModel vm)
    {
        MenuItem? pressed = null;
        Point start = default;
        var dragging = false;
        var moved = false;

        menu.PreviewMouseLeftButtonDown += (_, e) =>
        {
            pressed = ItemOf(e.OriginalSource as DependencyObject);
            start = e.GetPosition(menu);
            dragging = false;
        };
        menu.PreviewMouseMove += (_, e) =>
        {
            if (pressed is null) return;
            if (e.LeftButton != MouseButtonState.Pressed) { pressed = null; dragging = false; return; }
            if (!dragging)
            {
                if (Math.Abs(e.GetPosition(menu).Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                dragging = true;
            }
            if (ItemsControl.ItemsControlFromItemContainer(pressed) is not { } parent) return;
            var target = parent.Items.OfType<MenuItem>().FirstOrDefault(i => i != pressed && i.Tag is string && IsUnder(i, e));
            if (target is null) return;
            if (!WorkspaceOrder.Move(vm.Settings.Workspaces.All, (string)pressed.Tag, (string)target.Tag)) return;
            var to = parent.Items.IndexOf(target);
            parent.Items.Remove(pressed);
            parent.Items.Insert(to, pressed);
            pressed.Focus();
            moved = true;
        };
        menu.PreviewMouseLeftButtonUp += (_, e) =>
        {
            // ドラッグの後は項目の Click（切り替え）を起こさない
            if (dragging) e.Handled = true;
            pressed = null;
            dragging = false;
        };
        menu.Closed += (_, _) =>
        {
            if (moved) App.SaveSettings();
        };
    }

    private static MenuItem? ItemOf(DependencyObject? d)
    {
        for (; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is MenuItem { Tag: string } mi) return mi;
        return null;
    }

    private static bool IsUnder(MenuItem item, MouseEventArgs e)
    {
        if (!item.IsVisible) return false;
        var p = e.GetPosition(item);
        return p.Y >= 0 && p.Y < item.ActualHeight && p.X >= 0 && p.X < item.ActualWidth;
    }
}

using System.Windows.Controls;
using System.Windows.Input;
using GitKnife.ViewModels;

namespace GitKnife.Views;

/// <summary>
/// 木を平らに並べた一覧（行に階層と開閉がある ListBox / ListView）の ← / →。エクスプローラーと同じ：
/// →：閉じた行は開き、開いた行は最初の子へ。←：開いた行は閉じ、それ以外は親の行へ。
/// 開閉で行が並べ直されても、動いた先の行を選んでフォーカスを置く（続けて矢印キーで動ける）。1 行だけ選んでいるときに効く。
/// </summary>
internal static class TreeKeys
{
    /// <summary>行の形。CanExpand は子があって開閉できるか。親は「上にある、階層が浅い最初の行」。</summary>
    public readonly record struct Node(bool CanExpand, bool IsExpanded, int Level);

    /// <param name="describe">行の形（木の行でなければ null。そのときはキーを横取りしない）。</param>
    /// <param name="toggle">行を開閉して並べ直し、並べ直した後のその行を返す（行を作り直す一覧のため）。</param>
    public static void Attach(ListBox list, Func<object, Node?> describe, Func<object, object?> toggle)
    {
        list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is not (Key.Left or Key.Right) || Keyboard.Modifiers != ModifierKeys.None) return;
            if (list.SelectedItems.Count != 1 || list.SelectedItem is not { } item || describe(item) is not { } node) return;
            e.Handled = true;
            if (Move(list, item, node, e.Key == Key.Right, describe, toggle) is not { } target) return;
            list.SelectedItem = target;
            list.ScrollIntoView(target);
            list.UpdateLayout();
            (list.ItemContainerGenerator.ContainerFromItem(target) as ListBoxItem)?.Focus();
        };
    }

    /// <summary>ファイル一覧（FileListItem の行。ツリー表示のフォルダ）。</summary>
    public static void AttachFiles(ListBox list, Action<FileListItem> toggle)
        => Attach(list,
            row => row is FileListItem f ? new Node(f.IsFolder && f.Children.Count > 0, f.IsExpanded, f.Level) : null,
            row => { toggle((FileListItem)row); return row; });

    /// <summary>動いた先の行（動かなければ null）。</summary>
    private static object? Move(ListBox list, object item, Node node, bool right, Func<object, Node?> describe, Func<object, object?> toggle)
    {
        if (right)
        {
            if (!node.CanExpand) return null;
            if (!node.IsExpanded) return toggle(item);
            var i = list.Items.IndexOf(item);
            return i >= 0 && i + 1 < list.Items.Count && describe(list.Items[i + 1]) is { } next && next.Level > node.Level ? list.Items[i + 1] : null;
        }
        if (node is { CanExpand: true, IsExpanded: true }) return toggle(item);
        for (var i = list.Items.IndexOf(item) - 1; i >= 0; i--)
            if (describe(list.Items[i]) is { } up && up.Level < node.Level) return list.Items[i];
        return null;
    }
}

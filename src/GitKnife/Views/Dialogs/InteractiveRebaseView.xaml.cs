using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GitKnife.ViewModels;

namespace GitKnife.Views.Dialogs;

public partial class InteractiveRebaseView : UserControl
{
    public InteractiveRebaseView() => InitializeComponent();

    private InteractiveRebaseViewModel? Vm => DataContext as InteractiveRebaseViewModel;

    private void List_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Vm is { } vm) vm.SelectedSteps = [.. List.SelectedItems.Cast<RebaseStepViewModel>().OrderBy(vm.Steps.IndexOf)];
    }

    /// <summary>ドロップダウンは右端を動作の欄の右端より少し内側にそろえ、左へ広がる。</summary>
    private void ActionsPopup_Loaded(object sender, System.Windows.RoutedEventArgs e)
        => ((Popup)sender).CustomPopupPlacementCallback = (popup, target, _) =>
            [new CustomPopupPlacement(new System.Windows.Point(target.Width - popup.Width - 17, target.Height - 4), PopupPrimaryAxis.Horizontal)];

    /// <summary>Reword の窓を、その行の下（左は件名の列のあたり）に出す。本文の 72 桁目に縦線。</summary>
    private void RewordLayer_IsVisibleChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true || Vm is not { RewordStep: { } step }) return;
        var y = 30.0;
        if (List.ItemContainerGenerator.ContainerFromItem(step) is ListViewItem row)
            y = row.TranslatePoint(new System.Windows.Point(0, row.ActualHeight), List).Y + 7;
        Canvas.SetLeft(RewordEditor, 135);
        Canvas.SetTop(RewordEditor, y);
        var box = RewordDescriptionBox;
        var width = new System.Windows.Media.FormattedText(new string('x', 72), System.Globalization.CultureInfo.InvariantCulture, System.Windows.FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(box.FontFamily, box.FontStyle, box.FontWeight, box.FontStretch), box.FontSize, System.Windows.Media.Brushes.Black,
            System.Windows.Media.VisualTreeHelper.GetDpi(box).PixelsPerDip).WidthIncludingTrailingWhitespace;
        RewordRuler.Margin = new System.Windows.Thickness(box.Padding.Left + 2 + width, 0, 0, 0);
        Dispatcher.BeginInvoke(() => { RewordSubjectBox.Focus(); RewordSubjectBox.CaretIndex = RewordSubjectBox.Text.Length; }, System.Windows.Threading.DispatcherPriority.Input);
    }

    // ---- 行のドラッグ＆ドロップで並べ替え（公開リリースノートの「コミットのドラッグ＆ドロップ並べ替え」。細部は推定）----
    private const string DragFormat = "GitKnife.RebaseStep";
    private System.Windows.Point? _dragStart;

    private void List_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        => _dragStart = (e.OriginalSource as System.Windows.DependencyObject)?.FindAncestor<ComboBox>() is null
            && (e.OriginalSource as System.Windows.DependencyObject)?.FindAncestor<ListViewItem>() is not null ? e.GetPosition(List) : null;

    private void List_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start || e.LeftButton != MouseButtonState.Pressed || List.SelectedItem is not RebaseStepViewModel step) return;
        var d = e.GetPosition(List) - start;
        if (System.Math.Abs(d.Y) < System.Windows.SystemParameters.MinimumVerticalDragDistance) return;
        _dragStart = null;
        System.Windows.DragDrop.DoDragDrop(List, new System.Windows.DataObject(DragFormat, step), System.Windows.DragDropEffects.Move);
    }

    private void List_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DragFormat) ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void List_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (Vm is not { } vm || e.Data.GetData(DragFormat) is not RebaseStepViewModel step) return;
        var target = (e.OriginalSource as System.Windows.DependencyObject)?.FindAncestor<ListViewItem>()?.DataContext as RebaseStepViewModel;
        vm.MoveTo(step, target is null ? vm.Steps.Count - 1 : vm.Steps.IndexOf(target));
        e.Handled = true;
    }

    /// <summary>P / E / R / S / F / D で選んだ行の動作を変え、Ctrl+Up / Ctrl+Down で動かす（ドロップダウンに出ているキー）。</summary>
    private void List_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || vm.SelectedSteps.Count == 0 || e.OriginalSource is TextBox) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is Key.Up or Key.Down)
        {
            vm.Move(vm.SelectedSteps, e.Key == Key.Up ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (Keyboard.Modifiers != ModifierKeys.None) return;
        if (RebaseActionItem.All.FirstOrDefault(a => a.Key == e.Key.ToString()) is { } action)
        {
            vm.ApplyAction(vm.SelectedSteps, action);
            e.Handled = true;
        }
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GitKnife.Controls;

/// <summary>ツールバーのボタン（アイコンの下にラベル、右上に数字のバッジ）。見た目は Themes/CustomControls.xaml の暗黙スタイル。</summary>
public class ToolbarButton : Button
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(ToolbarButton));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(ToolbarButton));
    public static readonly DependencyProperty BadgeProperty = DependencyProperty.Register(nameof(Badge), typeof(int), typeof(ToolbarButton), new PropertyMetadata(0));
    public static readonly DependencyProperty BadgeAutomationIdProperty = DependencyProperty.Register(nameof(BadgeAutomationId), typeof(string), typeof(ToolbarButton));

    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string? Label { get => (string?)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public int Badge { get => (int)GetValue(BadgeProperty); set => SetValue(BadgeProperty, value); }
    public string? BadgeAutomationId { get => (string?)GetValue(BadgeAutomationIdProperty); set => SetValue(BadgeAutomationIdProperty, value); }

}

/// <summary>分割ボタンの右側（▼）。押すと ContextMenu をボタンの下に開く。</summary>
public class DropDownArrowButton : Button
{
    protected override void OnClick()
    {
        base.OnClick();
        if (ContextMenu is null) return;
        ContextMenu.PlacementTarget = Tag as UIElement ?? this;
        ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        ContextMenu.IsOpen = true;
    }
}

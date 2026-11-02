using System.Windows.Automation;
using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GitKnife.Controls;

/// <summary>
/// ダイアログの参照の選択（"toggleButton" + 現在値。押すとフィルタ付きの一覧が開く）。
/// Items は文字列の一覧、SelectedItem が選んだもの。DisplayText を入れると、ボタンにはそちらを出す（"default (origin/main)" など）。
/// </summary>
public sealed class RefPicker : UserControl
{
    public static readonly DependencyProperty ItemsProperty = DependencyProperty.Register(nameof(Items), typeof(IEnumerable), typeof(RefPicker),
        new PropertyMetadata(null, (d, _) => ((RefPicker)d).ApplyFilter()));
    public static readonly DependencyProperty SelectedItemProperty = DependencyProperty.Register(nameof(SelectedItem), typeof(string), typeof(RefPicker),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((RefPicker)d).UpdateText()));
    public static readonly DependencyProperty DisplayTextProperty = DependencyProperty.Register(nameof(DisplayText), typeof(string), typeof(RefPicker),
        new PropertyMetadata(null, (d, _) => ((RefPicker)d).UpdateText()));
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(RefPicker),
        new PropertyMetadata(null, (d, e) => ((RefPicker)d)._icon.Data = (Geometry?)e.NewValue));

    public IEnumerable? Items { get => (IEnumerable?)GetValue(ItemsProperty); set => SetValue(ItemsProperty, value); }
    public string? SelectedItem { get => (string?)GetValue(SelectedItemProperty); set => SetValue(SelectedItemProperty, value); }
    public string? DisplayText { get => (string?)GetValue(DisplayTextProperty); set => SetValue(DisplayTextProperty, value); }
    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }

    private readonly Button _button;
    private readonly Path _icon;
    private readonly TextBlock _text;
    private readonly Popup _popup;
    private readonly TextBox _filter;
    private readonly ListBox _list;

    public RefPicker()
    {
        _icon = new Path { Margin = new Thickness(0, 0, 6, 0) };
        _icon.SetResourceReference(StyleProperty, "Gk.IconPath");
        _text = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var chevron = new Path { HorizontalAlignment = HorizontalAlignment.Right };
        chevron.SetResourceReference(StyleProperty, "Gk.SmallIcon");
        chevron.SetResourceReference(Path.DataProperty, "Icon.ChevronDown");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(_text, 1);
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(_icon);
        grid.Children.Add(_text);
        grid.Children.Add(chevron);

        _button = new Button { Content = grid, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(6, 3, 6, 3), Height = 24 };
        _button.SetResourceReference(StyleProperty, "Gk.PickerButton");
        AutomationProperties.SetAutomationId(_button, "toggleButton");
        _button.Click += (_, _) => Open();

        // フィルタは文字を打ち始めたら出す（一覧は最初は候補だけ）
        _filter = new TextBox { Margin = new Thickness(6, 6, 6, 2), Height = 24, Visibility = Visibility.Collapsed };
        _filter.SetResourceReference(StyleProperty, "Gk.TextBox");
        AutomationProperties.SetAutomationId(_filter, "FilterTextBox");
        _filter.TextChanged += (_, _) =>
        {
            _filter.Visibility = _filter.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
            if (_filter.Text.Length == 0) _list?.Focus();
        };
        _filter.PreviewKeyDown += Filter_KeyDown;
        _list = new ListBox { MaxHeight = 260, BorderThickness = new Thickness(0), Padding = new Thickness(4) };
        _list.SetResourceReference(StyleProperty, "Gk.ListBox");
        _list.SetResourceReference(ListBox.ItemContainerStyleProperty, "Gk.PickerItem");
        _list.ItemTemplate = MakeItemTemplate();
        _list.PreviewTextInput += (_, e) =>
        {
            _filter.Visibility = Visibility.Visible;
            _filter.Text += e.Text;
            _filter.CaretIndex = _filter.Text.Length;
            _filter.Focus();
            e.Handled = true;
        };
        _list.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _list.SelectedItem is string s) { Choose(s); e.Handled = true; }
            else if (e.Key == Key.Escape) { ClosePopup(); e.Handled = true; }
        };
        _list.MouseUp += (_, e) => { if (_list.SelectedItem is string s) Choose(s); };
        var panel = new DockPanel();
        DockPanel.SetDock(_filter, Dock.Top);
        panel.Children.Add(_filter);
        panel.Children.Add(_list);
        var border = new Border { Child = panel, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        border.SetResourceReference(Border.BackgroundProperty, "Gk.Menu.Background");
        border.SetResourceReference(Border.BorderBrushProperty, "Gk.Border");
        _popup = new Popup { Child = border, PlacementTarget = _button, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true };
        Content = new Grid { Children = { _button, _popup } };
        UpdateText();
    }

    /// <summary>候補の行：アイコンと名前。</summary>
    private DataTemplate MakeItemTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(MarginProperty, new Thickness(8, 2, 8, 2));
        var icon = new FrameworkElementFactory(typeof(Path));
        icon.SetResourceReference(StyleProperty, "Gk.IconPath");
        icon.SetBinding(Path.DataProperty, new System.Windows.Data.Binding(nameof(Icon)) { Source = this });
        icon.SetValue(MarginProperty, new Thickness(0, 0, 6, 0));
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding());
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(icon);
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    private void UpdateText() => _text.Text = DisplayText ?? SelectedItem ?? "";

    private void Open()
    {
        _popup.MinWidth = _button.ActualWidth;
        _filter.Text = "";
        ApplyFilter();
        _list.SelectedItem = SelectedItem;
        _popup.IsOpen = true;
        Dispatcher.BeginInvoke(() =>
        {
            if (_list.ItemContainerGenerator.ContainerFromItem(_list.SelectedItem) is ListBoxItem item) item.Focus(); else _list.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void ApplyFilter()
    {
        if (_list is null) return;
        var all = Items?.Cast<object>().Select(o => o.ToString() ?? "") ?? [];
        var q = _filter.Text.Trim();
        _list.ItemsSource = q.Length == 0 ? all.ToList() : all.Where(s => s.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
    }

    private void Filter_KeyDown(object sender, KeyEventArgs e)
    {
        var count = _list.Items.Count;
        if (e.Key == Key.Down && count > 0) { _list.SelectedIndex = Math.Min(count - 1, _list.SelectedIndex + 1); e.Handled = true; }
        else if (e.Key == Key.Up && count > 0) { _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1); e.Handled = true; }
        else if (e.Key == Key.Enter && (_list.SelectedItem ?? (count > 0 ? _list.Items[0] : null)) is string s) { Choose(s); e.Handled = true; }
        else if (e.Key == Key.Escape) { _popup.IsOpen = false; e.Handled = true; }
    }

    private void ClosePopup()
    {
        if (_popup is not null) _popup.IsOpen = false;
    }

    private void Choose(string s)
    {
        SelectedItem = s;
        _popup.IsOpen = false;
    }
}

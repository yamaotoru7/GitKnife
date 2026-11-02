using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GitKnife.Controls;
using GitKnife.ViewModels.Dialogs;
using static GitKnife.Views.Dialogs.Form;

namespace GitKnife.Views.Dialogs;

/// <summary>
/// Custom Commands タブ（Preferences と Repository Settings で共用）。
/// 左に一覧（名前、説明、対象の 3 行。Shared は右上に人のアイコン）と + / −、右に編集欄と "Available variables:"。何も選んでいないときは右が空の面。
/// </summary>
internal sealed class CustomCommandsEditorView : UserControl
{
    private readonly CustomCommandsEditorViewModel _vm;

    public CustomCommandsEditorView(CustomCommandsEditorViewModel vm)
    {
        _vm = vm;
        DataContext = vm;
        var grid = new Grid { Height = 360 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(ListPane());
        var editor = Editor();
        Grid.SetColumn(editor, 2);
        grid.Children.Add(editor);
        Content = grid;
    }

    // ------------------------------------------------------------------ 左の一覧

    private FrameworkElement ListPane()
    {
        var list = new ListBox();
        AutomationProperties.SetAutomationId(list, "CustomCommandsListBox");
        list.SetResourceReference(BackgroundProperty, "Gk.Input.Background");
        list.SetResourceReference(BorderBrushProperty, "Gk.Input.Border");
        list.BorderThickness = new Thickness(1, 1, 1, 0);
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(CustomCommandsEditorViewModel.Items)));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(CustomCommandsEditorViewModel.Selected)));
        list.ItemTemplate = ItemTemplate();
        list.ItemContainerStyle = (Style)Application.Current.FindResource("Gk.SelectionRow");
        list.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);

        var add = PreferencesView.SmallButton("Icon.Plus", () => { });
        AutomationProperties.SetAutomationId(add, "AddCustomCommandDropDownButton");
        add.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = add, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
            foreach (var (id, title, _) in CustomCommandItemViewModel.Targets)
            {
                var target = id;
                var mi = new MenuItem { Header = $"Add {title} Custom Command" };
                mi.Click += (_, _) => _vm.Add(target);
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        };
        var remove = PreferencesView.SmallButton("Icon.Minus", () => _vm.RemoveSelected());
        AutomationProperties.SetAutomationId(remove, "RemoveCustomCommandButton");
        remove.SetBinding(IsEnabledProperty, new Binding(nameof(CustomCommandsEditorViewModel.HasSelection)));

        return PreferencesView.FramedList(list, add, remove);
    }

    private static DataTemplate ItemTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(MarginProperty, new Thickness(4, 2, 2, 3));
        var stack = new FrameworkElementFactory(typeof(StackPanel));
        foreach (var (path, dim) in new[] { (nameof(CustomCommandItemViewModel.Name), false), (nameof(CustomCommandItemViewModel.Details), true), (nameof(CustomCommandItemViewModel.TargetText), true) })
        {
            var t = new FrameworkElementFactory(typeof(TextBlock));
            t.SetBinding(TextBlock.TextProperty, new Binding(path));
            t.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            if (dim) t.SetValue(OpacityProperty, 0.75);
            stack.AppendChild(t);
        }
        root.AppendChild(stack);
        // Shared は右上に人のアイコン
        var share = new FrameworkElementFactory(typeof(System.Windows.Shapes.Path));
        share.SetValue(System.Windows.Shapes.Path.DataProperty, Icon("Icon.Shared"));
        share.SetResourceReference(StyleProperty, "Gk.IconPath");
        share.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Right);
        share.SetValue(VerticalAlignmentProperty, VerticalAlignment.Top);
        share.SetValue(MarginProperty, new Thickness(0, 2, 4, 0));
        share.SetBinding(VisibilityProperty, new Binding(nameof(CustomCommandItemViewModel.IsShared)) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        root.AppendChild(share);
        return new DataTemplate { VisualTree = root };
    }

    // ------------------------------------------------------------------ 右の編集欄

    private FrameworkElement Editor()
    {
        var outer = new Border();
        outer.SetResourceReference(Border.BackgroundProperty, "Gk.Panel.Background");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(244) });

        var form = new StackPanel { Margin = new Thickness(0, 14, 16, 0) };
        form.SetBinding(DataContextProperty, new Binding(nameof(CustomCommandsEditorViewModel.Selected)));
        form.SetBinding(VisibilityProperty, new Binding(nameof(CustomCommandsEditorViewModel.HasSelection)) { Source = _vm, Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });

        if (_vm.IsRepository)
        {
            // Local / Shared と、Shared のときだけ OS
            var local = BoolRadio("LocalRadioButton", "Local", nameof(CustomCommandItemViewModel.IsLocal));
            var shared = BoolRadio("SharedRadioButton", "Shared", nameof(CustomCommandItemViewModel.IsShared));
            var os = new ComboBox { Width = 80, ItemsSource = CustomCommandItemViewModel.OsValues, ItemTemplate = OsTemplate() };
            AutomationProperties.SetAutomationId(os, "OSComboBox");
            os.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(CustomCommandItemViewModel.Os)));
            var osLabel = new TextBlock { Text = "OS:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            AutomationProperties.SetAutomationId(osLabel, "OSTextBlock");
            var osPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { osLabel, os } };
            osPanel.SetBinding(VisibilityProperty, new Binding(nameof(CustomCommandItemViewModel.IsShared)) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
            var top = new DockPanel { LastChildFill = false };
            DockPanel.SetDock(osPanel, Dock.Right);
            top.Children.Add(osPanel);
            top.Children.Add(local);
            top.Children.Add(shared);
            form.Children.Add(Row(null, top));
            var location = new TextBlock { Opacity = 0.75, Margin = new Thickness(0, 0, 0, 4) };
            AutomationProperties.SetAutomationId(location, "LocationTextBlock");
            location.SetBinding(TextBlock.TextProperty, new Binding(nameof(CustomCommandItemViewModel.LocationText)));
            form.Children.Add(Row(null, location, 22));
        }

        var target = new ComboBox { Width = 100, HorizontalAlignment = HorizontalAlignment.Left, ItemsSource = CustomCommandItemViewModel.Targets.Select(t => t.id).ToList(), ItemTemplate = TargetTemplate() };
        AutomationProperties.SetAutomationId(target, "TargetsComboBox");
        target.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(CustomCommandItemViewModel.Target)));
        form.Children.Add(Row("Target:", target));
        var refs = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (text, path) in new[] { ("Local Branch", nameof(CustomCommandItemViewModel.RefLocal)), ("Remote Branch", nameof(CustomCommandItemViewModel.RefRemote)), ("Tag", nameof(CustomCommandItemViewModel.RefTag)) })
            refs.Children.Add(Check("", text, path).With(c => c.Margin = new Thickness(0, 0, 8, 0)));
        form.Children.Add(Row(null, refs, 26, nameof(CustomCommandItemViewModel.IsRef)));
        form.Children.Add(Row("Name:", WithHint(Text("", nameof(CustomCommandItemViewModel.Name)), nesting: true)));
        form.Children.Add(Row(null, Horizontal(
            BoolRadio("UiRadioButton", "UI", nameof(CustomCommandItemViewModel.IsUi)),
            BoolRadio("ProcessRadioButton", "Action", nameof(CustomCommandItemViewModel.IsAction))), 26));

        // Action のとき
        form.Children.Add(Row("Action:", ActionButton("ProcessActionButton", nameof(CustomCommandItemViewModel.ActionTitle), nameof(CustomCommandItemViewModel.ActionDetails),
            item => EditAction(item.Action, item.Target, allowCancel: false, a => item.Action = a)), visibleWhen: nameof(CustomCommandItemViewModel.IsAction)));

        // UI のとき
        form.Children.Add(Row("Title:", WithHint(Text("DialogTitleTextBox", nameof(CustomCommandItemViewModel.UiTitle))), visibleWhen: nameof(CustomCommandItemViewModel.IsUi)));
        form.Children.Add(Row("Description:", WithHint(Text("", nameof(CustomCommandItemViewModel.UiDescription))), visibleWhen: nameof(CustomCommandItemViewModel.IsUi)));
        form.Children.Add(Row("Controls:", ActionButton("UiControlsButton", nameof(CustomCommandItemViewModel.ControlsText), null, item =>
        {
            var vm = new EditCustomControlsDialogViewModel(item.Controls);
            if (DialogWindow.Run(vm, out _)) item.Controls = vm.Result;
        }), visibleWhen: nameof(CustomCommandItemViewModel.IsUi)));
        form.Children.Add(Row("Buttons:", ButtonRow(1), visibleWhen: nameof(CustomCommandItemViewModel.IsUi)));
        form.Children.Add(Row(null, ButtonRow(2), visibleWhen: nameof(CustomCommandItemViewModel.IsUi)));

        grid.Children.Add(form);

        var help = Variables();
        help.SetBinding(DataContextProperty, new Binding(nameof(CustomCommandsEditorViewModel.Selected)));
        help.SetBinding(VisibilityProperty, new Binding(nameof(CustomCommandsEditorViewModel.HasSelection)) { Source = _vm, Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        Grid.SetColumn(help, 1);
        grid.Children.Add(help);
        // 何も選んでいないときはロゴだけ。
        // form と help は DataContext を Selected にしているので、表示の切り替えは Source を VM にする（null だと束縛が効かず、空の入力欄が出ていた）
        var empty = new Image { Width = 88, Height = 88, Opacity = 0.25, Source = (ImageSource)Application.Current.FindResource("Gk.AppIcon"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(empty, "FallbackUserControl");
        empty.SetBinding(VisibilityProperty, new Binding(nameof(CustomCommandsEditorViewModel.HasSelection)) { Source = _vm, Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility"), ConverterParameter = "invert" });
        Grid.SetColumnSpan(empty, 2);
        grid.Children.Add(empty);
        outer.Child = grid;
        return outer;
    }

    /// <summary>
    /// bool に束縛した RadioButton。GroupName で組にすると、一覧で項目を替えたとき前の項目に値が書かれる（Shared Echo が UI に変わった）ので、
    /// 自分だけの組にして、相手の RadioButton は VM の通知（IsUi と IsAction など）で外す。
    /// </summary>
    private static RadioButton BoolRadio(string id, string content, string path)
    {
        var r = new RadioButton { Content = content, GroupName = "cc-" + id, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        r.SetResourceReference(ForegroundProperty, "Gk.Text");
        AutomationProperties.SetAutomationId(r, id);
        r.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(path));
        return r;
    }

    private static FrameworkElement Row(string? label, FrameworkElement element, double height = 32, string? visibleWhen = null)
    {
        var g = new Grid { MinHeight = height };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        if (label is not null) g.Children.Add(new TextBlock { Text = label, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        element.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(element, 1);
        g.Children.Add(element);
        if (visibleWhen is not null)
            g.SetBinding(VisibilityProperty, new Binding(visibleWhen) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        return g;
    }

    /// <summary>"Bash Command  Run bash Script  ..." の形の押せる欄（押すと編集のダイアログ）。</summary>
    private static Button ActionButton(string id, string titlePath, string? detailsPath, Action<CustomCommandItemViewModel> onClick)
    {
        var title = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) };
        title.SetBinding(TextBlock.TextProperty, new Binding(titlePath));
        if (detailsPath is null) title.FontWeight = FontWeights.Normal;
        var details = new TextBlock { Opacity = 0.75, TextTrimming = TextTrimming.CharacterEllipsis };
        if (detailsPath is not null) details.SetBinding(TextBlock.TextProperty, new Binding(detailsPath));
        var dots = new TextBlock { Text = "...", Margin = new Thickness(8, 0, 0, 0) };
        var dock = new DockPanel();
        DockPanel.SetDock(dots, Dock.Right);
        dock.Children.Add(dots);
        dock.Children.Add(title);
        dock.Children.Add(details);
        var b = new Button { Content = dock, Height = 24, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(8, 0, 6, 0) };
        b.SetResourceReference(StyleProperty, "Gk.Button");
        AutomationProperties.SetAutomationId(b, id);
        b.Click += (_, _) => { if (b.DataContext is CustomCommandItemViewModel item) onClick(item); };
        return b;
    }

    /// <summary>ボタン n の行：使うか、題、動作。</summary>
    private static FrameworkElement ButtonRow(int n)
    {
        var p = typeof(CustomCommandItemViewModel);
        var enabled = Check($"UiActionButton{n}CheckBox", "", $"Button{n}Enabled");
        enabled.VerticalAlignment = VerticalAlignment.Center;
        var title = Text(n == 2 ? "UiActionButton2TitleTextBox" : "", $"Button{n}Title");
        title.Width = 100;
        var action = ActionButton($"UiActionButton{n}", $"Button{n}ActionTitle", $"Button{n}ActionDetails", item =>
        {
            var current = n == 1 ? item.Button1Action : item.Button2Action;
            EditAction(current, item.Target, allowCancel: true, a => { if (n == 1) item.Button1Action = a; else item.Button2Action = a; });
        });
        action.Margin = new Thickness(6, 0, 0, 0);
        var dock = new DockPanel();
        dock.Children.Add(enabled);
        dock.Children.Add(title);
        dock.Children.Add(action);
        return dock;
    }

    private static void EditAction(Core.Settings.CustomCommandAction action, string target, bool allowCancel, Action<Core.Settings.CustomCommandAction> set)
    {
        var vm = new EditCustomActionDialogViewModel(action, target, allowCancel);
        if (DialogWindow.Run(vm, out _)) set(vm.Result);
    }

    /// <summary>
    /// 欄の右端の青い "?"。マウスを乗せると "Available variables:" と変数の表（Name は先頭に "Use '/' for nesting"）。
    /// </summary>
    private static FrameworkElement WithHint(TextBox box, bool nesting = false)
    {
        box.Padding = new Thickness(box.Padding.Left, box.Padding.Top, 18, box.Padding.Bottom);
        var tip = new StackPanel();
        if (nesting) tip.Children.Add(new TextBlock { Text = "Use '/' for nesting" });
        tip.Children.Add(new TextBlock { Text = "Available variables:" });
        tip.Children.Add(VariableRows(12));
        var mark = new TextBlock { Text = "?", FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0), ToolTip = new ToolTip { Content = tip }, Background = Brushes.Transparent };
        mark.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Accent");
        ToolTipService.SetInitialShowDelay(mark, 200);
        var grid = new Grid();
        grid.Children.Add(box);
        grid.Children.Add(mark);
        return grid;
    }

    /// <summary>右の "Available variables:"（2 列の表。"Obsolete:" の見出しも行の 1 つ）。</summary>
    internal static FrameworkElement Variables()
    {
        var panel = new StackPanel { Margin = new Thickness(8, 10, 4, 0) };
        var header = new TextBlock { Text = "Available variables:", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 14) };
        AutomationProperties.SetAutomationId(header, "DescriptionTextBlock");
        panel.Children.Add(header);
        panel.Children.Add(VariableRows(0));
        var border = new Border { BorderThickness = new Thickness(1, 0, 0, 0), Child = panel };
        border.SetResourceReference(Border.BorderBrushProperty, "Gk.Border");
        return border;
    }

    /// <summary>変数の 2 列の表（DataContext の Variables）。gap は列の間の余白（ツールチップでは広めに）。</summary>
    private static ItemsControl VariableRows(double gap)
    {
        var rows = new ItemsControl();
        rows.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(CustomCommandItemViewModel.Variables)));
        var g = new FrameworkElementFactory(typeof(Grid));
        var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c1.SetValue(ColumnDefinition.WidthProperty, new GridLength(100 + gap));
        var c2 = new FrameworkElementFactory(typeof(ColumnDefinition));
        g.AppendChild(c1);
        g.AppendChild(c2);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(CustomCommandVariableRow.Name)));
        name.SetValue(TextBlock.FontSizeProperty, 11.5);
        g.AppendChild(name);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(nameof(CustomCommandVariableRow.Text)));
        text.SetValue(Grid.ColumnProperty, 1);
        text.SetValue(TextBlock.FontSizeProperty, 11.5);
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        g.AppendChild(text);
        g.SetValue(MinHeightProperty, 16.0);
        rows.ItemTemplate = new DataTemplate { VisualTree = g };
        return rows;
    }

    private static DataTemplate OsTemplate() => TextTemplate(new OsText());
    private static DataTemplate TargetTemplate() => TextTemplate(new TargetTitle());

    private static DataTemplate TextTemplate(IValueConverter converter)
    {
        var t = new FrameworkElementFactory(typeof(TextBlock));
        t.SetBinding(TextBlock.TextProperty, new Binding { Converter = converter });
        return new DataTemplate { VisualTree = t };
    }

    private sealed class OsText : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => value switch { "windows" => "Windows", "macOS" => "MacOS", _ => "Any" };
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }

    private sealed class TargetTitle : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => CustomCommandItemViewModel.Targets.FirstOrDefault(t => t.id == value as string).title ?? value ?? "";
        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
    }
}

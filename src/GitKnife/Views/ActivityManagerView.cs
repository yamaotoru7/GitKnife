using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using GitKnife.Controls;
using GitKnife.ViewModels;
using ICSharpCode.AvalonEdit;

namespace GitKnife.Views;

/// <summary>
/// Activity Manager。ステータス欄の ShowActivityManagerToggleButton で開くポップアップ（1044×455）。
/// 左：ViewModeTabControl（All / User / Background）と JobListBox（NameTextBlock、FinishTimeTextBlock、DetailsTextBlock）、
/// 右：JobDetailsNameTextBlock、JobDetailsFinishTimeTextBlock（"succeeded 25 10 2026 08:57:33"）、JobDetailsOutputEditor。
/// </summary>
internal sealed class ActivityManagerView : UserControl
{
    private readonly ListBox _list;
    private readonly TabControl _modes;
    private readonly TextEditor _output;
    private readonly TextBlock _name;
    private readonly TextBlock _finish;
    private readonly TextBlock _noActivities;
    private readonly FrameworkElement _noSelection;
    private readonly FrameworkElement _details;
    private RepositoryViewModel? _repo;
    private ActivityJob? _shown;

    public ActivityManagerView()
    {
        AutomationProperties.SetAutomationId(this, "ActivityManagerUserControl");
        Width = 1044;
        Height = 455;
        var border = new Border { BorderThickness = new Thickness(1) };
        border.SetResourceReference(Border.BackgroundProperty, "Gk.Details.Background");
        border.SetResourceReference(Border.BorderBrushProperty, "Gk.Border");

        var root = new Grid();
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1) });
        root.ColumnDefinitions.Add(new ColumnDefinition());

        // 左：表示の切替と一覧
        var left = new DockPanel { Margin = new Thickness(10, 6, 0, 10) };
        _modes = new TabControl { HorizontalAlignment = HorizontalAlignment.Center };
        _modes.SetResourceReference(StyleProperty, "Gk.TabControl");
        AutomationProperties.SetAutomationId(_modes, "ViewModeTabControl");
        foreach (var h in new[] { "All", "User", "Background" })
        {
            var t = new TabItem { Header = h, Padding = new Thickness(0) };
            t.SetResourceReference(StyleProperty, "Gk.TabItem");
            _modes.Items.Add(t);
        }
        _modes.SelectedIndex = ModeToIndex(App.Settings.ActivityManagerViewMode);
        _modes.SelectionChanged += (_, e) =>
        {
            if (e.OriginalSource != _modes) return;
            App.Settings.ActivityManagerViewMode = IndexToMode(_modes.SelectedIndex);
            ApplyFilter();
        };
        DockPanel.SetDock(_modes, Dock.Top);
        left.Children.Add(_modes);
        _list = new ListBox { BorderThickness = new Thickness(0), Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 4, 0, 0) };
        AutomationProperties.SetAutomationId(_list, "JobListBox");
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, ScrollBarVisibility.Disabled);
        _list.ItemTemplate = JobTemplate();
        _list.ItemContainerStyle = ItemStyle();
        _list.SelectionChanged += (_, _) => Show(_list.SelectedItem as ActivityJob);
        var listHost = new Grid();
        listHost.Children.Add(_list);
        _noActivities = new TextBlock { Text = "No Activities", FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _noActivities.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        listHost.Children.Add(_noActivities);
        left.Children.Add(listHost);
        root.Children.Add(left);

        var line = new Border();
        line.SetResourceReference(Border.BackgroundProperty, "Gk.Border");
        Grid.SetColumn(line, 1);
        root.Children.Add(line);

        // 右：選んだジョブの詳細
        var right = new Grid { Margin = new Thickness(10, 8, 10, 10) };
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition());
        _name = new TextBlock { FontSize = 14, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        AutomationProperties.SetAutomationId(_name, "JobDetailsNameTextBlock");
        _finish = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontSize = 11.5 };
        _finish.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        AutomationProperties.SetAutomationId(_finish, "JobDetailsFinishTimeTextBlock");
        right.Children.Add(_name);
        right.Children.Add(_finish);
        _output = new TextEditor
        {
            IsReadOnly = true, FontSize = 12.5, Margin = new Thickness(0, 6, 0, 0), WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _output.SetResourceReference(FontFamilyProperty, "Gk.CodeFont");
        _output.SetResourceReference(BackgroundProperty, "Gk.Details.Background");
        _output.SetResourceReference(ForegroundProperty, "Gk.Text");
        _output.Options.EnableHyperlinks = false;
        _output.TextArea.TextView.LineTransformers.Add(new Dialogs.GitOutputColorizer());
        AutomationProperties.SetAutomationId(_output, "JobDetailsOutputEditor");
        Grid.SetRow(_output, 1);
        right.Children.Add(_output);
        _details = right;
        Grid.SetColumn(right, 2);
        root.Children.Add(right);

        var empty = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        empty.Children.Add(new Image { Width = 64, Height = 64, Opacity = 0.3, Source = (ImageSource)FindResource("Gk.AppIcon") });
        var emptyText = new TextBlock { Text = "No Selected Activity", FontSize = 15, Margin = new Thickness(0, 4, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        emptyText.SetResourceReference(TextBlock.ForegroundProperty, "Gk.Text.Dim");
        empty.Children.Add(emptyText);
        _noSelection = empty;
        Grid.SetColumn(empty, 2);
        root.Children.Add(empty);

        border.Child = root;
        Content = border;
        Show(null);
    }

    /// <summary>表示するリポジトリ（開くたびに今のタブを渡す）。いちばん新しいジョブを選ぶ。</summary>
    public void Attach(RepositoryViewModel? repo)
    {
        if (_repo is not null) _repo.Jobs.CollectionChanged -= Jobs_CollectionChanged;
        _repo = repo;
        if (repo is not null) repo.Jobs.CollectionChanged += Jobs_CollectionChanged;
        _list.ItemsSource = repo?.Jobs;
        ApplyFilter();
        _list.SelectedIndex = _list.Items.Count > 0 ? 0 : -1;
    }

    private void Jobs_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmpty();

    private void ApplyFilter()
    {
        if (_list.ItemsSource is null) { UpdateEmpty(); return; }
        var view = CollectionViewSource.GetDefaultView(_list.ItemsSource);
        var index = _modes.SelectedIndex;
        view.Filter = index switch
        {
            1 => o => o is ActivityJob { IsBackground: false },
            2 => o => o is ActivityJob { IsBackground: true },
            _ => null,
        };
        UpdateEmpty();
    }

    private void UpdateEmpty() => _noActivities.Visibility = _list.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void Show(ActivityJob? job)
    {
        if (_shown is not null) _shown.PropertyChanged -= Job_PropertyChanged;
        _shown = job;
        if (job is not null) job.PropertyChanged += Job_PropertyChanged;
        _details.Visibility = job is null ? Visibility.Collapsed : Visibility.Visible;
        _noSelection.Visibility = job is null ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    private void Job_PropertyChanged(object? sender, PropertyChangedEventArgs e) => Dispatcher.BeginInvoke(Refresh);

    private void Refresh()
    {
        if (_shown is not { } job) return;
        _name.Text = job.Name;
        _finish.Text = job.FinishText;
        if (_output.Text != job.OutputText) _output.Text = job.OutputText;
    }

    // 設定 ActivityManagerViewMode：User = 0、All = 1、Background = 2
    private static int ModeToIndex(int mode) => mode switch { 0 => 1, 2 => 2, _ => 0 };
    private static int IndexToMode(int index) => index switch { 1 => 0, 2 => 2, _ => 1 };

    private static DataTemplate JobTemplate()
    {
        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(MarginProperty, new Thickness(2, 1, 2, 3));
        var c0 = new FrameworkElementFactory(typeof(ColumnDefinition));
        var c1 = new FrameworkElementFactory(typeof(ColumnDefinition));
        c1.SetValue(ColumnDefinition.WidthProperty, GridLength.Auto);
        grid.AppendChild(c0);
        grid.AppendChild(c1);
        var r0 = new FrameworkElementFactory(typeof(RowDefinition));
        r0.SetValue(RowDefinition.HeightProperty, GridLength.Auto);
        var r1 = new FrameworkElementFactory(typeof(RowDefinition));
        r1.SetValue(RowDefinition.HeightProperty, GridLength.Auto);
        grid.AppendChild(r0);
        grid.AppendChild(r1);

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(ActivityJob.Name)));
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(AutomationProperties.AutomationIdProperty, "NameTextBlock");
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        grid.AppendChild(name);

        var right = new FrameworkElementFactory(typeof(StackPanel));
        right.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        right.SetValue(Grid.ColumnProperty, 1);
        var warn = new FrameworkElementFactory(typeof(IconImage));
        warn.SetValue(IconImage.GeometryProperty, Application.Current.FindResource("Icon.Warning"));
        warn.SetValue(AutomationProperties.AutomationIdProperty, "WarningImage");
        warn.SetValue(MarginProperty, new Thickness(0, 0, 4, 0));
        warn.SetBinding(VisibilityProperty, new Binding(nameof(ActivityJob.IsFailed)) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        right.AppendChild(warn);
        var busy = new FrameworkElementFactory(typeof(ProgressBar));
        busy.SetValue(WidthProperty, 12.0);
        busy.SetValue(HeightProperty, 12.0);
        busy.SetValue(ProgressBar.IsIndeterminateProperty, true);
        busy.SetValue(AutomationProperties.AutomationIdProperty, "BusyIndicator");
        busy.SetValue(MarginProperty, new Thickness(0, 0, 4, 0));
        busy.SetBinding(VisibilityProperty, new Binding(nameof(ActivityJob.IsRunning)) { Converter = (IValueConverter)Application.Current.FindResource("Gk.BoolToVisibility") });
        right.AppendChild(busy);
        var time = new FrameworkElementFactory(typeof(TextBlock));
        time.SetBinding(TextBlock.TextProperty, new Binding(nameof(ActivityJob.FinishTimeText)));
        time.SetValue(TextBlock.FontSizeProperty, 11.0);
        time.SetValue(AutomationProperties.AutomationIdProperty, "FinishTimeTextBlock");
        time.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        right.AppendChild(time);
        grid.AppendChild(right);

        var details = new FrameworkElementFactory(typeof(TextBlock));
        details.SetBinding(TextBlock.TextProperty, new Binding(nameof(ActivityJob.Details)));
        details.SetValue(Grid.RowProperty, 1);
        details.SetValue(Grid.ColumnSpanProperty, 2);
        details.SetValue(TextBlock.FontSizeProperty, 11.5);
        details.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        details.SetValue(AutomationProperties.AutomationIdProperty, "DetailsTextBlock");
        grid.AppendChild(details);
        return new DataTemplate { VisualTree = grid };
    }

    /// <summary>選んだ行はアクセント色の塗り。</summary>
    private static Style ItemStyle()
    {
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(ForegroundProperty, new DynamicResourceExtension("Gk.Text")));
        style.Setters.Add(new Setter(PaddingProperty, new Thickness(6, 2, 6, 2)));
        style.Setters.Add(new Setter(MarginProperty, new Thickness(0, 0, 0, 2)));
        var bd = new FrameworkElementFactory(typeof(Border), "Bd");
        bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
        bd.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        bd.SetBinding(Border.PaddingProperty, new Binding(nameof(Padding)) { RelativeSource = RelativeSource.TemplatedParent });
        bd.AppendChild(new FrameworkElementFactory(typeof(ContentPresenter)));
        var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = bd };
        var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Gk.List.Hover"), "Bd"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new DynamicResourceExtension("Gk.Selection"), "Bd"));
        selected.Setters.Add(new Setter(ForegroundProperty, Brushes.White));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(TemplateProperty, template));
        return style;
    }
}

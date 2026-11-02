using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using GitKnife.Controls;
using GitKnife.Core.Diff;
using GitKnife.ViewModels.Dialogs;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;

namespace GitKnife.Views.Dialogs;

/// <summary>
/// Merge Conflict Resolver の本文（左右のカード（AllRemoteCheckBox / AllLocalCheckBox）、
/// MergeStatusTextBlock "0/1"、RemoteMergeEditor / LocalMergeEditor / MergedMergeEditor、"editing allowed"）。
/// 左右のエディタで競合の部分を押すと、その側を結果に入れる／外す。
/// </summary>
internal sealed class MergeResolverView : UserControl
{
    private readonly MergeResolverViewModel _vm;
    private readonly TextEditor _theirs;
    private readonly TextEditor _ours;
    private readonly TextEditor _merged;
    private IReadOnlyList<MergeResolution.LineRange> _theirsRanges = [];
    private IReadOnlyList<MergeResolution.LineRange> _oursRanges = [];
    private IReadOnlyList<MergeResolution.LineRange> _mergedRanges = [];
    private bool _updating;

    public MergeResolverView(MergeResolverViewModel vm)
    {
        _vm = vm;
        Height = 740;
        _theirs = Editor("RemoteMergeEditor", readOnly: true);
        _ours = Editor("LocalMergeEditor", readOnly: true);
        _merged = Editor("MergedMergeEditor", readOnly: false);
        _theirs.TextArea.TextView.BackgroundRenderers.Add(new RangeRenderer(() => _theirsRanges, i => _vm.Resolution.ChoicesOf(i).Contains(MergeResolution.Side.Theirs), "Gk.Merge.Theirs"));
        _ours.TextArea.TextView.BackgroundRenderers.Add(new RangeRenderer(() => _oursRanges, i => _vm.Resolution.ChoicesOf(i).Contains(MergeResolution.Side.Ours), "Gk.Merge.Ours"));
        _merged.TextArea.TextView.BackgroundRenderers.Add(new RangeRenderer(() => _mergedRanges, i => _vm.Resolution.ChoicesOf(i).Count == 0, "Gk.Merge.Unresolved"));
        _theirs.TextArea.PreviewMouseLeftButtonUp += (_, _) => Pick(_theirs, _theirsRanges, MergeResolution.Side.Theirs);
        _ours.TextArea.PreviewMouseLeftButtonUp += (_, _) => Pick(_ours, _oursRanges, MergeResolution.Side.Ours);
        _merged.TextChanged += (_, _) => { if (!_updating) _vm.SetEditedText(_merged.Text); };
        // 左右のスクロールを合わせる
        _theirs.TextArea.TextView.ScrollOffsetChanged += (_, _) => { if (!_updating) { _updating = true; _ours.ScrollToVerticalOffset(_theirs.VerticalOffset); _updating = false; } };
        _ours.TextArea.TextView.ScrollOffsetChanged += (_, _) => { if (!_updating) { _updating = true; _theirs.ScrollToVerticalOffset(_ours.VerticalOffset); _updating = false; } };

        var root = new Grid { Margin = new Thickness(-20, -26, -20, 0) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // 見出し
        var title = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(22, 18, 0, 6) };
        title.Children.Add(new TextBlock { Text = "Merge", FontSize = 16, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(new IconImage { Geometry = Form.Icon("Icon.File"), Margin = new Thickness(10, 0, 4, 0) }.With(i => AutomationProperties.SetAutomationId(i, "FileIcon")));
        title.Children.Add(new TextBlock { Text = vm.Info.Path, VerticalAlignment = VerticalAlignment.Center }.With(t => AutomationProperties.SetAutomationId(t, "FilePathTextBlock")));
        root.Children.Add(title);

        // カード
        var cards = new Grid { Margin = new Thickness(20, 0, 20, 6) };
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        cards.Children.Add(Card("AllRemoteCheckBox", vm.Info.TheirsName, vm.Info.Theirs.FirstOrDefault()?.Subject, nameof(MergeResolverViewModel.AllTheirs)));
        var right = Card("AllLocalCheckBox", vm.Info.OursName, vm.Info.Ours.FirstOrDefault()?.Subject, nameof(MergeResolverViewModel.AllOurs));
        Grid.SetColumn(right, 1);
        cards.Children.Add(right);
        var status = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
        status.Children.Add(new TextBlock { Text = "Resolved ", Foreground = Brush("Gk.Text.Dim"), VerticalAlignment = VerticalAlignment.Center }.With(t => AutomationProperties.SetAutomationId(t, "ResolvedTextBlock")));
        var count = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        count.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(MergeResolverViewModel.StatusText)));
        AutomationProperties.SetAutomationId(count, "MergeStatusTextBlock");
        status.Children.Add(count);
        // 前後の競合へ（MergeStatusTextBlock の下の 2 つのボタン）
        status.Children.Add(NavButton("Icon.ChevronUp", -1));
        status.Children.Add(NavButton("Icon.ChevronDown", +1));
        Grid.SetColumn(status, 1);
        cards.Children.Add(status);
        Grid.SetRow(cards, 1);
        root.Children.Add(cards);

        // 左右と結果（並べ方は設定 MergerLayoutOrientation。0 は上に左右・下に結果、1 は横に 左・結果・右）
        _body = new Grid { Margin = new Thickness(20, 0, 20, 0) };
        Grid.SetRow(_body, 2);
        Grid.SetRowSpan(_body, 2);
        root.Children.Add(_body);
        ApplyLayout();
        // 左下の切り替えボタン
        var toggle = new Button { Width = 22, Height = 22, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(20, 4, 0, -30), ToolTip = "Change layout orientation",
            Content = new IconImage { Geometry = Form.Icon("Icon.Appearance") } };
        toggle.SetResourceReference(StyleProperty, "Gk.HeaderIconButton");
        AutomationProperties.SetAutomationId(toggle, "LayoutOrientationToggleButton");
        toggle.Click += (_, _) =>
        {
            App.Settings.MergerLayoutOrientation = App.Settings.MergerLayoutOrientation == 0 ? 1 : 0;
            ApplyLayout();
        };
        Grid.SetRow(toggle, 4);
        root.Children.Add(toggle);
        var editing = new TextBlock { Text = "editing allowed", HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("Gk.Text.Dim"), Margin = new Thickness(0, 4, 0, -30), FontSize = 11.5 };
        Grid.SetRow(editing, 4);
        root.Children.Add(editing);
        Content = root;

        vm.Changed += (_, _) => Rebuild();
        Loaded += (_, _) => Rebuild();
    }

    private readonly Grid _body;

    private void ApplyLayout()
    {
        _body.Children.Clear();
        _body.RowDefinitions.Clear();
        _body.ColumnDefinitions.Clear();
        Border Separator(int row, int column)
        {
            var sep = new Border { Background = Brush("Gk.Splitter") };
            Grid.SetRow(sep, row);
            Grid.SetColumn(sep, column);
            return sep;
        }
        void Place(UIElement e, int row, int column, int columnSpan = 1)
        {
            Grid.SetRow(e, row);
            Grid.SetColumn(e, column);
            Grid.SetColumnSpan(e, columnSpan);
            _body.Children.Add(e);
        }
        if (App.Settings.MergerLayoutOrientation == 1)
        {
            foreach (var w in new[] { 1.0, 0, 1.0, 0, 1.0 })
                _body.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1) : new GridLength(w, GridUnitType.Star) });
            Place(_theirs, 0, 0);
            _body.Children.Add(Separator(0, 1));
            Place(_merged, 0, 2);
            _body.Children.Add(Separator(0, 3));
            Place(_ours, 0, 4);
            return;
        }
        _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1) });
        _body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        foreach (var w in new[] { 1.0, 0, 1.0 })
            _body.ColumnDefinitions.Add(new ColumnDefinition { Width = w == 0 ? new GridLength(1) : new GridLength(w, GridUnitType.Star) });
        Place(_theirs, 0, 0);
        _body.Children.Add(Separator(0, 1));
        Place(_ours, 0, 2);
        var line = Separator(1, 0);
        Grid.SetColumnSpan(line, 3);
        _body.Children.Add(line);
        Place(_merged, 2, 0, 3);
    }

    private static SolidColorBrush Brush(string key) => (SolidColorBrush)Application.Current.FindResource(key);

    private static TextEditor Editor(string id, bool readOnly)
    {
        var e = new TextEditor { IsReadOnly = readOnly, ShowLineNumbers = true, FontSize = 13, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        e.SetResourceReference(Control.FontFamilyProperty, "Gk.CodeFont");
        e.SetResourceReference(Control.BackgroundProperty, "Gk.Diff.Background");
        e.SetResourceReference(Control.ForegroundProperty, "Gk.Text");
        e.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "Gk.Text.Faint");
        e.Options.HighlightCurrentLine = false;
        AutomationProperties.SetAutomationId(e, id);
        return e;
    }

    private FrameworkElement Card(string id, string branch, string? subject, string path)
    {
        var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0) };
        AutomationProperties.SetAutomationId(check, id);
        check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new System.Windows.Data.Binding(path));
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { new IconImage { Geometry = Form.Icon("Icon.Branch"), Margin = new Thickness(0, 0, 6, 0) }, new TextBlock { Text = branch, FontWeight = FontWeights.SemiBold } },
        });
        if (subject is not null) text.Children.Add(new TextBlock { Text = subject, Margin = new Thickness(18, 2, 0, 0) });
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Children = { check, text } };
        return new Border
        {
            Child = panel, Width = 340, Height = 44, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1), BorderBrush = Brush("Gk.Input.Border"), Background = Brush("Gk.Input.Background"),
        };
    }

    private int _current = -1;

    private Button NavButton(string icon, int direction)
    {
        var b = new Button { Width = 26, Height = 22, Margin = new Thickness(direction < 0 ? 8 : 2, 0, 0, 0), Content = new IconImage { Geometry = Form.Icon(icon) } };
        b.SetResourceReference(StyleProperty, "Gk.HeaderIconButton");
        b.Click += (_, _) => GoToConflict(direction);
        return b;
    }

    /// <summary>前後の競合の部分を 3 つのエディタで見える位置に。</summary>
    private void GoToConflict(int direction)
    {
        var count = _mergedRanges.Count;
        if (count == 0) return;
        _current = ((_current < 0 ? (direction > 0 ? -1 : 0) : _current) + direction + count) % count;
        Show(_theirs, _theirsRanges);
        Show(_ours, _oursRanges);
        Show(_merged, _mergedRanges);

        void Show(TextEditor e, IReadOnlyList<MergeResolution.LineRange> ranges)
        {
            if (_current >= ranges.Count) return;
            var line = Math.Clamp(ranges[_current].Start + 1, 1, Math.Max(1, e.Document.LineCount));
            e.ScrollTo(line, 0);
            e.TextArea.Caret.Line = line;
        }
    }

    private void Pick(TextEditor editor, IReadOnlyList<MergeResolution.LineRange> ranges, MergeResolution.Side side)
    {
        var line = editor.TextArea.Caret.Line - 1;
        for (var i = 0; i < ranges.Count; i++)
            if (ranges[i].Contains(line)) { _vm.Toggle(i, side); return; }
    }

    private void Rebuild()
    {
        _updating = true;
        (var t, _theirsRanges) = _vm.Resolution.Build(MergeResolution.Side.Theirs);
        (var o, _oursRanges) = _vm.Resolution.Build(MergeResolution.Side.Ours);
        if (_theirs.Text != t) _theirs.Document = new TextDocument(t);
        if (_ours.Text != o) _ours.Document = new TextDocument(o);
        if (!_vm.MergedEdited)
        {
            (var m, _mergedRanges) = _vm.Resolution.BuildMerged();
            var offset = _merged.VerticalOffset;
            _merged.Document = new TextDocument(m);
            _merged.ScrollToVerticalOffset(offset);
        }
        foreach (var e in new[] { _theirs, _ours, _merged }) e.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        _updating = false;
    }

    /// <summary>競合の部分の背景（選んでいる側は濃く、選んでいない側は枠だけ。結果の未解決は赤）。</summary>
    private sealed class RangeRenderer(Func<IReadOnlyList<MergeResolution.LineRange>> ranges, Func<int, bool> active, string brushKey) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext dc)
        {
            if (!textView.VisualLinesValid || textView.TryFindResource(brushKey) is not SolidColorBrush brush) return;
            var faint = new SolidColorBrush(Color.FromArgb(0xA0, brush.Color.R, brush.Color.G, brush.Color.B));   // 選んでいない側も色で塗る
            var list = ranges();
            for (var i = 0; i < list.Count; i++)
            {
                var r = list[i];
                Rect? rect = null;
                foreach (var vl in textView.VisualLines)
                {
                    var line = vl.FirstDocumentLine.LineNumber - 1;
                    if (!r.Contains(line)) continue;
                    var y = vl.VisualTop - textView.VerticalOffset;
                    var rr = new Rect(0, y, Math.Max(textView.ActualWidth, 1), vl.Height);
                    rect = rect is { } prev ? Rect.Union(prev, rr) : rr;
                }
                if (rect is { } box) dc.DrawRectangle(active(i) ? brush : faint, active(i) ? null : new Pen(brush, 1), box);
            }
        }
    }
}

internal static class ElementExtensions
{
    public static T With<T>(this T element, Action<T> setup)
    {
        setup(element);
        return element;
    }
}

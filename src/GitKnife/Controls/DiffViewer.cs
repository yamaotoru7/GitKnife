using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GitKnife.Core.Diff;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace GitKnife.Controls;

/// <summary>diff の上で選んだ行（ハンク番号 → hunk.Lines の添字）。</summary>
public sealed class DiffLinesEventArgs(IReadOnlyDictionary<int, HashSet<int>> lines) : EventArgs
{
    public IReadOnlyDictionary<int, HashSet<int>> Lines { get; } = lines;
}

/// <summary>
/// 1 ファイルの diff 表示（Side by side / Inline）。AvalonEdit を左右 2 つ（または 1 つ）並べる。
/// - 1 行目はハンク見出し。片側に無い行は灰色の詰め物
/// - 変更行をクリックすると、その変更のまとまり（連続する +/- 行）を青枠で選び、右上に Stage / Discard...（staged なら Unstage）を出す
/// - 複数行をドラッグで選ぶと、その範囲の変更行が対象になる
/// </summary>
public sealed class DiffViewer : Grid
{
    public static readonly DependencyProperty DiffProperty = DependencyProperty.Register(nameof(Diff), typeof(FileDiff), typeof(DiffViewer),
        new PropertyMetadata(null, (d, _) => ((DiffViewer)d).Rebuild()));
    public static readonly DependencyProperty SideBySideProperty = DependencyProperty.Register(nameof(SideBySide), typeof(bool), typeof(DiffViewer),
        new PropertyMetadata(true, (d, _) => ((DiffViewer)d).Rebuild()));
    public static readonly DependencyProperty IsStagedProperty = DependencyProperty.Register(nameof(IsStaged), typeof(bool), typeof(DiffViewer),
        new PropertyMetadata(false, (d, _) => ((DiffViewer)d).UpdateButtons()));

    public FileDiff? Diff { get => (FileDiff?)GetValue(DiffProperty); set => SetValue(DiffProperty, value); }
    public bool SideBySide { get => (bool)GetValue(SideBySideProperty); set => SetValue(SideBySideProperty, value); }
    public bool IsStaged { get => (bool)GetValue(IsStagedProperty); set => SetValue(IsStagedProperty, value); }

    public event EventHandler<DiffLinesEventArgs>? StageRequested;
    public event EventHandler<DiffLinesEventArgs>? UnstageRequested;
    public event EventHandler<DiffLinesEventArgs>? DiscardRequested;

    private readonly DiffPane _left;
    private readonly DiffPane _right;
    private readonly DiffPane _inline;
    private readonly Border _divider;
    private readonly StackPanel _buttons;
    private readonly Button _stageButton;
    private readonly Button _discardButton;
    private IReadOnlyList<DiffRow> _rows = [];
    private readonly SortedSet<int> _selected = [];
    private bool _syncing;

    internal IReadOnlyList<DiffRow> Rows => _rows;
    internal bool IsRowSelected(int row) => _selected.Contains(row);

    public DiffViewer()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _left = new DiffPane(this, DiffSide.Left);
        _right = new DiffPane(this, DiffSide.Right);
        _inline = new DiffPane(this, DiffSide.Both);
        _left.Editor.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _divider = new Border { Width = 1 };
        _divider.SetResourceReference(Border.BackgroundProperty, "Gk.Splitter");
        SetColumn(_left.Editor, 0);
        SetColumn(_divider, 1);
        SetColumn(_right.Editor, 2);
        SetColumnSpan(_inline.Editor, 3);
        Children.Add(_left.Editor);
        Children.Add(_divider);
        Children.Add(_right.Editor);
        Children.Add(_inline.Editor);

        _stageButton = MakeButton();
        _discardButton = MakeButton("Discard...");
        _stageButton.Click += (_, _) => Raise(IsStaged ? UnstageRequested : StageRequested);
        _discardButton.Click += (_, _) => Raise(DiscardRequested);
        _buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        _buttons.Children.Add(_stageButton);
        _buttons.Children.Add(_discardButton);
        SetColumn(_buttons, 2);
        Children.Add(_buttons);

        foreach (var p in new[] { _left, _right, _inline })
        {
            var pane = p;
            pane.Editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => OnScrolled(pane);
            pane.Editor.TextArea.PreviewMouseLeftButtonUp += (_, _) => Dispatcher.BeginInvoke(() => SelectFromEditor(pane));
            pane.Editor.TextArea.KeyUp += (_, e) => { if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown) SelectFromEditor(pane); };
        }
        Rebuild();
    }

    /// <summary>選んだまとまりに Stage / Discard... ボタンを出すか（コミット詳細では出さない）。</summary>
    public bool ShowActions { get; set; } = true;

    /// <summary>
    /// 中身の行数に合わせて高さを決める（コミット詳細の Commit モードで、ファイルごとに展開して縦に並べるとき）。
    /// 0 なら親に合わせる。値は最大の高さ。
    /// </summary>
    public double AutoHeightMax { get; set; }

    /// <summary>
    /// 長い行を折り返す（WordWrapToggleButton、設定 DiffWordWrap）。Side by side では左右の行の高さが揃わなくなることがある。
    /// </summary>
    public bool WordWrap
    {
        get => _left.Editor.WordWrap;
        set
        {
            foreach (var p in new[] { _left, _right, _inline })
            {
                p.Editor.WordWrap = value;
                p.Editor.HorizontalScrollBarVisibility = value ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto;
            }
        }
    }

    /// <summary>空白・タブ・改行の記号を出す（ShowHiddenSymbolsToggleButton、設定 DiffShowHiddenSymbols）。</summary>
    public bool ShowHiddenSymbols
    {
        get => _left.Editor.Options.ShowSpaces;
        set
        {
            foreach (var p in new[] { _left, _right, _inline })
            {
                p.Editor.Options.ShowSpaces = value;
                p.Editor.Options.ShowTabs = value;
                p.Editor.Options.ShowEndOfLine = value;
            }
        }
    }

    /// <summary>前／次の変更のまとまりへ移って選ぶ（Ctrl+Up / Ctrl+Down、PreviousButton / NextButton）。</summary>
    public void GoToChange(int direction)
    {
        if (_rows.Count == 0) return;
        var pane = SideBySide ? _right : _inline;
        var current = _selected.Count > 0 ? (direction > 0 ? _selected.Max : _selected.Min) : pane.Editor.TextArea.Caret.Line - 1;
        var currentBlock = current >= 0 && current < _rows.Count ? _rows[current].Block : -1;
        for (var i = current + direction; i >= 0 && i < _rows.Count; i += direction)
        {
            if (_rows[i].Kind != DiffRowKind.Change || _rows[i].Block == currentBlock) continue;
            var block = _rows[i].Block;
            _selected.Clear();
            for (var k = 0; k < _rows.Count; k++) if (_rows[k].Block == block) _selected.Add(k);
            pane.Editor.TextArea.Caret.Line = _selected.Min + 1;
            pane.Editor.ScrollTo(_selected.Min + 1, 0);
            foreach (var p in new[] { _left, _right, _inline }) p.Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            UpdateButtons();
            return;
        }
    }

    private static Button MakeButton(string text = "Stage")
    {
        var b = new Button { Content = text, Margin = new Thickness(2, 0, 0, 0), Padding = new Thickness(9, 1, 9, 2), MinHeight = 22 };
        b.SetResourceReference(StyleProperty, "Gk.Button");
        return b;
    }

    private void Raise(EventHandler<DiffLinesEventArgs>? handler)
    {
        var lines = DiffLayout.SelectedLines(_rows, _selected);
        if (lines.Count > 0) handler?.Invoke(this, new DiffLinesEventArgs(lines));
    }

    private void Rebuild()
    {
        if (_left is null) return;   // 構築中
        _selected.Clear();
        var diff = Diff;
        _rows = diff is null ? [] : SideBySide ? DiffLayout.SideBySide(diff) : DiffLayout.Inline(diff);
        var split = SideBySide;
        _left.Editor.Visibility = _right.Editor.Visibility = _divider.Visibility = split ? Visibility.Visible : Visibility.Collapsed;
        _inline.Editor.Visibility = split ? Visibility.Collapsed : Visibility.Visible;
        SetColumn(_buttons, split ? 2 : 0);
        SetColumnSpan(_buttons, split ? 1 : 3);
        if (split)
        {
            _left.SetRows(_rows);
            _right.SetRows(_rows);
            _inline.SetRows([]);
        }
        else
        {
            _inline.SetRows(_rows);
            _left.SetRows([]);
            _right.SetRows([]);
        }
        if (AutoHeightMax > 0)
        {
            // 行の高さはフォントから（AvalonEdit の既定の行の高さとほぼ同じ）。横スクロールバーの分を足す
            var lineHeight = _right.Editor.FontSize * _right.Editor.FontFamily.LineSpacing;
            Height = Math.Min(AutoHeightMax, _rows.Count * lineHeight + SystemParameters.HorizontalScrollBarHeight + 4);
        }
        UpdateButtons();
    }

    private void OnScrolled(DiffPane source)
    {
        if (_syncing) return;
        _syncing = true;
        var y = source.Editor.TextArea.TextView.VerticalOffset;
        foreach (var p in new[] { _left, _right })
            if (p != source && SideBySide) p.Editor.ScrollToVerticalOffset(y);
        _syncing = false;
        UpdateButtons();
    }

    /// <summary>エディタのキャレット・選択範囲から、選ぶ diff 行を決める。</summary>
    private void SelectFromEditor(DiffPane pane)
    {
        var area = pane.Editor.TextArea;
        _selected.Clear();
        if (_rows.Count > 0)
        {
            if (area.Selection.IsEmpty || area.Selection.StartPosition.Line == area.Selection.EndPosition.Line)
            {
                // 1 行：その変更のまとまり全体
                var row = area.Caret.Line - 1;
                if (row >= 0 && row < _rows.Count && _rows[row].Kind == DiffRowKind.Change)
                {
                    var block = _rows[row].Block;
                    for (var i = 0; i < _rows.Count; i++) if (_rows[i].Block == block) _selected.Add(i);
                }
            }
            else
            {
                var a = Math.Min(area.Selection.StartPosition.Line, area.Selection.EndPosition.Line) - 1;
                var b = Math.Max(area.Selection.StartPosition.Line, area.Selection.EndPosition.Line) - 1;
                for (var i = Math.Max(0, a); i <= Math.Min(b, _rows.Count - 1); i++)
                    if (_rows[i].Kind == DiffRowKind.Change) _selected.Add(i);
            }
        }
        foreach (var p in new[] { _left, _right, _inline }) p.Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        UpdateButtons();
    }

    /// <summary>選択枠の右上にボタンを置く。</summary>
    private void UpdateButtons()
    {
        if (_buttons is null) return;
        _stageButton.Content = IsStaged ? "Unstage" : "Stage";
        _discardButton.Visibility = IsStaged ? Visibility.Collapsed : Visibility.Visible;
        if (_selected.Count == 0 || !ShowActions) { _buttons.Visibility = Visibility.Collapsed; return; }
        var pane = SideBySide ? _right : _inline;
        var tv = pane.Editor.TextArea.TextView;
        var first = _selected.Min + 1;
        if (first > pane.Editor.Document.LineCount) { _buttons.Visibility = Visibility.Collapsed; return; }
        var top = tv.GetVisualTopByDocumentLine(first) - tv.VerticalOffset;
        // 選択枠の上端から 20px ほど下、枠の内側の右端
        var y = top + 20;
        _buttons.Margin = new Thickness(0, Math.Max(0, y), 24, 0);
        _buttons.Visibility = y < ActualHeight ? Visibility.Visible : Visibility.Collapsed;
    }
}

internal enum DiffSide { Left, Right, Both }

/// <summary>diff の片側（または Inline の 1 つ）のエディタ。行の背景、行番号、見出しの色をつける。</summary>
internal sealed class DiffPane
{
    private readonly DiffViewer _owner;
    public DiffSide Side { get; }
    public TextEditor Editor { get; }
    private IReadOnlyList<DiffRow> _rows = [];
    public IReadOnlyList<DiffRow> Rows => _rows;
    public DiffViewer Owner => _owner;

    public DiffPane(DiffViewer owner, DiffSide side)
    {
        _owner = owner;
        Side = side;
        Editor = new TextEditor
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            WordWrap = false,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontSize = App.Settings.CodeEditorFontSize,
            Padding = new Thickness(0),
        };
        // Preferences の "Font size:" を変えたら合わせる
        App.SettingsChanged += (_, _) =>
        {
            Editor.FontSize = App.Settings.CodeEditorFontSize;
            // "Show -+ marks" を変えたら行番号の欄の幅を測り直す
            foreach (var m in Editor.TextArea.LeftMargins) m.InvalidateMeasure();
        };
        Editor.SetResourceReference(Control.FontFamilyProperty, "Gk.CodeFont");
        Editor.SetResourceReference(Control.BackgroundProperty, "Gk.Diff.Background");
        Editor.SetResourceReference(Control.ForegroundProperty, "Gk.Text");
        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.HighlightCurrentLine = false;
        Editor.TextArea.SelectionCornerRadius = 0;
        Editor.TextArea.SetResourceReference(TextArea.SelectionBrushProperty, "Gk.Diff.TextSelection");
        Editor.TextArea.SelectionBorder = null;
        Editor.TextArea.SelectionForeground = null;
        Editor.TextArea.LeftMargins.Add(new DiffNumberMargin(this));
        Editor.TextArea.TextView.BackgroundRenderers.Add(new DiffBackgroundRenderer(this));
        Editor.TextArea.TextView.LineTransformers.Add(new DiffHeaderColorizer(this));
        Editor.TextArea.TextView.LineTransformers.Add(new DiffInlineColorizer(this));
        Editor.ContextMenu = new ContextMenu();
        Editor.ContextMenuOpening += (_, _) => Editor.ContextMenu = BuildMenu();
    }

    public void SetRows(IReadOnlyList<DiffRow> rows)
    {
        _rows = rows;
        var lines = rows.Select(r => r.Kind == DiffRowKind.HunkHeader ? r.HeaderText : Side switch
        {
            DiffSide.Left => r.Left?.Text ?? "",
            DiffSide.Right => r.Right?.Text ?? "",
            _ => (r.Left ?? r.Right)?.Text ?? "",
        });
        Editor.Document = new TextDocument(string.Join("\n", lines));
        Editor.SyntaxHighlighting = SyntaxColors.For(_owner.Diff?.NewPath ?? _owner.Diff?.OldPath);
        Editor.ScrollToHome();
    }

    /// <summary>本文の右クリック（選択が無いと History と Copy は押せない）。</summary>
    private ContextMenu BuildMenu()
    {
        var hasSelection = !Editor.TextArea.Selection.IsEmpty;
        var m = new ContextMenu();
        m.Items.Add(new Separator());
        var history = new MenuItem { Header = "History for Selected Lines", IsEnabled = hasSelection };
        history.Click += (_, _) => Views.Alerts.NotImplemented("History for Selected Lines");
        m.Items.Add(history);
        m.Items.Add(new Separator());
        var copy = new MenuItem { Header = "Copy", IsEnabled = hasSelection };
        copy.Click += (_, _) => Editor.Copy();
        m.Items.Add(copy);
        var patch = new MenuItem { Header = "Copy as Patch" };
        patch.Click += (_, _) => { if (_owner.Diff is { } d && PatchBuilder.BuildAll(d) is { } p) System.Windows.Clipboard.SetText(p); };
        m.Items.Add(patch);
        return m;
    }

    public DiffRow? RowAt(int documentLine) => documentLine >= 1 && documentLine <= _rows.Count ? _rows[documentLine - 1] : null;

    /// <summary>行の背景（null なら塗らない）。</summary>
    public Brush? BackgroundFor(DiffRow row)
    {
        if (row.Kind != DiffRowKind.Change) return null;
        var side = Side switch { DiffSide.Left => row.Left, DiffSide.Right => row.Right, _ => row.Left ?? row.Right };
        var key = side is null ? "Gk.Diff.Filler" : side.Kind == DiffLineKind.Added ? "Gk.Diff.Added" : "Gk.Diff.Removed";
        return Editor.TryFindResource(key) as Brush;
    }
}

/// <summary>行の背景と、選んだ変更のまとまりの青枠。</summary>
internal sealed class DiffBackgroundRenderer(DiffPane pane) : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (!textView.VisualLinesValid) return;
        var width = Math.Max(textView.ActualWidth, ((System.Windows.Controls.Primitives.IScrollInfo)textView).ExtentWidth) + textView.HorizontalOffset;
        Rect? selection = null;
        foreach (var vl in textView.VisualLines)
        {
            var line = vl.FirstDocumentLine.LineNumber;
            if (pane.RowAt(line) is not { } row) continue;
            var rect = new Rect(0, vl.VisualTop - textView.VerticalOffset, width, vl.Height);
            if (pane.BackgroundFor(row) is { } brush) dc.DrawRectangle(brush, null, rect);
            if (pane.Owner.IsRowSelected(line - 1)) selection = selection is { } s ? Rect.Union(s, rect) : rect;
        }
        if (selection is { } sel && textView.TryFindResource("Gk.Diff.SelectionBorder") is Brush border)
        {
            var fill = textView.TryFindResource("Gk.Diff.SelectionFill") as Brush;
            dc.DrawRoundedRectangle(fill, new Pen(border, 1.5), new Rect(sel.X + 1, sel.Y + 0.5, Math.Max(0, sel.Width - 2), Math.Max(0, sel.Height - 1)), 2, 2);
        }
    }
}

/// <summary>ハンク見出し（@@ ... @@）を淡い色にする。</summary>
internal sealed class DiffHeaderColorizer(DiffPane pane) : DocumentColorizingTransformer
{
    protected override void ColorizeLine(DocumentLine line)
    {
        if (pane.RowAt(line.LineNumber) is not { Kind: DiffRowKind.HunkHeader } || line.Length == 0) return;
        if (CurrentContext.TextView.TryFindResource("Gk.Diff.Hunk") is not Brush brush) return;
        ChangeLinePart(line.Offset, line.EndOffset, el => el.TextRunProperties.SetForegroundBrush(brush));
    }
}

/// <summary>
/// 変更行の中の、実際に変わった語（トークン）を濃い色で塗る（インラインハイライト）。
/// Side by side で左右が対になっている行だけ。
/// </summary>
internal sealed class DiffInlineColorizer(DiffPane pane) : DocumentColorizingTransformer
{
    private readonly Dictionary<DiffRow, (IReadOnlyList<TextRange> removed, IReadOnlyList<TextRange> added)> _cache = [];

    private IReadOnlyList<DiffRow>? _pairedFor;
    private readonly Dictionary<DiffRow, (string Old, string New)> _pairs = [];

    /// <summary>
    /// Inline 表示では削除行と追加行が別の行なので、同じまとまりの i 番目の削除行と i 番目の追加行を組にする
    /// （Inline でも変わった語に濃い地を付ける）。
    /// </summary>
    private void BuildPairs()
    {
        if (ReferenceEquals(_pairedFor, pane.Rows)) return;
        _pairedFor = pane.Rows;
        _pairs.Clear();
        foreach (var group in pane.Rows.Where(r => r.Kind == DiffRowKind.Change).GroupBy(r => (r.Hunk, r.Block)))
        {
            var removed = group.Where(r => r.Left is not null && r.Right is null).ToList();
            var added = group.Where(r => r.Right is not null && r.Left is null).ToList();
            for (var i = 0; i < Math.Min(removed.Count, added.Count); i++)
            {
                _pairs[removed[i]] = (removed[i].Left!.Text, added[i].Right!.Text);
                _pairs[added[i]] = (removed[i].Left!.Text, added[i].Right!.Text);
            }
        }
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (pane.RowAt(line.LineNumber) is not { Kind: DiffRowKind.Change } row) return;
        string oldText, newText;
        bool removedSide;
        if (pane.Side == DiffSide.Both)
        {
            BuildPairs();
            if (!_pairs.TryGetValue(row, out var pair)) return;
            (oldText, newText) = pair;
            removedSide = row.Left is not null;
        }
        else
        {
            if (row is not { Left: { } l, Right: { } r }) return;
            (oldText, newText) = (l.Text, r.Text);
            removedSide = pane.Side == DiffSide.Left;
        }
        if (!_cache.TryGetValue(row, out var ranges))
        {
            if (_cache.Count > 2000) _cache.Clear();
            _cache[row] = ranges = InlineDiff.Compute(oldText, newText);
        }
        var list = removedSide ? ranges.removed : ranges.added;
        if (list.Count == 0) return;
        if (CurrentContext.TextView.TryFindResource(removedSide ? "Gk.Diff.RemovedInline" : "Gk.Diff.AddedInline") is not Brush brush) return;
        foreach (var t in list)
        {
            var start = line.Offset + Math.Min(t.Start, line.Length);
            var end = line.Offset + Math.Min(t.Start + t.Length, line.Length);
            if (end > start) ChangeLinePart(start, end, el => el.BackgroundBrush = brush);
        }
    }
}

/// <summary>行番号（Side by side は片側の番号、Inline は旧と新の 2 列）。詰め物と見出しの行は空。</summary>
internal sealed class DiffNumberMargin(DiffPane pane) : AbstractMargin
{
    private double _digitWidth = 7;

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null) oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
        base.OnTextViewChanged(oldTextView, newTextView);
        if (newTextView is not null) newTextView.VisualLinesChanged += OnVisualLinesChanged;
    }

    private void OnVisualLinesChanged(object? sender, EventArgs e) { InvalidateMeasure(); InvalidateVisual(); }

    private int Digits()
    {
        var max = 1;
        foreach (var r in pane.Rows) max = Math.Max(max, Math.Max(r.Left?.Number ?? 0, r.Right?.Number ?? 0));
        return Math.Max(2, (int)Math.Floor(Math.Log10(max)) + 1);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var ft = Format("0");
        _digitWidth = ft.WidthIncludingTrailingWhitespace;
        var columns = pane.Side == DiffSide.Both ? 2 : 1;
        return new Size(columns * (Digits() * _digitWidth + 6) + 8 + MarkWidth, 0);
    }

    /// <summary>Preferences の "Show -+ marks"（設定 DiffShowChangeMarks）：行番号の右に + / -。</summary>
    private double MarkWidth => App.Settings.DiffShowChangeMarks ? _digitWidth + 2 : 0;

    private FormattedText Format(string s)
    {
        var tv = TextView;
        var typeface = new Typeface((FontFamily)pane.Editor.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var brush = (TryFindResource("Gk.Text.Faint") as Brush) ?? Brushes.Gray;
        return new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, pane.Editor.FontSize * 0.8, brush,
            tv is null ? 1.0 : VisualTreeHelper.GetDpi(tv).PixelsPerDip);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var tv = TextView;
        if (tv is null || !tv.VisualLinesValid) return;
        var colWidth = Digits() * _digitWidth + 6;
        // 右端の区切り線（行番号と本文の間に縦線）
        if (TryFindResource("Gk.Splitter") is Brush line)
            dc.DrawRectangle(line, null, new Rect(RenderSize.Width - 3, 0, 1, RenderSize.Height));
        foreach (var vl in tv.VisualLines)
        {
            if (pane.RowAt(vl.FirstDocumentLine.LineNumber) is not { } row || row.Kind == DiffRowKind.HunkHeader) continue;
            var y = vl.VisualTop - tv.VerticalOffset + 2;
            if (pane.Side == DiffSide.Both)
            {
                Draw(row.Left?.Number, 0);
                Draw(row.Right?.Number, colWidth);
            }
            else Draw(pane.Side == DiffSide.Left ? row.Left?.Number : row.Right?.Number, 0);
            if (MarkWidth > 0)
            {
                var kind = pane.Side switch { DiffSide.Left => row.Left?.Kind, DiffSide.Right => row.Right?.Kind, _ => (row.Right ?? row.Left)?.Kind };
                var mark = kind switch { DiffLineKind.Added => "+", DiffLineKind.Removed => "-", _ => null };
                if (mark is not null) dc.DrawText(Format(mark), new Point(RenderSize.Width - 3 - MarkWidth, y));
            }

            void Draw(int? n, double x)
            {
                if (n is not > 0) return;
                var ft = Format(n.Value.ToString(CultureInfo.InvariantCulture));
                dc.DrawText(ft, new Point(x + colWidth - 3 - ft.Width, y));
            }
        }
    }
}

using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using GitKnife.Controls;
using GitKnife.Core.Diff;
using GitKnife.Core.Git;
using GitKnife.ViewModels;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;

namespace GitKnife.Views;

/// <summary>
/// Blame。別窓 "app.cs - Blame"（1020×720）。
/// 右上に UndoButton / RedoButton と RevisionsComboBox（ファイルの履歴。最初はファイルを最後に変えたコミット）、
/// 本文は選んだリビジョンでのファイル全体にそのリビジョンの変更を挟んで並べ（消した行は赤、足した行は緑、行番号は旧と新の 2 列）、
/// 左の列に行ごとのコミット（まとまりの先頭に件名・日付・SHA）。SHA を押すとそのコミットの Blame に移る。上の時間軸は未実装。
/// </summary>
internal sealed class BlameWindow : ToolWindow
{
    private readonly RepositoryViewModel _repo;
    private readonly FileHistoryReader _reader;
    private readonly string _path;
    private readonly ComboBox _revisions;
    private readonly Button _undo;
    private readonly Button _redo;
    private readonly ProgressBar _busy;
    private readonly TextEditor _editor;
    private readonly Stack<HistoryItem> _back = new();
    private readonly Stack<HistoryItem> _forward = new();
    private IReadOnlyList<BlameRow> _rows = [];
    private bool _navigating;

    public BlameWindow(RepositoryViewModel repo, string path)
        : base("Blame", path, App.Settings.BlameWindowLocationState, 1020, 720)
    {
        _repo = repo;
        _reader = new FileHistoryReader(repo.Git, repo.Path);
        _path = path;

        _busy = new ProgressBar { Width = 14, Height = 14, IsIndeterminate = true, Margin = new Thickness(0, 0, 6, 0), Visibility = Visibility.Hidden };
        AutomationProperties.SetAutomationId(_busy, "BusyIndicator");
        _undo = NavButton("UndoButton", "Icon.ChevronLeft", () => Navigate(_back, _forward));
        _redo = NavButton("RedoButton", "Icon.ChevronRight", () => Navigate(_forward, _back));
        _revisions = new ComboBox { Width = 490, Height = 22, ItemTemplate = (DataTemplate)XamlReader.Parse(RevisionTemplate) };
        AutomationProperties.SetAutomationId(_revisions, "RevisionsComboBox");
        _revisions.SelectionChanged += async (_, e) =>
        {
            if (!_navigating && e.RemovedItems.Count > 0 && e.RemovedItems[0] is HistoryItem previous) { _back.Push(previous); _forward.Clear(); }
            await LoadBlameAsync();
        };
        foreach (var e in new FrameworkElement[] { _revisions, _redo, _undo, _busy })
        {
            DockPanel.SetDock(e, Dock.Right);
            e.VerticalAlignment = VerticalAlignment.Center;
            HeaderRight.Children.Add(e);
        }

        _editor = new TextEditor
        {
            IsReadOnly = true, ShowLineNumbers = false, WordWrap = false, FontSize = App.Settings.CodeEditorFontSize,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            BorderThickness = new Thickness(1),
        };
        _editor.SetResourceReference(FontFamilyProperty, "Gk.CodeFont");
        _editor.SetResourceReference(BackgroundProperty, "Gk.Diff.Background");
        _editor.SetResourceReference(ForegroundProperty, "Gk.Text");
        _editor.SetResourceReference(BorderBrushProperty, "Gk.Border");
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.HighlightCurrentLine = false;
        // Ctrl+F：エディタの検索パネル（SearchTextBox。ふだんは隠れている）。
        // GitKnife は AvalonEdit の検索パネル（見た目は AvalonEdit のまま）
        var search = ICSharpCode.AvalonEdit.Search.SearchPanel.Install(_editor);
        System.Windows.Automation.AutomationProperties.SetAutomationId(search, "PART_SearchPanelUserControl");
        search.SetResourceReference(ICSharpCode.AvalonEdit.Search.SearchPanel.MarkerBrushProperty, "Gk.Diff.TextSelection");
        // Ctrl+G：Go To Line（新しい方の行番号）
        InputBindings.Add(new System.Windows.Input.KeyBinding(new RelayAction(GoToLine), System.Windows.Input.Key.G, System.Windows.Input.ModifierKeys.Control));
        _editor.TextArea.LeftMargins.Insert(0, new BlameMargin(this));
        _editor.TextArea.LeftMargins.Insert(1, new NumberMargin(this));
        _editor.TextArea.TextView.BackgroundRenderers.Add(new RowRenderer(this));
        Body = new Border { Margin = new Thickness(20, 4, 20, 20), Child = _editor };

        Loaded += async (_, _) => await LoadAsync();
    }

    private IReadOnlyList<BlameRow> Rows => _rows;

    private Button NavButton(string id, string icon, Action click)
    {
        var b = new Button { Width = 22, Height = 22, Content = new IconImage { Geometry = (Geometry)FindResource(icon) }, IsEnabled = false };
        b.SetResourceReference(StyleProperty, "Gk.HeaderIconButton");
        AutomationProperties.SetAutomationId(b, id);
        b.Click += (_, _) => click();
        return b;
    }

    private void Navigate(Stack<HistoryItem> from, Stack<HistoryItem> to)
    {
        if (from.Count == 0 || _revisions.SelectedItem is not HistoryItem current) return;
        to.Push(current);
        _navigating = true;
        _revisions.SelectedItem = from.Pop();
        _navigating = false;
    }

    private async Task LoadAsync()
    {
        try
        {
            var history = (await _reader.ReadHistoryAsync(_path)).Select(h => new HistoryItem(h)).ToList();
            _revisions.ItemsSource = history;
            Timeline.SetCommits(history.Select(h => h.Entry.Date).ToList(), -1);
            Timeline.CommitClicked += (_, i) => _revisions.SelectedIndex = i;
            var last = await _reader.ReadLastRevisionAsync(_path);
            _navigating = true;
            _revisions.SelectedItem = history.FirstOrDefault(h => h.Entry.Sha == last) ?? history.FirstOrDefault();
            _navigating = false;
        }
        catch (GitException ex) { Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
    }

    /// <summary>選んだリビジョンの Blame（そのリビジョンの全体の diff、そのリビジョンと親の blame）。</summary>
    private async Task LoadBlameAsync()
    {
        _undo.IsEnabled = _back.Count > 0;
        _redo.IsEnabled = _forward.Count > 0;
        if (_revisions.SelectedItem is not HistoryItem item) return;
        Timeline.Selected = _revisions.SelectedIndex;
        _busy.Visibility = Visibility.Visible;
        try
        {
            var entry = item.Entry;
            var current = await _reader.BlameAsync(entry.Sha, entry.Path);
            FileDiff? diff = null;
            IReadOnlyList<BlameLine> parent = [];
            if (entry.Change != Core.Git.Model.FileChange.Added)
            {
                diff = await _reader.ReadDiffAsync(entry, hasParent: true, fullFile: true);
                parent = await _reader.BlameAsync(entry.Sha + "~", entry.OldPath ?? entry.Path);
            }
            if (_revisions.SelectedItem != item) return;
            _rows = BlameLayout.Build(diff, current, parent);
            _editor.Text = string.Join("\n", _rows.Select(r => r.Text));
            _editor.SyntaxHighlighting = GitKnife.Controls.SyntaxColors.For(_path);   // Blame も色分けする
            _editor.TextArea.TextView.Redraw();
        }
        catch (GitException ex) { Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError); }
        finally { _busy.Visibility = Visibility.Hidden; }
    }

    /// <summary>左の列の SHA を押した：そのコミットの Blame に移る。</summary>
    private void GoTo(string sha)
    {
        if (_revisions.ItemsSource is not IEnumerable<HistoryItem> items) return;
        if (items.FirstOrDefault(h => h.Entry.Sha == sha) is { } target) _revisions.SelectedItem = target;
    }

    private void GoToLine()
    {
        var dlg = new ViewModels.Dialogs.GoToLineDialogViewModel();
        if (!Dialogs.DialogWindow.Run(dlg, Dialogs.DialogViews.For(dlg))) return;
        // 消した行は新しい行番号を持たないので、新しい行番号がその数のいちばん近い行へ
        var index = _rows.ToList().FindIndex(r => r.NewNumber >= dlg.Line && r.Kind != Core.Diff.DiffLineKind.Removed);
        var line = index >= 0 ? index + 1 : _rows.Count;
        if (line < 1) return;
        _editor.ScrollTo(line, 1);
        _editor.TextArea.Caret.Line = line;
        _editor.TextArea.Caret.Column = 1;
        _editor.TextArea.Focus();
    }

    private BlameRow? RowAt(int lineNumber) => lineNumber >= 1 && lineNumber <= _rows.Count ? _rows[lineNumber - 1] : null;

    private const string RevisionTemplate = """
        <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:c="clr-namespace:GitKnife.Controls;assembly=GitKnife">
            <DockPanel>
                <TextBlock DockPanel.Dock="Right" Text="{Binding Date}" Margin="10,0,4,0" VerticalAlignment="Center"/>
                <c:Avatar Width="14" Height="14" NameText="{Binding Author}" Email="{Binding Email}" Margin="2,0,6,0" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding ShortSha}" Margin="0,0,8,0" VerticalAlignment="Center"/>
                <TextBlock Text="{Binding Subject}" TextTrimming="CharacterEllipsis" VerticalAlignment="Center"/>
            </DockPanel>
        </DataTemplate>
        """;

    // ------------------------------------------------------------------ 描画

    private static Typeface Face(Control c, FontWeight weight) => new((FontFamily)c.FindResource("Gk.UiFont"), FontStyles.Normal, weight, FontStretches.Normal);

    /// <summary>左の列：まとまりの先頭の行にコミットの件名・日付・SHA、まとまりの境目に線。</summary>
    private sealed class BlameMargin : AbstractMargin
    {
        private const double ColumnWidth = 330;
        private readonly BlameWindow owner;

        public BlameMargin(BlameWindow owner)
        {
            this.owner = owner;
            AutomationProperties.SetAutomationId(this, "BlameListBox");
        }

        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new System.Windows.Automation.Peers.FrameworkElementAutomationPeer(this);

        protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
        {
            if (oldTextView is not null) oldTextView.VisualLinesChanged -= Changed;
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null) newTextView.VisualLinesChanged += Changed;
        }

        private void Changed(object? sender, EventArgs e) => InvalidateVisual();

        protected override Size MeasureOverride(Size availableSize) => new(ColumnWidth, 0);

        protected override void OnRender(DrawingContext dc)
        {
            var tv = TextView;
            if (tv is null || !tv.VisualLinesValid) return;
            var text = owner.TryFindResource("Gk.Text") as Brush ?? Brushes.White;
            var dim = owner.TryFindResource("Gk.Text.Dim") as Brush ?? Brushes.Gray;
            var line = owner.TryFindResource("Gk.Border") as Brush ?? Brushes.DimGray;
            var pen = new Pen(line, 1);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            dc.DrawRectangle(line, null, new Rect(ColumnWidth - 1, 0, 1, RenderSize.Height));
            foreach (var vl in tv.VisualLines)
            {
                if (owner.RowAt(vl.FirstDocumentLine.LineNumber) is not { } row || row.Blame is not { } b) continue;
                var y = vl.VisualTop - tv.VerticalOffset;
                if (!row.StartsGroup) continue;
                dc.DrawLine(pen, new Point(0, y + 0.5), new Point(ColumnWidth - 1, y + 0.5));
                var top = y + 1;
                var sha = new FormattedText(b.ShortSha[..6], CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face(owner, FontWeights.Normal), 12, text, dpi);
                var date = new FormattedText(b.Time.ToString("d MMM yyyy", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face(owner, FontWeights.Normal), 12, text, dpi);
                var shaX = ColumnWidth - 22 - sha.Width;
                var dateX = shaX - 10 - date.Width;
                var summary = new FormattedText(b.Summary, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Face(owner, FontWeights.Normal), 12.5, text, dpi)
                { MaxTextWidth = Math.Max(10, dateX - 30), Trimming = TextTrimming.CharacterEllipsis, MaxLineCount = 1 };
                dc.DrawText(summary, new Point(22, top));
                dc.DrawText(date, new Point(dateX, top));
                dc.DrawText(sha, new Point(shaX, top));
                // ⇗：そのコミットを別ウィンドウで（行の右端のボタン）
                if (owner.TryFindResource("Icon.ExternalWindow") is Geometry icon)
                {
                    dc.PushTransform(new TranslateTransform(ColumnWidth - 18, top + 2));
                    dc.PushTransform(new ScaleTransform(12 / 16.0, 12 / 16.0));
                    dc.DrawGeometry(null, new Pen(dim, 1.3), icon);
                    dc.Pop();
                    dc.Pop();
                }
                // 作者の色の小さな四角（アバターの代わり）
                dc.DrawRoundedRectangle(new SolidColorBrush(Avatar.ColorFor(b.Email)), null, new Rect(4, top + 2, 12, 12), 2, 2);
            }
        }

        /// <summary>マウスの位置の行が属するまとまりのコミット。</summary>
        private BlameLine? BlameAt(MouseEventArgs e)
        {
            var tv = TextView;
            var vl = tv?.GetVisualLineFromVisualTop(e.GetPosition(tv).Y + tv.VerticalOffset);
            if (vl is null) return null;
            var n = vl.FirstDocumentLine.LineNumber;
            while (n > 1 && owner.RowAt(n) is { StartsGroup: false }) n--;
            return owner.RowAt(n)?.Blame;
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (BlameAt(e) is not { } b) return;
            // 右端の ⇗ は別ウィンドウ、それ以外はそのコミットの Blame へ
            if (e.GetPosition(this).X >= ColumnWidth - 20) RevisionWindow.OpenCommit(owner._repo, b.Sha, b.Author, b.Email, b.Time, b.Summary);
            else owner.GoTo(b.Sha);
        }

        /// <summary>行の右クリック：Open in Separate Window...、Reveal in GitKnife、---、Save as...、---、Copy Commit SHA、Copy Commit Info。</summary>
        protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonUp(e);
            if (BlameAt(e) is not { } b) return;
            var repo = owner._repo;
            var m = new ContextMenu();
            void Add(string header, Action action)
            {
                var mi = new MenuItem { Header = new AccessText { Text = header } };
                System.Windows.Automation.AutomationProperties.SetName(mi, header);
                mi.Click += (_, _) => action();
                m.Items.Add(mi);
            }
            Add("Open in Separate Window...", () => RevisionWindow.OpenCommit(repo, b.Sha, b.Author, b.Email, b.Time, b.Summary));
            Add("Reveal in GitKnife", () =>
            {
                repo.NavigateToCommit(b.Sha);
                Application.Current.MainWindow?.Activate();
            });
            m.Items.Add(new Separator());
            Add("Save as...", () => _ = RevisionFileMenus.SaveAs(repo, b.Sha, owner._path));
            m.Items.Add(new Separator());
            Add("Copy Commit SHA", () => RepositoryActions.Copy(b.Sha));
            Add("Copy Commit Info", () => RepositoryActions.Copy($"{b.ShortSha} {b.Summary}{Environment.NewLine}Author: {b.Author} <{b.Email}>{Environment.NewLine}Date: {b.Time:yyyy-MM-dd HH:mm:ss zzz}"));
            m.PlacementTarget = this;
            m.IsOpen = true;
            e.Handled = true;
        }
    }

    /// <summary>行番号（旧と新の 2 列。消した行は旧だけ、足した行は新だけ）。</summary>
    private sealed class NumberMargin(BlameWindow owner) : AbstractMargin
    {
        protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
        {
            if (oldTextView is not null) oldTextView.VisualLinesChanged -= Changed;
            base.OnTextViewChanged(oldTextView, newTextView);
            if (newTextView is not null) newTextView.VisualLinesChanged += Changed;
        }

        private void Changed(object? sender, EventArgs e) { InvalidateMeasure(); InvalidateVisual(); }

        private double ColumnWidth => Math.Max(2, owner.Rows.Count.ToString(CultureInfo.InvariantCulture).Length) * 7.5 + 8;

        protected override Size MeasureOverride(Size availableSize) => new(ColumnWidth * 2 + 6, 0);

        protected override void OnRender(DrawingContext dc)
        {
            var tv = TextView;
            if (tv is null || !tv.VisualLinesValid) return;
            var brush = owner.TryFindResource("Gk.Text.Faint") as Brush ?? Brushes.Gray;
            var line = owner.TryFindResource("Gk.Border") as Brush ?? Brushes.DimGray;
            var face = new Typeface((FontFamily)owner._editor.FontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            dc.DrawRectangle(line, null, new Rect(RenderSize.Width - 2, 0, 1, RenderSize.Height));
            foreach (var vl in tv.VisualLines)
            {
                if (owner.RowAt(vl.FirstDocumentLine.LineNumber) is not { } row) continue;
                var y = vl.VisualTop - tv.VerticalOffset + 1;
                void Draw(int n, double right)
                {
                    if (n <= 0) return;
                    var ft = new FormattedText(n.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, owner._editor.FontSize * 0.8, brush, dpi);
                    dc.DrawText(ft, new Point(right - ft.Width, y));
                }
                Draw(row.Kind == DiffLineKind.Added ? 0 : row.OldNumber, ColumnWidth - 4);
                Draw(row.Kind == DiffLineKind.Removed ? 0 : row.NewNumber, ColumnWidth * 2 - 4);
            }
        }
    }

    /// <summary>消した行と足した行の塗り。</summary>
    private sealed class RowRenderer(BlameWindow owner) : IBackgroundRenderer
    {
        public KnownLayer Layer => KnownLayer.Background;

        public void Draw(TextView textView, DrawingContext dc)
        {
            var added = owner.TryFindResource("Gk.Diff.Added") as Brush;
            var removed = owner.TryFindResource("Gk.Diff.Removed") as Brush;
            foreach (var vl in textView.VisualLines)
            {
                if (owner.RowAt(vl.FirstDocumentLine.LineNumber) is not { } row || row.Kind == DiffLineKind.Context) continue;
                var brush = row.Kind == DiffLineKind.Added ? added : removed;
                if (brush is null) continue;
                var y = vl.VisualTop - textView.VerticalOffset;
                dc.DrawRectangle(brush, null, new Rect(0, y, textView.ActualWidth + textView.HorizontalOffset, vl.Height));
            }
        }
    }
}

using System.Windows.Automation;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using GitKnife.ViewModels;

namespace GitKnife.Views;

public partial class NotificationBarView : UserControl
{
    private NotificationBarViewModel? _vm;

    public NotificationBarView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null) _vm.PropertyChanged -= OnChanged;
            _vm = e.NewValue as NotificationBarViewModel;
            if (_vm is not null) _vm.PropertyChanged += OnChanged;
            BuildText();
        };
    }

    private RepositoryViewModel? Repo => (Parent as FrameworkElement)?.DataContext as RepositoryViewModel;

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NotificationBarViewModel.Parts)) BuildText();
    }

    /// <summary>文をつなぐ（ブランチ名はリンクの色）。UIA の Name は文全体になる。</summary>
    private void BuildText()
    {
        Text.Inlines.Clear();
        foreach (var p in _vm?.Parts ?? [])
        {
            var run = new Run(p.Text);
            if (p.IsLink) run.SetResourceReference(TextElement.ForegroundProperty, "Gk.Link");
            Text.Inlines.Add(run);
        }
        AutomationProperties.SetName(Text, string.Concat((_vm?.Parts ?? []).Select(p => p.Text)));
    }

    private void Button1_Click(object sender, RoutedEventArgs e) => Do(_vm?.Action1);
    private void Button2_Click(object sender, RoutedEventArgs e) => Do(_vm?.Action2);
    private void Button3_Click(object sender, RoutedEventArgs e) => Do(_vm?.Action3);

    private void Do(NotificationAction? action)
    {
        if (Repo is not { } repo || _vm is null || action is null) return;
        switch (action)
        {
            case NotificationAction.Resolve:
            case NotificationAction.Commit:
                // 競合ファイルの解決とコミットは Local Changes で行う（Merge Conflict Resolver は未実装）
                repo.View = ViewModels.RepositoryView.LocalChanges;
                break;
            case NotificationAction.BisectBad or NotificationAction.BisectGood or NotificationAction.BisectSkip:
                if (_vm.CommandFor(action.Value) is { } bisect) RepositoryActions.RunBisect(repo, bisect);
                break;
            default:
                if (_vm.CommandFor(action.Value) is { } args) RepositoryActions.RunDirect(repo, action.ToString()!, args);
                break;
        }
    }

    private void Abort_Click(object sender, RoutedEventArgs e)
    {
        if (Repo is not { } repo || _vm is null) return;
        var (title, message, submit, args) = _vm.AbortInfo;
        if (!_vm.AbortNeedsConfirmation || Alerts.Confirm(title, message, submit)) RepositoryActions.RunDirect(repo, submit, args);
    }
}

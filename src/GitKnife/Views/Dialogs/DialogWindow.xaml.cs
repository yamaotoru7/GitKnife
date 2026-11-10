using System.Windows;
using GitKnife.Core.Git;
using GitKnife.Core.Logging;
using GitKnife.ViewModels.Dialogs;

namespace GitKnife.Views.Dialogs;

public partial class DialogWindow : Window
{
    private readonly DialogViewModel _vm;
    private CancellationTokenSource? _cts;
    /// <summary>競合で止まったときの git の出力（閉じた後で "Merge Conflict" を出す）。</summary>
    private GitResult? _conflict;

    private DialogWindow(DialogViewModel vm, FrameworkElement body)
    {
        InitializeComponent();
        WindowFrame.UseRoundCorners(this);
        _vm = vm;
        DataContext = vm;
        body.DataContext = vm;
        // 設定画面（Preferences、Repository Settings）も同じ配色（入力欄 #333333 と枠 #525252）
        // Workspaces も同じ（地 #2B2B2B、枠 #525252）
        if (vm.ShowHeader || vm is PreferencesViewModel or RepositorySettingsViewModel or WorkspacesDialogViewModel) UseDialogColors();
        // Submit の無いダイアログは Close（Cancel）が既定のボタン（青い枠）。Workspaces は灰色の枠のまま
        if (!vm.ShowSubmit && vm is not WorkspacesDialogViewModel) CancelBtn.IsDefault = true;
        Body.Content = body;
        // アイコン付きのダイアログの本文は最低 32（-6 の余白を引いて）。1 行のフォーム（Rename Stash 164、Apply Patch 162 など）はこれで決まる。
        // 本文の無い確認だけは見出しとアイコンの高さで決まる（1 行の題名で 146、2 行で 158）
        if (vm.ShowHeader && vm is not ConfirmDialogViewModel) Body.MinHeight = 38;
        // 確認の下の余白は 21（枠の 1 を含む）。ほかのダイアログは本文の高さで合わせてあるので変えていない
        if (vm is ConfirmDialogViewModel) Layout.Margin = new Thickness(20, 26, 20, 19);
        // Interactive Rebase も下の余白は 21（窓の高さ 720）
        if (vm is ViewModels.InteractiveRebaseViewModel) Layout.Margin = new Thickness(20, 26, 20, 19);
        Width = vm.DialogWidth;   // 束縛だと SizeToContent より先に決まらないことがあるので直接入れる
        if (vm.CanResize) UseResize(vm);
    }

    /// <summary>
    /// 大きさを変えられる窓（Workspaces：330×370、右下に点の三角）。本文は残りの高さいっぱい。
    /// 下の余白は 20（ほかのダイアログは 16）。最小の大きさは開いたときの大きさにする。
    /// </summary>
    private void UseResize(DialogViewModel vm)
    {
        SizeToContent = SizeToContent.Manual;
        if (!double.IsNaN(vm.DialogHeight)) Height = vm.DialogHeight;
        MinWidth = Width;
        MinHeight = Height;
        BodyRow.Height = new GridLength(1, GridUnitType.Star);
        Layout.Margin = new Thickness(20, 26, 20, 20);
        Grip.Visibility = Visibility.Visible;
    }

    private void Grip_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        Width = Math.Max(MinWidth, Width + e.HorizontalChange);
        Height = Math.Max(MinHeight, Height + e.VerticalChange);
    }

    /// <summary>
    /// アイコン付きのダイアログは本体より暗い地に、明るめの入力欄と枠（ボタンも同じ枠）。
    /// 中の部品は Gk.Input.* を見るので、このウィンドウの中だけ Gk.Dialog.* に差し替える（モーダルなので開いている間のテーマ切り替えは考えない）。
    /// </summary>
    private void UseDialogColors()
    {
        SetResourceReference(BackgroundProperty, "Gk.Dialog.Background");
        Resources["Gk.Input.Background"] = FindResource("Gk.Dialog.Input.Background");
        Resources["Gk.Input.Border"] = FindResource("Gk.Dialog.Input.Border");
    }

    /// <summary>ダイアログを出し、実行して成功したら true。持ち主はアクティブなウィンドウ（UIA ではその子 Window）。</summary>
    public static bool Run(DialogViewModel vm, FrameworkElement body)
    {
        var w = new DialogWindow(vm, body) { Owner = ActiveOwner };
        var ok = w.ShowDialog() == true;
        if (w._conflict is { } r)
            ShowOutput("Merge Conflict", "Automatic merge failed. Fix conflicts and then commit the result", Combine(r.StandardOutput, r.StandardError));
        return ok;
    }

    /// <summary>本文を DialogViews で作って出す（閉じた後で何もしないダイアログ用）。</summary>
    public static bool Run(DialogViewModel vm, out bool ok) => ok = Run(vm, DialogViews.For(vm));

    private static Window? ActiveOwner => Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current.MainWindow;

    /// <summary>git の出力を見せる（"Git Error"、"Merge Conflict"）。</summary>
    public static void ShowOutput(string title, string subtitle, string output)
    {
        var vm = new GitOutputDialogViewModel(title, subtitle, output);
        // 持ち主はメインウィンドウ（UIA で一番手前の子 Window になる）
        new DialogWindow(vm, DialogViews.For(vm)) { Owner = Application.Current.MainWindow ?? ActiveOwner }.ShowDialog();
    }

    private static string FirstLine(string text) => text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";

    private static string Combine(string a, string b) => string.Join("\n", new[] { a.Trim(), b.Trim() }.Where(s => s.Length > 0));

    /// <summary>SubmitButton を押したのと同じ（本文の操作がそのまま実行になる所。Open Repository の Browse...）。</summary>
    public void Submit() => Submit_Click(this, new RoutedEventArgs());

    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.CanSubmitNow) return;
        _cts = new CancellationTokenSource();
        var job = _vm.ActivityRepository?.StartJob(_vm.ActivityName);
        using var scope = job?.Enter();
        try
        {
            _vm.IsBusy = true;
            _vm.StatusIsWarning = false;
            _vm.StatusMessage = null;
            await _vm.SubmitAsync(_cts.Token);
            job?.Finish(true, _vm.ActivitySummary(job.OutputText));
            DialogResult = true;
        }
        catch (OperationCanceledException) { job?.Finish(false, "Canceled"); }
        catch (GitConflictException ex)
        {
            job?.Finish(false, "Merge conflict");
            _conflict = ex.Result;
            DialogResult = true;
        }
        catch (GitErrorMessageException ex)
        {
            job?.Finish(false, ex.Message);
            _vm.StatusMessage = null;
            ShowOutput("Git Error", "An unexpected error occured while performing the git request", ex.Message);
        }
        catch (GitException ex)
        {
            job?.Finish(false, FirstLine(ex.Result.StandardError.Length > 0 ? ex.Result.StandardError : ex.Result.StandardOutput));
            Log.Error($"{_vm.Title} failed: {ex.Message}");
            _vm.StatusMessage = null;
            Alerts.ShowGitError(ex.Result.CommandLine, ex.Result.StandardError.Length > 0 ? ex.Result.StandardError : ex.Result.StandardOutput);
        }
        catch (Exception ex)
        {
            job?.Finish(false, ex.Message);
            Log.Error($"{_vm.Title} failed: {ex}");
            Alerts.ShowGitError(_vm.Title, ex.Message);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        // 実行中なら git を止める（GitProcess がプロセスごと終わらせる）
        if (_vm.IsBusy) { _cts?.Cancel(); return; }
        if (!_vm.ConfirmCancel()) return;
        DialogResult = false;
    }
}

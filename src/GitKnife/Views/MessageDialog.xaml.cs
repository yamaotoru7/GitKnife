using System.Windows;

namespace GitKnife.Views;

public partial class MessageDialog : Window
{
    public MessageDialog(string title, string message, string submitText, bool showCancel, string? detail = null)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        SubmitButton.Content = submitText;
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrEmpty(detail))
        {
            DetailText.Text = detail;
            DetailText.Visibility = Visibility.Visible;
        }
    }

    private void Submit_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}

/// <summary>ViewModel から出す確認・エラーのダイアログ。持ち主はアクティブなウィンドウ（UIA ではその子 Window として見える）。</summary>
public static class Alerts
{
    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    /// <summary>確認（題名と説明だけ）。</summary>
    public static bool Confirm(string title, string message, string submitText, double width = 590, string cancelText = "Cancel")
        => Dialogs.DialogWindow.Run(new ViewModels.Dialogs.ConfirmDialogViewModel(title, message, submitText, width, cancelText), out _);

    /// <summary>まだ作っていないメニュー項目を押したとき。</summary>
    public static void NotImplemented(string what)
        => new MessageDialog("Not implemented yet", $"'{what.TrimEnd('.')}' is not implemented in GitKnife yet.", "OK", showCancel: false) { Owner = Owner }.ShowDialog();

    /// <summary>git 以外の失敗（Import from Fork など、GitKnife だけの機能）。</summary>
    public static void ShowError(string title, string message)
        => new MessageDialog(title, message, "OK", showCancel: false) { Owner = Owner }.ShowDialog();

    /// <summary>知らせるだけ（Check for Updates の「最新です」など）。</summary>
    public static void ShowInfo(string title, string message)
        => new MessageDialog(title, message, "OK", showCancel: false) { Owner = Owner }.ShowDialog();

    /// <summary>git が失敗したとき（"Git Error"）。</summary>
    public static void ShowGitError(string command, string output)
        => Dialogs.DialogWindow.ShowOutput("Git Error", "An unexpected error occured while performing the git request", (output.Trim() + "\ngit command: " + command).Trim());
}

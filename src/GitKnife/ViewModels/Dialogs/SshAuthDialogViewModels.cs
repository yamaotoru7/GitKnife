using CommunityToolkit.Mvvm.ComponentModel;
using GitKnife.Core.Git;

namespace GitKnife.ViewModels.Dialogs;

/// <summary>
/// SSH の接続のパスワード・鍵のパスフレーズ（ほかの質問も）を聞く。ssh の質問の文言をそのまま見出しの下に出す。
/// Remember は Windows の資格情報マネージャーに保存する（パスワードとパスフレーズだけ）。前の答えが通らなかったときは警告を出す。
/// </summary>
public sealed partial class SshPasswordDialogViewModel : DialogViewModel
{
    private readonly AskPassRequest _request;

    public SshPasswordDialogViewModel(AskPassRequest request)
    {
        _request = request;
        if (request.Retry)
        {
            StatusIsWarning = true;
            StatusMessage = request.Question.Kind == AskPassKind.Passphrase ? "Wrong passphrase, please try again" : "Permission denied, please try again";
        }
    }

    public AskPassKind Kind => _request.Question.Kind;

    public override string Title => Kind switch
    {
        AskPassKind.Password => "SSH Password",
        AskPassKind.Passphrase => "SSH Key Passphrase",
        _ => "SSH Authentication",
    };

    public override string Subtitle => Kind switch
    {
        AskPassKind.Password => $"Enter the password for {_request.Question.Destination}",
        AskPassKind.Passphrase => $"Enter the passphrase of the key to connect to {_request.Question.Destination}",
        _ => $"{_request.Question.Destination} asks for authentication",
    };

    public override string SubmitText => "Connect";
    public override double DialogWidth => 520;

    /// <summary>ssh の質問の文言（"user@host's password:" など。末尾の空白は除く）。</summary>
    public string PromptText => _request.Question.Prompt.Trim();

    public string FieldLabel => Kind switch
    {
        AskPassKind.Password => "Password:",
        AskPassKind.Passphrase => "Passphrase:",
        _ => "Response:",
    };

    /// <summary>PasswordBox は値を束縛できないので、画面が変わるたびに入れる。</summary>
    public string Password { get; set; } = "";

    [ObservableProperty] private bool _remember = true;

    public bool CanRemember => _request.Question.IsSecret;

    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

/// <summary>知らないホストの鍵の確認（ssh の "The authenticity of host … can't be established." と指紋）。Connect で yes、Cancel で no。</summary>
public sealed class SshHostKeyDialogViewModel(AskPassQuestion question) : DialogViewModel
{
    public override string Title => "Unknown SSH Host";
    public override string Subtitle => $"The authenticity of {question.Destination} can't be established";
    public override string SubmitText => "Connect";
    public override double DialogWidth => 600;

    /// <summary>ssh の文言（指紋を含む）。最後の "Are you sure …?" の行は Connect のボタンが代わるので除く。</summary>
    public string Details => string.Join("\n", question.Prompt.Replace("\r\n", "\n").Split('\n')
        .Select(l => l.TrimEnd())
        .Where(l => l.Length > 0 && !l.Contains("(yes/no", StringComparison.OrdinalIgnoreCase)));

    public string NoteText => "Connect adds the host key to ~/.ssh/known_hosts. Check the fingerprint with the server administrator if you are not sure.";

    public override Task SubmitAsync(CancellationToken ct) => Task.CompletedTask;
}

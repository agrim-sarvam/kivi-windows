using System;
using System.Threading.Tasks;
using System.Windows;
using Kivi.Platform.Auth;
using Button = System.Windows.Controls.Button;

namespace Kivi.App.Views.Auth;

/// <summary>
/// Scoped-down sign-in gate (map §3.2's spirit, without the OTP/recovery/linking state machine —
/// see CLAUDE.md task notes). A "Sign in with Google" card + a "skip / use local" escape hatch so
/// anonymous local-endpoint use is never blocked by a mandatory sign-in wall (mirrors the
/// reference's <c>authGateDestination</c> "auth==nil is never a wall" spirit — the only reason
/// this app needs auth at all is to reach the hosted QA endpoint, not local dev).
/// </summary>
public partial class SignInScreen : Window
{
    private readonly AuthController _auth;
    private OtpFlowHandle? _otpHandle;

    /// <summary>Set before <see cref="Window.Close"/>: true = signed in, false = skipped/anonymous.</summary>
    public bool SignedIn { get; private set; }

    public SignInScreen(AuthController auth)
    {
        _auth = auth;
        InitializeComponent();
    }

    // ---- Email OTP: step 1, send code ----

    private async void SendCodeButton_Click(object sender, RoutedEventArgs e)
    {
        var email = EmailTextBox.Text.Trim();
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
        {
            ShowError("Enter a valid email address.");
            return;
        }

        await SendCodeAsync(email).ConfigureAwait(true);
    }

    private async Task SendCodeAsync(string email)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "sending code...";
        SetBusy(true);

        try
        {
            var handle = await _auth.StartEmailOtpAsync(email).ConfigureAwait(true);
            _otpHandle = handle;

            EmailStep.Visibility = Visibility.Collapsed;
            CodeStep.Visibility = Visibility.Visible;
            CodeSentToText.Text = $"we sent a 6-digit code to {email}";
            CodeTextBox.Text = string.Empty;
            StatusText.Visibility = Visibility.Collapsed;
            CodeTextBox.Focus();
        }
        catch (Exception ex)
        {
            StatusText.Visibility = Visibility.Collapsed;
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ---- Email OTP: step 2, verify code ----

    private async void VerifyCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_otpHandle is not { } handle)
        {
            ShowError("Send a code first.");
            return;
        }

        var code = CodeTextBox.Text.Trim();
        if (code.Length == 0)
        {
            ShowError("Enter the 6-digit code from your email.");
            return;
        }

        ErrorText.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "verifying...";
        SetBusy(true);

        SignInResult result;
        try
        {
            result = await _auth.SubmitEmailOtpAsync(handle, code).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            result = new SignInResult(SignInOutcome.Failed, ex.Message);
        }

        SetBusy(false);
        StatusText.Visibility = Visibility.Collapsed;

        switch (result.Outcome)
        {
            case SignInOutcome.Success:
                SignedIn = true;
                Close();
                return;

            case SignInOutcome.InvalidCode:
                ShowError(result.ErrorMessage ?? "That code is invalid or has expired. Try resending.");
                return;

            case SignInOutcome.Cancelled:
                return;

            case SignInOutcome.Failed:
            default:
                ShowError(result.ErrorMessage ?? "Sign-in failed. Please try again.");
                return;
        }
    }

    private async void ResendCodeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_otpHandle is not { } handle) return;
        await SendCodeAsync(handle.Email).ConfigureAwait(true);
    }

    private void UseDifferentEmailButton_Click(object sender, RoutedEventArgs e)
    {
        _otpHandle = null;
        ErrorText.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Collapsed;
        CodeStep.Visibility = Visibility.Collapsed;
        EmailStep.Visibility = Visibility.Visible;
        EmailTextBox.Focus();
    }

    private void SetBusy(bool busy)
    {
        SendCodeButton.IsEnabled = !busy;
        VerifyCodeButton.IsEnabled = !busy;
        ResendCodeButton.IsEnabled = !busy;
        UseDifferentEmailButton.IsEnabled = !busy;
        EmailTextBox.IsEnabled = !busy;
        CodeTextBox.IsEnabled = !busy;
        GoogleButton.IsEnabled = !busy;
        SkipButton.IsEnabled = !busy;
    }

    // ---- Google OAuth (existing path — left as-is, still blocked by the Kratos return_to gap) ----

    private async void GoogleButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        StatusText.Visibility = Visibility.Visible;
        StatusText.Text = "waiting for you to sign in in your browser...";
        GoogleButton.IsEnabled = false;
        SkipButton.IsEnabled = false;

        SignInResult result;
        try
        {
            result = await _auth.SignInWithGoogleAsync();
        }
        catch (Exception ex)
        {
            result = new SignInResult(SignInOutcome.Failed, ex.Message);
        }

        GoogleButton.IsEnabled = true;
        SkipButton.IsEnabled = true;
        StatusText.Visibility = Visibility.Collapsed;

        switch (result.Outcome)
        {
            case SignInOutcome.Success:
                SignedIn = true;
                Close();
                return;

            case SignInOutcome.AccountLinkingRequired:
                ShowError(result.ErrorMessage ??
                    "An account with this email already exists. Sign in with your password first, then link Google from settings.");
                return;

            case SignInOutcome.Cancelled:
                // Silent — the user re-tapped or the flow was superseded.
                return;

            case SignInOutcome.Failed:
            default:
                ShowError(result.ErrorMessage ?? "Sign-in failed. Please try again.");
                return;
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }

    private void SkipButton_Click(object sender, RoutedEventArgs e)
    {
        SignedIn = false;
        Close();
    }
}

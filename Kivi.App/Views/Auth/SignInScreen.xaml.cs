using System;
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

    /// <summary>Set before <see cref="Window.Close"/>: true = signed in, false = skipped/anonymous.</summary>
    public bool SignedIn { get; private set; }

    public SignInScreen(AuthController auth)
    {
        _auth = auth;
        InitializeComponent();
    }

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

using Kivi.Core.Contracts;

namespace Kivi.Platform.Auth;

/// <summary>Config for the two Kratos/org-service backends (map §4).</summary>
public sealed record AuthConfig(Uri KratosUrl, Uri OrgServiceUrl)
{
    public static AuthConfig Default { get; } = new(
        KratosUrl: new Uri("https://login.sarvam.ai/identity/"),
        OrgServiceUrl: new Uri("https://auth.sarvam.ai/"));
}

/// <summary>Outcome of <see cref="AuthController.SignInWithGoogleAsync"/> / the email-OTP path.</summary>
public enum SignInOutcome { Success, AccountLinkingRequired, Cancelled, Failed, InvalidCode }

public sealed record SignInResult(SignInOutcome Outcome, string? ErrorMessage = null);

/// <summary>
/// Handle the UI holds between <see cref="AuthController.StartEmailOtpAsync"/> (send code) and
/// <see cref="AuthController.SubmitEmailOtpAsync"/> (verify code) — carries whatever flow
/// id/action/email is needed to submit the second step against the same Kratos flow.
/// </summary>
public sealed record OtpFlowHandle(string FlowId, string ActionUrl, string Email);

/// <summary>Secret-store keys (map §3.6) — Kratos-only subset for this pass.</summary>
internal static class AuthSecretKeys
{
    public const string KratosSessionToken = "kratosSessionToken";
    public const string OrgServiceJwt = "orgServiceJWT";
    public const string KratosUserId = "kratosUserID";
    public const string KratosEmail = "kratosEmail";
    public const string KratosDisplayName = "kratosDisplayName";
}

/// <summary>
/// Facade orchestrating loopback OAuth + Kratos + org-JWT mint + DPAPI persistence (map §3.3).
/// This is what feeds the wire client's bearer and what the sign-in UI drives.
///
/// Kept free of WPF — pure orchestration over <see cref="KratosAuthClient"/>,
/// <see cref="OrgJwtClient"/>, <see cref="LoopbackOAuthListener"/>, and <see cref="ISecretStore"/>.
/// </summary>
public sealed class AuthController
{
    private readonly KratosAuthClient _kratos;
    private readonly KratosOtpAuthClient? _kratosOtp;
    private readonly OrgJwtClient _orgJwt;
    private readonly ISecretStore _secrets;
    private readonly Func<IOAuthLoopbackListener> _listenerFactory;
    private readonly Action<string> _openBrowser;

    private string? _kratosSessionToken;

    public AuthController(
        KratosAuthClient kratos,
        OrgJwtClient orgJwt,
        ISecretStore secrets,
        Func<IOAuthLoopbackListener>? listenerFactory = null,
        Action<string>? openBrowser = null,
        KratosOtpAuthClient? kratosOtp = null)
    {
        _kratos = kratos;
        _orgJwt = orgJwt;
        _secrets = secrets;
        _listenerFactory = listenerFactory ?? LoopbackOAuthListener.StartNew;
        _openBrowser = openBrowser ?? kratos.OpenInBrowser;
        _kratosOtp = kratosOtp;
    }

    public bool IsSignedIn { get; private set; }
    public string? UserId { get; private set; }
    public string? Email { get; private set; }
    public string? DisplayName { get; private set; }

    /// <summary>
    /// Startup check: if a saved Kratos session token exists, validate it via whoami. 401 ⇒ dead,
    /// clear it and stay signed-out. Any other outcome (alive or degraded network/5xx/403) ⇒ stay
    /// signed-in with whatever cached identity we have.
    /// </summary>
    public async Task RestoreSessionAsync(CancellationToken ct = default)
    {
        var token = _secrets.Read(AuthSecretKeys.KratosSessionToken);
        if (string.IsNullOrEmpty(token))
        {
            IsSignedIn = false;
            return;
        }

        var who = await _kratos.WhoamiAsync(token, ct).ConfigureAwait(false);
        if (who.IsDead)
        {
            ClearStoredTokens();
            IsSignedIn = false;
            return;
        }

        _kratosSessionToken = token;
        IsSignedIn = true;
        UserId = who.UserId ?? _secrets.Read(AuthSecretKeys.KratosUserId);
        Email = who.Email ?? _secrets.Read(AuthSecretKeys.KratosEmail);
        DisplayName = who.DisplayName ?? _secrets.Read(AuthSecretKeys.KratosDisplayName);

        // Seed the JWT cache if we have a previously minted one still valid enough to try; the
        // first GetCurrentBearerAsync call will re-mint if it's actually expired.
        var savedJwt = _secrets.Read(AuthSecretKeys.OrgServiceJwt);
        if (!string.IsNullOrEmpty(savedJwt))
            _orgJwt.Seed(savedJwt, DateTimeOffset.UtcNow); // treat as "expiring now" — forces a remint check
    }

    /// <summary>
    /// Runs the full loopback → Kratos → JWT flow. Opens the browser for the user; awaits the
    /// callback. On success, persists all tokens. Distinguishes account-linking-required (missing
    /// <c>code</c> ⇒ email collision) from a generic failure.
    /// </summary>
    public async Task<SignInResult> SignInWithGoogleAsync(CancellationToken ct = default)
    {
        using var listener = _listenerFactory();
        try
        {
            var actionUrl = await _kratos.CreateLoginFlowAsync(listener.CallbackUrl, ct).ConfigureAwait(false);
            var redirectUrl = await _kratos.SubmitOidcAsync(actionUrl, ct).ConfigureAwait(false);

            var waitTask = listener.WaitForCodeAsync(ct);
            _openBrowser(redirectUrl);

            var callback = await waitTask.ConfigureAwait(false);

            if (!string.IsNullOrEmpty(callback.Error))
                return new SignInResult(SignInOutcome.Failed, $"Provider returned an error: {callback.Error}");

            if (string.IsNullOrEmpty(callback.Code))
            {
                // Missing code with no explicit error ⇒ per the map, the email-collision /
                // account-linking case.
                return new SignInResult(SignInOutcome.AccountLinkingRequired,
                    "An account with this email already exists. Sign in with your password first, then link Google from settings.");
            }

            var sessionToken = await _kratos.ExchangeCodeForSessionTokenAsync(callback.Code, ct).ConfigureAwait(false);
            var who = await _kratos.WhoamiAsync(sessionToken, ct).ConfigureAwait(false);

            var jwt = await _orgJwt.MintAsync(sessionToken, ct: ct).ConfigureAwait(false);

            _kratosSessionToken = sessionToken;
            IsSignedIn = true;
            UserId = who.UserId;
            Email = who.Email;
            DisplayName = who.DisplayName;

            _secrets.Write(AuthSecretKeys.KratosSessionToken, sessionToken);
            _secrets.Write(AuthSecretKeys.OrgServiceJwt, jwt.Token);
            if (UserId is not null) _secrets.Write(AuthSecretKeys.KratosUserId, UserId);
            if (Email is not null) _secrets.Write(AuthSecretKeys.KratosEmail, Email);
            if (DisplayName is not null) _secrets.Write(AuthSecretKeys.KratosDisplayName, DisplayName);

            return new SignInResult(SignInOutcome.Success);
        }
        catch (OperationCanceledException)
        {
            return new SignInResult(SignInOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            return new SignInResult(SignInOutcome.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Step 1 of the email-OTP unblock (map §3.3, extended — see CLAUDE.md task notes): starts a
    /// fresh Kratos login flow and requests Kratos email a 6-digit code to <paramref name="email"/>.
    /// Unlike <see cref="SignInWithGoogleAsync"/> this never opens a browser and never touches
    /// return_to — the "code" method is a pure API call/response cycle. Returns the handle the UI
    /// carries into <see cref="SubmitEmailOtpAsync"/>.
    /// </summary>
    public async Task<OtpFlowHandle> StartEmailOtpAsync(string email, CancellationToken ct = default)
    {
        if (_kratosOtp is null)
            throw new InvalidOperationException("AuthController was constructed without a KratosOtpAuthClient.");

        var flow = await _kratosOtp.StartFlowAsync(ct).ConfigureAwait(false);
        var updated = await _kratosOtp.RequestCodeAsync(flow.FlowId, flow.ActionUrl, email, ct).ConfigureAwait(false);
        return new OtpFlowHandle(updated.FlowId, updated.ActionUrl, email);
    }

    /// <summary>
    /// Step 2 of the email-OTP flow: submits the 6-digit code the user typed. On success, mints
    /// the org JWT and persists tokens exactly like <see cref="SignInWithGoogleAsync"/>'s success
    /// path. A wrong/expired code comes back as <see cref="SignInOutcome.InvalidCode"/> (not
    /// <see cref="SignInOutcome.Failed"/>) so the UI can re-prompt for the code instead of
    /// restarting the whole flow.
    /// </summary>
    public async Task<SignInResult> SubmitEmailOtpAsync(OtpFlowHandle handle, string code, CancellationToken ct = default)
    {
        if (_kratosOtp is null)
            throw new InvalidOperationException("AuthController was constructed without a KratosOtpAuthClient.");

        string sessionToken;
        try
        {
            sessionToken = await _kratosOtp.SubmitCodeAsync(handle.FlowId, handle.ActionUrl, handle.Email, code, ct)
                .ConfigureAwait(false);
        }
        catch (KratosAuthException ex)
        {
            return new SignInResult(SignInOutcome.InvalidCode, ex.Message);
        }
        catch (OperationCanceledException)
        {
            return new SignInResult(SignInOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            return new SignInResult(SignInOutcome.Failed, ex.Message);
        }

        try
        {
            var who = await _kratos.WhoamiAsync(sessionToken, ct).ConfigureAwait(false);
            var jwt = await _orgJwt.MintAsync(sessionToken, ct: ct).ConfigureAwait(false);

            _kratosSessionToken = sessionToken;
            IsSignedIn = true;
            UserId = who.UserId;
            Email = who.Email ?? handle.Email;
            DisplayName = who.DisplayName;

            _secrets.Write(AuthSecretKeys.KratosSessionToken, sessionToken);
            _secrets.Write(AuthSecretKeys.OrgServiceJwt, jwt.Token);
            if (UserId is not null) _secrets.Write(AuthSecretKeys.KratosUserId, UserId);
            if (Email is not null) _secrets.Write(AuthSecretKeys.KratosEmail, Email);
            if (DisplayName is not null) _secrets.Write(AuthSecretKeys.KratosDisplayName, DisplayName);

            return new SignInResult(SignInOutcome.Success);
        }
        catch (OperationCanceledException)
        {
            return new SignInResult(SignInOutcome.Cancelled);
        }
        catch (Exception ex)
        {
            return new SignInResult(SignInOutcome.Failed, ex.Message);
        }
    }

    /// <summary>
    /// Returns a valid (auto-refreshed) org JWT for the wire client's bearer, or null if signed out.
    /// </summary>
    public async Task<string?> GetCurrentBearerAsync(CancellationToken ct = default)
    {
        if (!IsSignedIn || _kratosSessionToken is null) return null;

        var jwt = await _orgJwt.RefreshIfNeeded(_kratosSessionToken, ct: ct).ConfigureAwait(false);
        _secrets.Write(AuthSecretKeys.OrgServiceJwt, jwt.Token);
        return jwt.Token;
    }

    public void SignOut()
    {
        ClearStoredTokens();
        IsSignedIn = false;
        _kratosSessionToken = null;
        UserId = null;
        Email = null;
        DisplayName = null;
    }

    private void ClearStoredTokens()
    {
        _secrets.Write(AuthSecretKeys.KratosSessionToken, string.Empty);
        _secrets.Write(AuthSecretKeys.OrgServiceJwt, string.Empty);
    }
}

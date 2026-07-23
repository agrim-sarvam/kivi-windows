// Kivi.App/Services/GoogleSignIn.cs
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Kivi.App.Services;

/// <summary>
/// Client-side-only Google identity capture for onboarding personalization (name/email/
/// avatar). Not an auth boundary: the id_token is decoded for display fields only, never
/// verified against a server, and never used to grant access to anything. No backend,
/// no account creation, no token persistence beyond the profile fields on AppConfig.
///
/// Uses the Authorization Code + PKCE flow (response_type=code), not the older implicit
/// flow (response_type=id_token) -- Google rejects the implicit flow for OAuth clients
/// created after it was deprecated, returning "Error 400: unsupported_response_type".
/// PKCE lets a public/desktop client (no client secret) exchange the code for tokens
/// safely: a random code_verifier is hashed into a code_challenge sent up front, and the
/// raw verifier is presented at token-exchange time so Google can confirm the same app
/// instance that started the flow is the one completing it.
/// </summary>
public static class GoogleSignIn
{
    public sealed record GoogleProfile(string Name, string Email, string? AvatarUrl);

    public static string BuildAuthUrl(string clientId, string redirectUri, string state, string codeChallenge)
    {
        // Note: System.Web.HttpUtility is not available under net8.0-windows without an
        // extra package reference, so query-string construction is done manually via
        // Uri.EscapeDataString instead (an approved deviation per the plan's Task 2 Step 4).
        var query = string.Join('&', new[]
        {
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "response_type=code",
            $"scope={Uri.EscapeDataString("openid email profile")}",
            $"state={Uri.EscapeDataString(state)}",
            $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
            "code_challenge_method=S256",
        });
        return $"https://accounts.google.com/o/oauth2/v2/auth?{query}";
    }

    public static async Task<GoogleProfile?> SignInAsync(string clientId, CancellationToken ct)
    {
        using var listener = new HttpListener();
        // Port 0 is not valid for HttpListener prefixes; pick a fixed high port used only
        // during the sign-in window. If it's in use, the listener throws and sign-in fails
        // gracefully (caller shows "couldn't start sign-in, try again").
        const int port = 51738;
        var redirectUri = $"http://127.0.0.1:{port}/callback";
        listener.Prefixes.Add($"{redirectUri}/");
        listener.Start();

        var state = Guid.NewGuid().ToString("N");
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = ToCodeChallenge(codeVerifier);
        var authUrl = BuildAuthUrl(clientId, redirectUri, state, codeChallenge);

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(authUrl) { UseShellExecute = true });
        }
        catch
        {
            return null; // couldn't launch a browser
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(5));

        HttpListenerContext context;
        try
        {
            var getContextTask = listener.GetContextAsync();
            using var reg = timeoutCts.Token.Register(listener.Stop);
            context = await getContextTask;
        }
        catch (Exception) when (timeoutCts.IsCancellationRequested)
        {
            return null; // timed out or cancelled
        }
        catch
        {
            return null;
        }

        // Authorization Code flow returns `code`/`state` as real query-string parameters
        // (unlike the old implicit flow's URL fragment), so a single request is enough --
        // no redirect-and-resubmit dance is needed here.
        var request = context.Request;
        string code;
        if (request.QueryString["error"] is { } error)
        {
            await RespondAsync(context, $"<html><body>Sign-in failed: {error}. You can close this tab.</body></html>", ct);
            return null;
        }
        else if (request.QueryString["code"] is { } c && request.QueryString["state"] == state)
        {
            code = c;
        }
        else
        {
            await RespondAsync(context, "<html><body>Unexpected response. You can close this tab.</body></html>", ct);
            return null;
        }

        await RespondAsync(context, "<html><body>Signed in — you can close this tab and return to Kivi.</body></html>", ct);

        var idToken = await ExchangeCodeForIdTokenAsync(clientId, code, redirectUri, codeVerifier, ct);
        return idToken is null ? null : DecodeProfile(idToken);
    }

    private static async Task RespondAsync(HttpListenerContext context, string html, CancellationToken ct)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, ct);
        context.Response.OutputStream.Close();
    }

    private static async Task<string?> ExchangeCodeForIdTokenAsync(
        string clientId, string code, string redirectUri, string codeVerifier, CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient();
            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["code"] = code,
                ["code_verifier"] = codeVerifier,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
            });
            using var response = await http.PostAsync("https://oauth2.googleapis.com/token", form, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("id_token", out var t) ? t.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    private static string ToCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private static GoogleProfile? DecodeProfile(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2) return null;
            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;
            var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
            var email = root.TryGetProperty("email", out var e) ? e.GetString() : null;
            var picture = root.TryGetProperty("picture", out var p) ? p.GetString() : null;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(email)) return null;
            return new GoogleProfile(name, email, picture);
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}

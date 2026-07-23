// Kivi.App/Services/GoogleSignIn.cs
using System.Net;
using System.Text;
using System.Text.Json;

namespace Kivi.App.Services;

/// <summary>
/// Client-side-only Google identity capture for onboarding personalization (name/email/
/// avatar). Not an auth boundary: the id_token is decoded for display fields only, never
/// verified against a server, and never used to grant access to anything. No backend,
/// no account creation, no token persistence beyond the profile fields on AppConfig.
/// </summary>
public static class GoogleSignIn
{
    public sealed record GoogleProfile(string Name, string Email, string? AvatarUrl);

    public static string BuildAuthUrl(string clientId, string redirectUri, string state)
    {
        // Note: System.Web.HttpUtility is not available under net8.0-windows without an
        // extra package reference, so query-string construction is done manually via
        // Uri.EscapeDataString instead (an approved deviation per the plan's Task 2 Step 4).
        var query = string.Join('&', new[]
        {
            $"client_id={Uri.EscapeDataString(clientId)}",
            $"redirect_uri={Uri.EscapeDataString(redirectUri)}",
            "response_type=id_token",
            $"scope={Uri.EscapeDataString("openid email profile")}",
            $"nonce={Uri.EscapeDataString(state)}",
            $"state={Uri.EscapeDataString(state)}",
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
        var authUrl = BuildAuthUrl(clientId, redirectUri, state);

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

        // Google's id_token flow returns the token in the URL *fragment*, which browsers
        // never send to the server. So the redirect page is a tiny script that reads
        // location.hash and re-submits it as a query string to this same endpoint.
        var request = context.Request;
        string responseHtml;
        GoogleProfile? profile = null;

        if (request.QueryString["id_token"] is { } idToken && request.QueryString["state"] == state)
        {
            profile = DecodeProfile(idToken);
            responseHtml = "<html><body>Signed in — you can close this tab and return to Kivi.</body></html>";
        }
        else
        {
            // First hit: browser landed with the token in the fragment. Serve a redirect
            // script that resubmits it as a query string.
            responseHtml = $$"""
                <html><body><script>
                    var params = new URLSearchParams(location.hash.substring(1));
                    location.href = "{{redirectUri}}?" + params.toString() + "&state={{state}}";
                </script></body></html>
                """;
        }

        var buffer = Encoding.UTF8.GetBytes(responseHtml);
        context.Response.ContentType = "text/html";
        context.Response.ContentLength64 = buffer.Length;
        await context.Response.OutputStream.WriteAsync(buffer, ct);
        context.Response.OutputStream.Close();

        if (profile is not null) return profile;

        // Second hit expected next (the resubmitted query-string request) — wait once more.
        try
        {
            var context2 = await listener.GetContextAsync();
            if (context2.Request.QueryString["id_token"] is { } idToken2 && context2.Request.QueryString["state"] == state)
                profile = DecodeProfile(idToken2);

            var buffer2 = Encoding.UTF8.GetBytes("<html><body>Signed in — you can close this tab and return to Kivi.</body></html>");
            context2.Response.ContentType = "text/html";
            context2.Response.ContentLength64 = buffer2.Length;
            await context2.Response.OutputStream.WriteAsync(buffer2, ct);
            context2.Response.OutputStream.Close();
        }
        catch
        {
            return null;
        }

        return profile;
    }

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

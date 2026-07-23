// Kivi.App/Services/GoogleOAuthConfig.cs
namespace Kivi.App.Services;

/// <summary>
/// The Google OAuth Desktop-app client ID/secret used by GoogleSignIn, resolved the same
/// way the Sarvam API key is: env vars (.env, dev-time) first, then an embedded
/// kivi-key.local.json (release-build-time), so neither value is hardcoded in source --
/// GitHub's push protection correctly flags literal OAuth credentials in committed code,
/// and it's better practice regardless of Google's own guidance that this particular
/// secret type isn't a true confidential secret for a Desktop-app client.
/// </summary>
public sealed record GoogleOAuthConfig(string ClientId, string ClientSecret)
{
    public static GoogleOAuthConfig Resolve(Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        var envId = configuration["GOOGLE_CLIENT_ID"];
        var envSecret = configuration["GOOGLE_CLIENT_SECRET"];
        if (!string.IsNullOrEmpty(envId) && !string.IsNullOrEmpty(envSecret))
            return new GoogleOAuthConfig(envId, envSecret);

        var embeddedPath = System.IO.Path.Combine(AppContext.BaseDirectory, "kivi-key.local.json");
        if (System.IO.File.Exists(embeddedPath))
        {
            try
            {
                var json = System.IO.File.ReadAllText(embeddedPath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                var id = root.TryGetProperty("GoogleClientId", out var idProp) ? idProp.GetString() : null;
                var secret = root.TryGetProperty("GoogleClientSecret", out var secretProp) ? secretProp.GetString() : null;
                if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(secret))
                    return new GoogleOAuthConfig(id, secret);
            }
            catch { /* malformed key file -- fall through with no config, same as missing-key behavior */ }
        }

        return new GoogleOAuthConfig("", "");
    }
}

using System.Net;
using System.Net.Http;
using Kivi.Platform.Auth;
using Xunit;

namespace Kivi.Core.Tests;

/// <summary>
/// GATED live smoke check against the real Kratos instance (<c>https://login.sarvam.ai/identity/</c>).
/// Confirms the "code" (email-OTP) login method group is still present on a freshly created login
/// flow — this is the structural assumption <see cref="KratosOtpAuthClient"/> is built on. Does NOT
/// attempt to actually request/verify a real OTP code (that needs a real inbox); it only inspects
/// the flow the unauthenticated GET returns.
///
/// Skips gracefully (never hard-fails the suite) if the live Kratos endpoint is unreachable —
/// mirrors <see cref="LiveServiceIntegrationTests"/>'s gating pattern.
/// </summary>
public class KratosOtpLiveSmokeTests
{
    private static readonly Uri KratosUrl = new("https://login.sarvam.ai/identity/");

    [SkippableFact]
    public async Task LoginFlow_StillOffersCodeMethodGroup()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        HttpResponseMessage resp;
        string body;
        try
        {
            resp = await http.GetAsync($"{KratosUrl}self-service/login/api");
            body = await resp.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            Skip.If(true, $"live Kratos endpoint unreachable: {ex.Message}");
            return;
        }

        Skip.IfNot(resp.IsSuccessStatusCode,
            $"live Kratos login flow returned HTTP {(int)resp.StatusCode} — cannot verify method groups");

        // Structural check only: don't fully deserialize (schema may evolve), just confirm the
        // "code" method group's submit node is present in the raw response, same shape confirmed
        // live on 2026-07-29 (group":"code" ... "value":"code").
        Assert.Contains("\"group\":\"code\"", body);
        Assert.Contains("\"value\":\"code\"", body);
    }
}

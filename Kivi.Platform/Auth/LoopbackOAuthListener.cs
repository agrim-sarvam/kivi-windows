using System.IO;
using System.Net;
using System.Text;

namespace Kivi.Platform.Auth;

/// <summary>
/// Loopback OAuth callback receiver (map §3.4). Replaces the <c>kivi://</c> custom-scheme deep
/// link: we bind an <see cref="HttpListener"/> to <c>http://127.0.0.1:&lt;port&gt;/callback</c>,
/// open the provider's auth URL in the user's default browser, and resume when the browser hits
/// our loopback endpoint.
///
/// Handles both callback shapes uniformly:
///  - Kratos: the code arrives as a normal <c>?code=</c> query parameter — the server sees it directly.
///  - Supabase: tokens arrive in the URL <c>#fragment</c>, which browsers never send to a server.
///    We serve a tiny HTML/JS page at <c>/callback</c> that reads <c>location.hash</c> and POSTs it
///    to a sibling endpoint (<c>/callback/fragment</c>) on the same listener.
///
/// One listener instance is used per sign-in attempt; a re-tap (new <see cref="WaitForCodeAsync"/>
/// call) supersedes any prior pending wait.
/// </summary>
public interface IOAuthLoopbackListener : IDisposable
{
    string CallbackUrl { get; }
    Task<OAuthCallbackResult> WaitForCodeAsync(CancellationToken ct);
}

public sealed class LoopbackOAuthListener : IOAuthLoopbackListener
{
    private const int PreferredPort = 51234;

    private readonly HttpListener _listener;
    private TaskCompletionSource<OAuthCallbackResult>? _pending;
    private CancellationTokenSource? _listenLoopCts;
    private Task? _listenLoopTask;

    public int Port { get; }
    public string CallbackUrl => $"http://127.0.0.1:{Port}/callback";

    private LoopbackOAuthListener(HttpListener listener, int port)
    {
        _listener = listener;
        Port = port;
    }

    /// <summary>Bind to <see cref="PreferredPort"/>, falling back to an OS-assigned ephemeral port.</summary>
    public static LoopbackOAuthListener StartNew()
    {
        // HttpListener needs an explicit prefix; "port 0" isn't directly supported the way sockets
        // support it, so we probe a free TCP port first (via a throwaway Socket bind), then wire
        // the listener to that exact port. Try the fixed preferred port first (cheap, deterministic
        // for docs/testing), fall back to an OS-chosen free port on conflict.
        if (TryStart(PreferredPort, out var listener))
            return listener!;

        var freePort = GetFreeTcpPort();
        if (TryStart(freePort, out listener))
            return listener!;

        throw new InvalidOperationException("Unable to bind a loopback HttpListener for the OAuth callback.");
    }

    private static bool TryStart(int port, out LoopbackOAuthListener? result)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try
        {
            listener.Start();
            result = new LoopbackOAuthListener(listener, port);
            return true;
        }
        catch (HttpListenerException)
        {
            listener.Close();
            result = null;
            return false;
        }
    }

    private static int GetFreeTcpPort()
    {
        var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    /// <summary>
    /// Wait for the OAuth callback. Completes with the parsed result once <c>/callback</c> (Kratos
    /// <c>?code=</c>) or <c>/callback/fragment</c> (Supabase <c>#fragment</c>, forwarded by the served
    /// HTML page) is hit. A new call supersedes any prior pending wait (cancels it).
    /// </summary>
    public Task<OAuthCallbackResult> WaitForCodeAsync(CancellationToken ct)
    {
        var previous = _pending;
        var tcs = new TaskCompletionSource<OAuthCallbackResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = tcs;
        previous?.TrySetCanceled();

        EnsureListenLoop();

        ct.Register(() => tcs.TrySetCanceled());
        return tcs.Task;
    }

    private void EnsureListenLoop()
    {
        if (_listenLoopTask is { IsCompleted: false }) return;
        _listenLoopCts = new CancellationTokenSource();
        _listenLoopTask = Task.Run(() => ListenLoopAsync(_listenLoopCts.Token));
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (HttpListenerException)
            {
                return; // listener stopped/disposed
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                HandleContext(ctx);
            }
            catch
            {
                try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { /* best effort */ }
            }
        }
    }

    private void HandleContext(HttpListenerContext ctx)
    {
        var path = ctx.Request.Url?.AbsolutePath ?? string.Empty;

        if (string.Equals(path, "/callback", StringComparison.OrdinalIgnoreCase))
        {
            var query = ctx.Request.QueryString;
            var code = query["code"];
            var error = query["error"];

            if (!string.IsNullOrEmpty(code) || !string.IsNullOrEmpty(error))
            {
                // Kratos-style (or an error response) — resolve immediately.
                RespondHtml(ctx, SuccessPageHtml());
                Complete(new OAuthCallbackResult(Code: code, Fragment: null, Error: error));
                return;
            }

            // No ?code= and no ?error= — might be a Supabase fragment callback. Serve the
            // fragment-forwarding page; it will POST back to /callback/fragment shortly.
            RespondHtml(ctx, FragmentForwardingPageHtml());
            return;
        }

        if (string.Equals(path, "/callback/fragment", StringComparison.OrdinalIgnoreCase)
            && ctx.Request.HttpMethod == "POST")
        {
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                body = reader.ReadToEnd();

            RespondHtml(ctx, SuccessPageHtml());
            Complete(new OAuthCallbackResult(Code: null, Fragment: body, Error: null));
            return;
        }

        ctx.Response.StatusCode = 404;
        ctx.Response.Close();
    }

    private void Complete(OAuthCallbackResult result)
    {
        var tcs = _pending;
        _pending = null;
        tcs?.TrySetResult(result);
    }

    private static void RespondHtml(HttpListenerContext ctx, string html)
    {
        var bytes = Encoding.UTF8.GetBytes(html);
        ctx.Response.ContentType = "text/html; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
    }

    private static string SuccessPageHtml() => """
        <!doctype html><html><head><meta charset="utf-8"><title>Kivi</title></head>
        <body style="font-family:sans-serif;text-align:center;padding-top:4em;">
        <h2>Signed in</h2><p>You can close this tab and return to Kivi.</p>
        </body></html>
        """;

    private static string FragmentForwardingPageHtml() => """
        <!doctype html><html><head><meta charset="utf-8"><title>Kivi</title></head>
        <body style="font-family:sans-serif;text-align:center;padding-top:4em;">
        <p id="msg">Completing sign-in&hellip;</p>
        <script>
          (function () {
            var hash = window.location.hash || "";
            if (hash.startsWith("#")) hash = hash.substring(1);
            fetch("/callback/fragment", { method: "POST", body: hash })
              .then(function () { document.getElementById("msg").textContent = "Signed in. You can close this tab."; })
              .catch(function () { document.getElementById("msg").textContent = "Sign-in failed to report back to Kivi."; });
          })();
        </script>
        </body></html>
        """;

    public void Dispose()
    {
        _listenLoopCts?.Cancel();
        _pending?.TrySetCanceled();
        try { _listener.Stop(); } catch { /* ignore */ }
        try { _listener.Close(); } catch { /* ignore */ }
    }
}

/// <summary>
/// The parsed result of an OAuth loopback callback. Exactly one of <see cref="Code"/> (Kratos) or
/// <see cref="Fragment"/> (Supabase) is populated on success; <see cref="Error"/> is set if the
/// provider redirected back with an error query param.
/// </summary>
public readonly record struct OAuthCallbackResult(string? Code, string? Fragment, string? Error);

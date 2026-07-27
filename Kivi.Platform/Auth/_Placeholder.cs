namespace Kivi.Platform.Auth;

// PHASE P6 (M7). Loopback OAuth listener (HttpListener http://127.0.0.1:<port>/callback,
// handles ?code= and #fragment) + JWT mint (POST auth.sarvam.ai/api/v2/auth/jwt, X-Session-Token,
// 15-min TTL, single-flight re-mint). Replaces the kivi:// custom scheme.
internal static class _Placeholder { }

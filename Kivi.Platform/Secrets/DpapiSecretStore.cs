using Kivi.Core.Contracts;

namespace Kivi.Platform.Secrets;

/// <summary>
/// PHASE P3/P7 — STUB. Real impl: DPAPI (ProtectedData) / Windows Credential Manager
/// (replaces Keychain / safeStorage). Stores kratos session + org JWT.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    public string? Read(string key) => null;          // P3/P7
    public void Write(string key, string value) { }   // P3/P7
}

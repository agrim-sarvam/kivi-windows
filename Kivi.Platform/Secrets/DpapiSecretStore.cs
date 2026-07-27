using System.IO;
using System.Security.Cryptography;
using System.Text;
using Kivi.Core.Contracts;

namespace Kivi.Platform.Secrets;

/// <summary>
/// REAL DPAPI-backed secret store (replaces Keychain / safeStorage). Each secret is encrypted with
/// <see cref="ProtectedData"/> under <see cref="DataProtectionScope.CurrentUser"/> and persisted as a
/// file under <c>%APPDATA%\Kivi\secrets</c> (platform-coupling-audit §7). One file per key so reads
/// and writes are independent and crash-safe.
///
/// Stores the Supabase/Kratos tokens, the 15-min org JWT, and the retained-audio AES key.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Kivi.Secrets.v1");

    private readonly string _dir;

    public DpapiSecretStore() : this(DefaultDir()) { }

    /// <summary>Construct with an explicit storage directory (used by tests).</summary>
    public DpapiSecretStore(string directory)
    {
        _dir = directory;
        Directory.CreateDirectory(_dir);
    }

    private static string DefaultDir()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi", "secrets");

    public string? Read(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) return null;
        try
        {
            byte[] cipher = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(cipher, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (CryptographicException)
        {
            // Corrupt or written under a different user/machine — treat as absent.
            return null;
        }
    }

    public void Write(string key, string value)
    {
        byte[] plain = Encoding.UTF8.GetBytes(value);
        byte[] cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        var path = PathFor(key);
        // Write-then-rename for atomicity so a crash mid-write never leaves a truncated secret.
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, cipher);
        File.Move(tmp, path, overwrite: true);
    }

    private string PathFor(string key)
    {
        // Hash the key to a filesystem-safe, fixed-length name (keys may contain path-hostile chars).
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_dir, Convert.ToHexString(hash) + ".dpapi");
    }
}

using System.Security.Cryptography;
using System.Text;
using Kivi.Core.Abstractions;

namespace Kivi.Platform.Secrets;

public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _path;

    public DpapiSecretStore(string? filePath = null)
    {
        _path = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi", "key.dat");
    }

    public string? GetApiKey()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var cipher = File.ReadAllBytes(_path);
            var plain = ProtectedData.Unprotect(cipher, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch { return null; }
    }

    public void SetApiKey(string key)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var cipher = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_path, cipher);
    }
}

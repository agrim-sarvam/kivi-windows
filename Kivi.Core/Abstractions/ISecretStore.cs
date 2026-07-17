namespace Kivi.Core.Abstractions;

public interface ISecretStore
{
    string? GetApiKey();
    void SetApiKey(string key);
}

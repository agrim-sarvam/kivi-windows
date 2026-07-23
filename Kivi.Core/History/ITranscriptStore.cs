namespace Kivi.Core.History;

/// <summary>
/// Persists dictation transcripts so History/Analytics survive restarts. Never stores
/// the API key or any secret -- unrelated to ISecretStore/IAppConfigStore, but follows
/// the same "never throw on a corrupt file" resilience contract as JsonAppConfigStore.
/// </summary>
public interface ITranscriptStore
{
    IReadOnlyList<TranscriptEntry> LoadAll();
    void Append(TranscriptEntry entry);
    void Clear();
}

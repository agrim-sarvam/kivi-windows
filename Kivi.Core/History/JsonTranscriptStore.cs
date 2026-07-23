using System.Text.Json;

namespace Kivi.Core.History;

public sealed class JsonTranscriptStore : ITranscriptStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonTranscriptStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kivi",
            "history.json");
    }

    public IReadOnlyList<TranscriptEntry> LoadAll()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath)) return Array.Empty<TranscriptEntry>();
                var json = File.ReadAllText(_filePath);
                var entries = JsonSerializer.Deserialize<List<TranscriptEntry>>(json, SerializerOptions);
                return entries ?? new List<TranscriptEntry>();
            }
            catch
            {
                return Array.Empty<TranscriptEntry>();
            }
        }
    }

    public void Append(TranscriptEntry entry)
    {
        lock (_lock)
        {
            var entries = LoadAll().ToList();
            entries.Add(entry);
            Save(entries);
        }
    }

    public void Clear()
    {
        lock (_lock) { Save(new List<TranscriptEntry>()); }
    }

    private void Save(List<TranscriptEntry> entries)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var json = JsonSerializer.Serialize(entries, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}

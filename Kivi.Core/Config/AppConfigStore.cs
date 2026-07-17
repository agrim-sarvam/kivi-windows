using System.Text.Json;

namespace Kivi.Core.Config;

/// <summary>
/// Loads and saves <see cref="AppConfig"/> to a JSON file so non-secret settings
/// persist across app restarts. Never stores the API key or any other secret —
/// secrets remain exclusively in the platform-specific secret store (e.g. DPAPI).
/// </summary>
public interface IAppConfigStore
{
    AppConfig Load();
    void Save(AppConfig config);
}

/// <summary>
/// Default <see cref="IAppConfigStore"/> implementation backed by a JSON file,
/// by default at "%APPDATA%\Kivi\settings.json". Pure BCL (System.Text.Json + System.IO),
/// so it has no OS-specific dependency and keeps Kivi.Core cross-platform.
/// </summary>
public sealed class JsonAppConfigStore : IAppConfigStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public JsonAppConfigStore(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kivi",
            "settings.json");
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return AppConfig.Default();

            var json = File.ReadAllText(_filePath);
            var config = JsonSerializer.Deserialize<AppConfig>(json, SerializerOptions);
            return config ?? AppConfig.Default();
        }
        catch
        {
            // Corrupt/malformed settings file must never crash the app — fall back to defaults.
            return AppConfig.Default();
        }
    }

    public void Save(AppConfig config)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(config, SerializerOptions);
        File.WriteAllText(_filePath, json);
    }
}

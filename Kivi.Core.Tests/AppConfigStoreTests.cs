using Kivi.Core.Config;
using Kivi.Core.Macros;
using Xunit;

public class AppConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _filePath;

    public AppConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "KiviCoreTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _filePath = Path.Combine(_tempDir, "settings.json");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsNonDefaultValues()
    {
        var store = new JsonAppConfigStore(_filePath);
        var config = AppConfig.Default();
        config.CleanupModel = "custom/model-x";
        config.CustomVocabulary = "Kivi, Sarvam, dictation";
        config.Macros.Add(new VoiceMacro("new line", "\n"));

        store.Save(config);
        var loaded = store.Load();

        Assert.Equal("custom/model-x", loaded.CleanupModel);
        Assert.Equal("Kivi, Sarvam, dictation", loaded.CustomVocabulary);
        Assert.Single(loaded.Macros);
        Assert.Equal("new line", loaded.Macros[0].Command);
        Assert.Equal("\n", loaded.Macros[0].Payload);
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsDefaults()
    {
        var store = new JsonAppConfigStore(_filePath);

        var loaded = store.Load();

        var defaults = AppConfig.Default();
        Assert.Equal(defaults.TranscriptionBaseUrl, loaded.TranscriptionBaseUrl);
        Assert.Equal(defaults.TranscriptionModel, loaded.TranscriptionModel);
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaultsInsteadOfThrowing()
    {
        File.WriteAllText(_filePath, "{ this is not valid json !!!");
        var store = new JsonAppConfigStore(_filePath);

        var loaded = store.Load();

        var defaults = AppConfig.Default();
        Assert.Equal(defaults.TranscriptionBaseUrl, loaded.TranscriptionBaseUrl);
        Assert.Equal(defaults.TranscriptionModel, loaded.TranscriptionModel);
    }
}

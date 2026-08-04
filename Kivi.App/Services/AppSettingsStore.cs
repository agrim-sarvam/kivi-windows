using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kivi.App.Services;

/// <summary>
/// Small, app-level settings that don't belong to the pure orb engine's <c>IFlowStore</c>: the chosen
/// global hotkey chord (storage form of <see cref="Kivi.Core.Hotkey.HotkeyChord"/>) and the
/// "has the user seen onboarding yet" flag. Persisted to <c>%APPDATA%\Kivi\app-settings.json</c>.
///
/// Kept separate from <see cref="Kivi.Core.Orb.JsonFlowStore"/> on purpose: those values feed the
/// golden-frame-tested engine; these are UI/platform concerns. Same defensive contract — a missing or
/// corrupt file yields defaults, never a throw; writes are best-effort.
/// </summary>
public interface IAppSettingsStore
{
    /// <summary>The chosen hotkey chord in HotkeyChord storage form (e.g. "A3", "11-5B"), or null if
    /// the user hasn't chosen one (⇒ use the app default).</summary>
    string? HotkeyChord { get; set; }

    /// <summary>True once the user has completed (or dismissed) the onboarding page.</summary>
    bool HasOnboarded { get; set; }
}

public sealed class JsonAppSettingsStore : IAppSettingsStore
{
    private sealed class Model
    {
        [JsonPropertyName("hotkeyChord")] public string? HotkeyChord { get; set; }
        [JsonPropertyName("hasOnboarded")] public bool HasOnboarded { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _lock = new();
    private Model _model;

    public JsonAppSettingsStore() : this(DefaultFilePath()) { }

    public JsonAppSettingsStore(string filePath)
    {
        _filePath = filePath;
        _model = Read() ?? new Model();
    }

    private static string DefaultFilePath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kivi");
        return Path.Combine(dir, "app-settings.json");
    }

    public string? HotkeyChord
    {
        get { lock (_lock) return _model.HotkeyChord; }
        set { lock (_lock) { _model.HotkeyChord = value; Write(); } }
    }

    public bool HasOnboarded
    {
        get { lock (_lock) return _model.HasOnboarded; }
        set { lock (_lock) { _model.HasOnboarded = value; Write(); } }
    }

    private Model? Read()
    {
        try
        {
            if (!File.Exists(_filePath)) return null;
            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<Model>(json, JsonOptions);
        }
        catch { return null; }
    }

    private void Write()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(_model, JsonOptions);
            var tmp = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(_filePath)) File.Replace(tmp, _filePath, null);
            else File.Move(tmp, _filePath);
        }
        catch { /* best-effort */ }
    }
}

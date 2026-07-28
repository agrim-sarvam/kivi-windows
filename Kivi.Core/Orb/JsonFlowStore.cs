// JSON-backed IFlowStore — local persistence for settings + transcript playback history.
// Port target per docs/maps/orb-engine-behavior.md §9.6: "a settings-backed store (JSON under
// %APPDATA%\Kivi — reuse the reference key names flowPage, flowOrbStyle, kiviFlowPlayback, …)".
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kivi.Core.Orb;

/// <summary>
/// Reads/writes %APPDATA%\Kivi\flowstore.json. Resilient: missing file → empty defaults;
/// corrupt file → caught, empty defaults (never throws/crashes the app); writes are atomic
/// (write to a temp file, then File.Replace/Move) so a crash mid-write can't corrupt the store.
/// </summary>
public sealed class JsonFlowStore : IFlowStore
{
    private sealed class OnDiskModel
    {
        [JsonPropertyName("flowPage")] public string? FlowPage { get; set; }
        [JsonPropertyName("flowOrbStyle")] public string? FlowOrbStyle { get; set; }
        [JsonPropertyName("flowOrbSize")] public string? FlowOrbSize { get; set; }
        [JsonPropertyName("flowTooltips")] public bool? FlowTooltips { get; set; }
        [JsonPropertyName("flowDefaultExpansion")] public string? FlowDefaultExpansion { get; set; }
        [JsonPropertyName("flowMovable")] public bool? FlowMovable { get; set; }
        [JsonPropertyName("flowDefaultPosition")] public string? FlowDefaultPosition { get; set; }
        [JsonPropertyName("flowReduceMotion")] public bool? FlowReduceMotion { get; set; }
        [JsonPropertyName("flowHaptics")] public bool? FlowHaptics { get; set; }
        [JsonPropertyName("flowSounds")] public bool? FlowSounds { get; set; }
        [JsonPropertyName("kiviFlowPlayback")] public List<PlaybackEntry>? KiviFlowPlayback { get; set; }
    }

    private sealed class PlaybackEntry
    {
        [JsonPropertyName("stage")] public string Stage { get; set; } = "";
        [JsonPropertyName("payload")] public string Payload { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly object _lock = new();

    public JsonFlowStore() : this(DefaultFilePath()) { }

    public JsonFlowStore(string filePath)
    {
        _filePath = filePath;
    }

    private static string DefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Kivi");
        return Path.Combine(dir, "flowstore.json");
    }

    public FlowSettings LoadSettings()
    {
        var model = ReadModel();
        var settings = FlowSettings.Default();
        if (model is null) return settings;

        if (Enum.TryParse<PageStyle>(model.FlowPage, ignoreCase: true, out var page)) settings.Page = page;
        if (Enum.TryParse<OrbStyle>(model.FlowOrbStyle, ignoreCase: true, out var orb)) settings.Orb = orb;
        if (Enum.TryParse<OrbSize>(model.FlowOrbSize, ignoreCase: true, out var size)) settings.OrbSize = size;
        if (model.FlowTooltips.HasValue) settings.Tooltips = model.FlowTooltips.Value;
        if (Enum.TryParse<DefaultExpansion>(model.FlowDefaultExpansion, ignoreCase: true, out var exp)) settings.DefaultExpansion = exp;
        if (model.FlowMovable.HasValue) settings.Movable = model.FlowMovable.Value;
        if (Enum.TryParse<DefaultPosition>(model.FlowDefaultPosition, ignoreCase: true, out var pos)) settings.DefaultPosition = pos;
        if (model.FlowReduceMotion.HasValue) settings.ReduceMotion = model.FlowReduceMotion.Value;
        if (model.FlowHaptics.HasValue) settings.Haptics = model.FlowHaptics.Value;
        if (model.FlowSounds.HasValue) settings.Sounds = model.FlowSounds.Value;

        return settings;
    }

    public void SaveSettings(FlowSettings s)
    {
        lock (_lock)
        {
            var model = ReadModel() ?? new OnDiskModel();
            model.FlowPage = s.Page.ToString();
            model.FlowOrbStyle = s.Orb.ToString();
            model.FlowOrbSize = s.OrbSize.ToString();
            model.FlowTooltips = s.Tooltips;
            model.FlowDefaultExpansion = s.DefaultExpansion.ToString();
            model.FlowMovable = s.Movable;
            model.FlowDefaultPosition = s.DefaultPosition.ToString();
            model.FlowReduceMotion = s.ReduceMotion;
            model.FlowHaptics = s.Haptics;
            model.FlowSounds = s.Sounds;
            WriteModel(model);
        }
    }

    public List<TxSnapshot> LoadPlayback()
    {
        var model = ReadModel();
        var result = new List<TxSnapshot>();
        if (model?.KiviFlowPlayback is null) return result;
        foreach (var entry in model.KiviFlowPlayback)
            result.Add(new TxSnapshot(entry.Stage, entry.Payload));
        return result;
    }

    public void SavePlayback(List<TxSnapshot> a)
    {
        lock (_lock)
        {
            var model = ReadModel() ?? new OnDiskModel();
            var entries = new List<PlaybackEntry>(a.Count);
            foreach (var snap in a)
                entries.Add(new PlaybackEntry { Stage = snap.Stage, Payload = snap.Payload });
            model.KiviFlowPlayback = entries;
            WriteModel(model);
        }
    }

    // MARK: disk I/O

    private OnDiskModel? ReadModel()
    {
        lock (_lock)
        {
            try
            {
                if (!File.Exists(_filePath)) return null;
                var json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonSerializer.Deserialize<OnDiskModel>(json, JsonOptions);
            }
            catch
            {
                // Corrupt/unreadable file: behave as if empty rather than crash.
                return null;
            }
        }
    }

    private void WriteModel(OnDiskModel model)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(model, JsonOptions);

            // Atomic write: write to a temp file in the same directory, then replace/move so a
            // crash mid-write never leaves flowstore.json truncated/corrupt.
            var tempPath = _filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tempPath, json);

            if (File.Exists(_filePath))
                File.Replace(tempPath, _filePath, destinationBackupFileName: null);
            else
                File.Move(tempPath, _filePath);
        }
        catch
        {
            // Best-effort persistence: swallow I/O errors (e.g. locked file, disk full) rather
            // than crashing the dictation loop.
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Kivi.App.Services;

/// One completed dictation take, for the History/Record pages.
public sealed record DictationHistoryEntry(
    string Text,            // the formatted final text (what got pasted)
    string? RawText,        // raw transcript, if different (may be null)
    string? AppName,        // e.g. "Code", "notepad" (from AppTarget.AppName)
    string? ExePath,        // full exe path, for app-icon extraction (from AppTarget.ExePath)
    DateTime TimestampUtc);

public interface IDictationHistoryStore
{
    void Add(DictationHistoryEntry entry);
    /// Newest-first.
    IReadOnlyList<DictationHistoryEntry> All();
    /// Raised (on whatever thread Add was called on) after an entry is added, so pages can refresh.
    event Action? Changed;
}

/// <summary>
/// Local, file-backed dictation history. Every completed take (text + which app + when) is
/// appended newest-first and persisted to <c>%APPDATA%\Kivi\dictation-history.json</c>. Read by
/// the History/Record pages via <see cref="All"/>.
///
/// Design notes:
/// - Load is fully defensive: a missing OR corrupt file yields an empty store, never a throw.
/// - <see cref="Add"/> caps the retained list at <see cref="MaxEntries"/> (drops oldest beyond),
///   writes best-effort (a failed write never propagates into the dictation loop), then raises
///   <see cref="Changed"/> on the calling thread.
/// - The in-memory list is lock-guarded; <see cref="All"/> returns a snapshot copy.
/// </summary>
public sealed class JsonDictationHistoryStore : IDictationHistoryStore
{
    private const int MaxEntries = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly object _gate = new();
    // Newest-first, oldest last.
    private readonly List<DictationHistoryEntry> _entries = new();

    public event Action? Changed;

    public JsonDictationHistoryStore()
        : this(DefaultFilePath())
    {
    }

    // Test/DI seam: allow an explicit path.
    public JsonDictationHistoryStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    private static string DefaultFilePath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Kivi");
        return Path.Combine(dir, "dictation-history.json");
    }

    public void Add(DictationHistoryEntry entry)
    {
        // Don't record blanks.
        if (entry is null || string.IsNullOrWhiteSpace(entry.Text))
            return;

        lock (_gate)
        {
            _entries.Insert(0, entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            Save();
        }

        Changed?.Invoke();
    }

    public IReadOnlyList<DictationHistoryEntry> All()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    // Called under _gate.
    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return;

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return;

            var loaded = JsonSerializer.Deserialize<List<DictationHistoryEntry>>(json, JsonOptions);
            if (loaded is null)
                return;

            foreach (var e in loaded)
            {
                if (e is not null && !string.IsNullOrWhiteSpace(e.Text))
                    _entries.Add(e);
            }

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
        }
        catch
        {
            // Corrupt/invalid/unreadable file: start empty, never throw.
            _entries.Clear();
        }
    }

    // Called under _gate.
    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var json = JsonSerializer.Serialize(_entries, JsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // Best-effort persistence: a failed write must not throw into the dictation loop.
        }
    }
}

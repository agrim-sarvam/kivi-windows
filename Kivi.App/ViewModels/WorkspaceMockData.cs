// Kivi.App/ViewModels/WorkspaceMockData.cs
namespace Kivi.App.ViewModels;

public sealed class PersonaModel
{
    public required string Name { get; set; }
    public List<string> AssignedApps { get; set; } = new();
    public List<string> ToneRules { get; set; } = new();
    public List<string> AttachedPresetNames { get; set; } = new();
}

public sealed class PresetModel
{
    public required string Name { get; set; }
    public required string Instruction { get; set; }
}

public sealed class MemoryEntryModel
{
    public required string Original { get; set; }
    public required string Corrected { get; set; }
    public DateTimeOffset AddedAt { get; set; }
}

/// <summary>
/// In-memory-only mock data for Personas/Presets/Memory, per the design spec: these three
/// pages get a complete, demoable UI now, but no real persistence or backend wiring (no
/// per-app auto-detection, no prompt injection, no disk storage). Resets to this seed on
/// every app restart. Kept as static state (not a DI-registered service) since nothing
/// else in the app depends on it and it isn't meant to model real, durable behavior yet.
/// </summary>
public static class WorkspaceMockData
{
    public static List<PersonaModel> Personas { get; } = new()
    {
        new PersonaModel
        {
            Name = "work messaging",
            AssignedApps = new() { "Slack", "Teams", "Discord" },
            ToneRules = new() { "Keep messages under three sentences", "Address seniors by first name, no \"sir\"", "Never use em dashes" },
            AttachedPresetNames = new() { "standup summariser" },
        },
        new PersonaModel { Name = "email", AssignedApps = new() { "Mail", "Outlook" }, ToneRules = new() { "Formal greeting and sign-off" }, AttachedPresetNames = new() },
        new PersonaModel { Name = "developer", AssignedApps = new() { "Cursor", "VS Code" }, ToneRules = new() { "Keep code comments terse" }, AttachedPresetNames = new() },
        new PersonaModel { Name = "casual", AssignedApps = new() { "WhatsApp", "Notes" }, ToneRules = new(), AttachedPresetNames = new() },
    };

    public static List<PresetModel> Presets { get; } = new()
    {
        new PresetModel { Name = "standup summariser", Instruction = "Summarize into: what I did yesterday, what I'm doing today, blockers." },
        new PresetModel { Name = "make it formal", Instruction = "Rewrite in a more formal, professional tone." },
        new PresetModel { Name = "make it concise", Instruction = "Cut to the essential point, remove filler words." },
    };

    public static List<MemoryEntryModel> MemoryEntries { get; } = new()
    {
        new MemoryEntryModel { Original = "sarvam", Corrected = "Sarvam", AddedAt = DateTimeOffset.UtcNow.AddDays(-3) },
        new MemoryEntryModel { Original = "kivi", Corrected = "Kivi", AddedAt = DateTimeOffset.UtcNow.AddDays(-2) },
        new MemoryEntryModel { Original = "priyank", Corrected = "Priyank", AddedAt = DateTimeOffset.UtcNow.AddDays(-1) },
    };
}

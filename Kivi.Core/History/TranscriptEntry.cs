namespace Kivi.Core.History;

public sealed record TranscriptEntry(
    string Id,
    string Text,
    DateTimeOffset Timestamp,
    string AppName,
    string? LanguageCode,
    int WordCount,
    bool WasRewrite);

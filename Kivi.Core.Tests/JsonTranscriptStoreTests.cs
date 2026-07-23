using Kivi.Core.History;
using Xunit;

public class JsonTranscriptStoreTests
{
    private static string TempPath() => Path.Combine(Path.GetTempPath(), $"kivi-history-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void LoadAll_ReturnsEmpty_WhenFileDoesNotExist()
    {
        var store = new JsonTranscriptStore(TempPath());
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void Append_ThenLoadAll_RoundTripsEntry()
    {
        var path = TempPath();
        try
        {
            var store = new JsonTranscriptStore(path);
            var entry = new TranscriptEntry("1", "hello world", DateTimeOffset.UtcNow, "Slack", "en-IN", 2, false);
            store.Append(entry);

            var loaded = new JsonTranscriptStore(path).LoadAll();

            Assert.Single(loaded);
            Assert.Equal("hello world", loaded[0].Text);
            Assert.Equal("Slack", loaded[0].AppName);
            Assert.Equal(2, loaded[0].WordCount);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Append_Twice_KeepsBothEntries_NewestLast()
    {
        var path = TempPath();
        try
        {
            var store = new JsonTranscriptStore(path);
            store.Append(new TranscriptEntry("1", "first", DateTimeOffset.UtcNow.AddMinutes(-5), "Slack", "en-IN", 1, false));
            store.Append(new TranscriptEntry("2", "second", DateTimeOffset.UtcNow, "Mail", "en-IN", 1, false));

            var loaded = store.LoadAll();

            Assert.Equal(2, loaded.Count);
            Assert.Equal("first", loaded[0].Text);
            Assert.Equal("second", loaded[1].Text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        var path = TempPath();
        try
        {
            var store = new JsonTranscriptStore(path);
            store.Append(new TranscriptEntry("1", "hello", DateTimeOffset.UtcNow, "Slack", "en-IN", 1, false));
            store.Clear();

            Assert.Empty(store.LoadAll());
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadAll_ReturnsEmpty_WhenFileIsCorrupt()
    {
        var path = TempPath();
        try
        {
            File.WriteAllText(path, "{ this is not valid json");
            var store = new JsonTranscriptStore(path);
            Assert.Empty(store.LoadAll());
        }
        finally { File.Delete(path); }
    }
}

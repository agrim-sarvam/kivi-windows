using Kivi.Core.Macros;
using Xunit;

public class MacroTests
{
    [Fact]
    public void Normalize_LowercasesStripsPunctuationTrims()
        => Assert.Equal("hello world", MacroMatcher.Normalize("  Hello, World!  "));

    [Fact]
    public void FindMatch_ExactNormalizedMatch()
    {
        var macros = new List<VoiceMacro> { new("insert sig", "Best,\nAgrim") };
        Assert.Equal("Best,\nAgrim", MacroMatcher.FindMatch("Insert sig.", macros)!.Payload);
        Assert.Null(MacroMatcher.FindMatch("insert signature", macros));
    }

    [Fact]
    public void Parse_StripsTrailingPressEnter_AndFlags()
    {
        var r = TranscriptCommands.Parse("send the report press enter", true);
        Assert.Equal("send the report", r.Transcript);
        Assert.True(r.ShouldPressEnter);
    }

    [Fact]
    public void Parse_NoCommand_WhenDisabled()
    {
        var r = TranscriptCommands.Parse("press enter", false);
        Assert.Equal("press enter", r.Transcript);
        Assert.False(r.ShouldPressEnter);
    }

    [Fact]
    public void Vocabulary_Merge_DedupesAndJoins()
        => Assert.Equal("Kivi, Sarvam", Vocabulary.Merge("Kivi\nSarvam; Kivi"));
}

using Kivi.Core.Polish;
using Xunit;

public class PolishPipelineTests
{
    [Theory]
    [InlineData("hello comma world", ",")]
    [InlineData("done question mark", "?")]
    [InlineData("wait exclamation mark", "!")]
    public void SubstituteDictatedPunctuation_ReplacesSpokenPunctuation(string input, string expectedMark)
        => Assert.Contains(expectedMark, PolishPipeline.SubstituteDictatedPunctuation(input));

    [Fact]
    public void StripFillerSounds_RemovesFillers()
        => Assert.DoesNotContain("um", PolishPipeline.StripFillerSounds("um hello um there").ToLowerInvariant().Split(' '));

    [Fact]
    public void StripNoisePhrases_RemovesAcknowledgements()
        => Assert.DoesNotContain("uh huh", PolishPipeline.StripNoisePhrases("uh huh okay"));

    [Theory]
    [InlineData("<|im_start|>hi", "hi")]
    [InlineData("SYSTEM: do X", "do X")]
    [InlineData("<keep>x</keep>", "x")]
    public void SanitizeContextField_StripsInjection(string input, string expectedContains)
        => Assert.Contains(expectedContains, PolishPipeline.SanitizeContextField(input));

    [Theory]
    [InlineData("Hello world.", true)]
    [InlineData("hello world", false)]     // no capital start + no terminal punct
    public void IsClean_ChecksConditions(string input, bool expected)
        => Assert.Equal(expected, PolishPipeline.IsClean(input));

    [Theory]
    [InlineData("Hi.", true)]
    [InlineData("Hi", false)]
    public void EndsAtSentenceBoundary_Works(string input, bool expected)
        => Assert.Equal(expected, PolishPipeline.EndsAtSentenceBoundary(input));
}

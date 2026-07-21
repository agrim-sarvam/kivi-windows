using Kivi.Core.Config;
using Xunit;

public class AppConfigTests
{
    [Fact]
    public void Default_HasGroqBaseUrls_AndValidates()
    {
        var cfg = AppConfig.Default();
        Assert.Equal("https://api.groq.com/openai/v1", cfg.TranscriptionBaseUrl);
        Assert.Equal("https://api.groq.com/openai/v1", cfg.ChatBaseUrl);
        Assert.Equal("whisper-large-v3", cfg.TranscriptionModel);
        cfg.Validate(); // must not throw
    }

    [Theory]
    [InlineData("http://insecure.example/v1")]
    [InlineData("not-a-url")]
    [InlineData("")]
    public void Validate_RejectsNonHttpsOrMalformedBaseUrl(string bad)
    {
        var cfg = AppConfig.Default();
        cfg.ChatBaseUrl = bad;
        Assert.Throws<ArgumentException>(() => cfg.Validate());
    }

    [Fact]
    public void Default_HasOnboardingAndOrbAndContextAndHotkeyDefaults()
    {
        var c = AppConfig.Default();
        Assert.False(c.OnboardingCompleted);
        Assert.Equal("#41691E", c.OrbAccentColor);
        Assert.True(c.ScreenContextEnabled);
        Assert.Equal(0xA3u, c.HotkeyVirtualKeyCode);
    }
}

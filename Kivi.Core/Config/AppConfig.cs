using Kivi.Core.Macros;
namespace Kivi.Core.Config;

public sealed class AppConfig
{
    public string TranscriptionBaseUrl { get; set; } = "https://api.sarvam.ai";
    public string ChatBaseUrl { get; set; } = "https://api.sarvam.ai";
    public string TranscriptionModel { get; set; } = "saaras:v3";
    public string CleanupModel { get; set; } = "sarvam-30b";
    public string FallbackModel { get; set; } = "sarvam-105b";
    public string? OutputLanguage { get; set; }
    public string? TranscriptionLanguage { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    public string CustomVocabulary { get; set; } = "";
    public List<VoiceMacro> Macros { get; set; } = new();
    public bool PressEnterCommandEnabled { get; set; } = true;
    public bool MetricsEnabled { get; set; }
    public bool OnboardingCompleted { get; set; }
    public string OrbAccentColor { get; set; } = "#41691E";
    public bool ScreenContextEnabled { get; set; } = true;
    public uint HotkeyVirtualKeyCode { get; set; } = 0xA3; // VK_RCONTROL (Right Ctrl)
    public uint RewriteHotkeyVirtualKeyCode { get; set; } = 0xA5; // VK_RMENU (Right Alt) -- "hey kivi" rewrite

    public static AppConfig Default() => new();

    public void Validate()
    {
        ValidateUrl(TranscriptionBaseUrl, nameof(TranscriptionBaseUrl));
        ValidateUrl(ChatBaseUrl, nameof(ChatBaseUrl));
    }

    private static void ValidateUrl(string value, string name)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException($"{name} must be an absolute HTTPS URL, got: '{value}'", name);
    }
}

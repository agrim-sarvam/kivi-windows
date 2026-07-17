using Kivi.Core.Macros;
namespace Kivi.Core.Config;

public sealed class AppConfig
{
    public string TranscriptionBaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string ChatBaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string TranscriptionModel { get; set; } = "whisper-large-v3";
    public string CleanupModel { get; set; } = "openai/gpt-oss-20b";
    public string FallbackModel { get; set; } = "qwen/qwen3-32b";
    public string? OutputLanguage { get; set; }
    public string? TranscriptionLanguage { get; set; }
    public int TimeoutSeconds { get; set; } = 20;
    public string CustomVocabulary { get; set; } = "";
    public List<VoiceMacro> Macros { get; set; } = new();
    public bool PressEnterCommandEnabled { get; set; } = true;
    public bool MetricsEnabled { get; set; }

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

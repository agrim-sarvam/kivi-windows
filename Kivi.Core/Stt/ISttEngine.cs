namespace Kivi.Core.Stt;

/// <summary>
/// Speech-to-text. <paramref name="mode"/> selects Sarvam's transcription behavior:
/// "translit" (romanized Hinglish -- English words in English, Indic words in Latin
/// letters) for the primary dictation hotkey, "translate" (everything rendered as proper
/// English) for the English-mode hotkey. See <see cref="SttMode"/> for the known values.
/// </summary>
public interface ISttEngine
{
    Task<string> TranscribeAsync(byte[] wav, string mode, CancellationToken ct);
}

/// <summary>Sarvam speech-to-text mode values used by Kivi's two dictation hotkeys.</summary>
public static class SttMode
{
    /// <summary>Romanized code-mix: English stays English, Hindi becomes Latin-letter "Hinglish".</summary>
    public const string Hinglish = "translit";

    /// <summary>Translate any spoken language into proper English.</summary>
    public const string English = "translate";
}

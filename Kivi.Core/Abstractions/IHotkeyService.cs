namespace Kivi.Core.Abstractions;

public interface IHotkeyService
{
    event Action? HoldStarted;
    event Action? HoldEnded;
    // The second dictation hotkey: hold to dictate in English-translate mode.
    event Action? EnglishHoldStarted;
    event Action? EnglishHoldEnded;
    void Start();
    void Stop();
    void SetHotkey(uint virtualKeyCode);
    void SetEnglishHotkey(uint virtualKeyCode);
    // Temporarily pauses/resumes hook handling without changing which key is bound (unlike
    // SetHotkey/SetEnglishHotkey). Used by the tray icon's "Pause dictation" command.
    void SetEnabled(bool enabled);
}

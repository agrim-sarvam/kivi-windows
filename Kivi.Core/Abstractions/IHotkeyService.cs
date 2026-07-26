namespace Kivi.Core.Abstractions;

public interface IHotkeyService
{
    // Press-and-hold to dictate; release to stop. bool arg: true = English hotkey (translate),
    // false = primary hotkey (Hinglish).
    event Action? HoldStarted;
    event Action? HoldEnded;
    // The second dictation hotkey: hold to dictate in English-translate mode.
    event Action? EnglishHoldStarted;
    event Action? EnglishHoldEnded;
    // Double-tap a hotkey to start hands-free dictation (recording continues with the key
    // released); a single tap of that same key stops it. Fires once to start, once to stop.
    // bool arg: true = the English hotkey was used, false = the primary hotkey.
    event Action<bool>? HandsFreeToggled;
    void Start();
    void Stop();
    void SetHotkey(uint virtualKeyCode);
    void SetEnglishHotkey(uint virtualKeyCode);
    // Temporarily pauses/resumes hook handling without changing which key is bound (unlike
    // SetHotkey/SetEnglishHotkey). Used by the tray icon's "Pause dictation" command.
    void SetEnabled(bool enabled);
}

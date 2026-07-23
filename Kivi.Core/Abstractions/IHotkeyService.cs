namespace Kivi.Core.Abstractions;

public interface IHotkeyService
{
    event Action? HoldStarted;
    event Action? HoldEnded;
    event Action? RewriteHoldStarted;
    event Action? RewriteHoldEnded;
    // Fire only while ArmReviewKeys() is active -- the hey-kivi accept/reject step.
    event Action? ReviewAccepted;
    event Action? ReviewCancelled;
    void Start();
    void Stop();
    void SetHotkey(uint virtualKeyCode);
    void SetRewriteHotkey(uint virtualKeyCode);
    void ArmReviewKeys();
    void DisarmReviewKeys();
    // Temporarily pauses/resumes hook handling without changing which key is bound (unlike
    // SetHotkey/SetRewriteHotkey). Used by the tray icon's "Pause dictation" command.
    void SetEnabled(bool enabled);
}

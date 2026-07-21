namespace Kivi.Core.Abstractions;

public interface IHotkeyService
{
    event Action HoldStarted;
    event Action HoldEnded;
    void Start();
    void Stop();
    void SetHotkey(uint virtualKeyCode);
}

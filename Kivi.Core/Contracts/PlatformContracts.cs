namespace Kivi.Core.Contracts;

// The in-process platform seams (tripwire T1 + T4). Kivi.Platform implements these for Windows;
// Kivi.Core consumes them via DI. No IPC bus — one process, async/await + events.
// Mirrors the Electron PlatformShell interface (src/main/platform/PlatformShell.ts), re-expressed
// as idiomatic .NET. Signatures per MASTER-PLAN §4; fleshed out in later phases.

/// <summary>A gesture edge from the global hotkey (down/up), fed to the pure GestureClassifier.</summary>
public enum GestureEdgeKind { Down, Up }

public readonly record struct GestureEdge(GestureEdgeKind Kind, long TimestampMs);

/// <summary>The frontmost (target) application captured at key-down.
/// Named AppTarget (not AppContext) to avoid colliding with System.AppContext.</summary>
public readonly record struct AppTarget(string? AppName, string? ExePath, string? WindowTitle);

public readonly record struct PasteMeta(bool IsTerminal, bool IsSecureField);

public enum PasteOutcome { Ok, SecureFieldBlocked, Failed }

/// <summary>Global hold-to-talk hotkey (WH_KEYBOARD_LL on a dedicated thread). See Kivi.Platform.Hotkey.</summary>
public interface IHotkeyService
{
    event Action<GestureEdge>? Edge;
    void Start();
    void Consume(bool on);
}

/// <summary>Clipboard + synthesized Ctrl+V paste into the frontmost app. See Kivi.Platform.Paste.</summary>
public interface IPasteService
{
    Task<PasteOutcome> InsertAsync(string text, PasteMeta meta);
}

/// <summary>The native layered orb overlay host. See Kivi.Platform.Overlay.</summary>
public interface IOverlayHost
{
    void ApplyNonActivating();
    void SetClickThrough(bool clickThrough);
}

/// <summary>Resolves the current foreground app (captured at key-down). See Kivi.Platform.Frontmost.</summary>
public interface IFrontmostApp
{
    AppTarget? Current { get; }
}

/// <summary>16 kHz Int16 mono LE PCM capture, ~100 ms (3200-byte) frames. See Kivi.Platform.Audio.</summary>
public interface IAudioCapture
{
    event Action<byte[]>? Frame;
    void Start();
    Task StopAsync();
}

/// <summary>DPAPI-backed secret store (replaces Keychain/safeStorage). See Kivi.Platform.Secrets.</summary>
public interface ISecretStore
{
    string? Read(string key);
    void Write(string key, string value);
}

/// <summary>Notification-area tray icon + popover host. See Kivi.Platform.Tray.</summary>
public interface ITrayHost
{
    void Show();
    void Hide();
}

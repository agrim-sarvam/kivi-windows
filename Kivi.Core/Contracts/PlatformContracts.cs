namespace Kivi.Core.Contracts;

// The in-process platform seams (tripwire T1 + T4). Kivi.Platform implements these for Windows;
// Kivi.Core consumes them via DI. No IPC bus — one process, async/await + events.
// Mirrors the Electron PlatformShell interface (src/main/platform/PlatformShell.ts), re-expressed
// as idiomatic .NET. Signatures per MASTER-PLAN §4; fleshed out in later phases.

/// <summary>A gesture edge from the global hotkey (down/up), fed to the pure GestureClassifier.</summary>
public enum GestureEdgeKind { Down, Up }

public readonly record struct GestureEdge(GestureEdgeKind Kind, long TimestampMs);

/// <summary>The frontmost (target) application captured at key-down.
/// Named AppTarget (not AppContext) to avoid colliding with System.AppContext.
/// WindowHandle is an opaque platform window handle (HWND on Windows, boxed as IntPtr via nint) so
/// the paste service can restore focus to the actual captured window before pasting — without it,
/// SendInput targets whatever window happens to have focus AT PASTE TIME, which silently diverges
/// from the app the user was dictating into the moment they so much as glance at/click any part of
/// the Kivi UI (the box, a satellite, thumbs) while a take is in flight.</summary>
public readonly record struct AppTarget(string? AppName, string? ExePath, string? WindowTitle, nint WindowHandle = 0);

public readonly record struct PasteMeta(bool IsTerminal, bool IsSecureField);

public enum PasteOutcome { Ok, SecureFieldBlocked, Failed }

/// <summary>Global hold-to-talk hotkey (WH_KEYBOARD_LL on a dedicated thread). See Kivi.Platform.Hotkey.</summary>
public interface IHotkeyService
{
    event Action<GestureEdge>? Edge;
    void Start();
    void Consume(bool on);

    /// <summary>
    /// Rebind the trigger to a new chord live, without tearing down the hook. The chord is a set of
    /// Windows virtual-key codes that must all be held (a single key, or a modifier-only combo like
    /// Ctrl+Win / both Ctrls). Takes effect on the next physical press; never fires a spurious Down
    /// on rebind. The storage form is <c>Kivi.Core.Hotkey.HotkeyChord.ToStorageString()</c>.
    /// </summary>
    void Rebind(Kivi.Core.Hotkey.HotkeyChord chord);
}

/// <summary>Clipboard + synthesized Ctrl+V paste into the frontmost app. See Kivi.Platform.Paste.</summary>
public interface IPasteService
{
    Task<PasteOutcome> InsertAsync(string text, PasteMeta meta, AppTarget? target = null);
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

    /// <summary>
    /// Push a phase change so the tray icon can re-tint + retime its breathing cycle.
    /// Kivi.Core stays System.Drawing-free: color is a plain (r,g,b) tuple, not System.Drawing.Color.
    /// </summary>
    void UpdateState(string phaseName, (byte R, byte G, byte B) baseColor);
}

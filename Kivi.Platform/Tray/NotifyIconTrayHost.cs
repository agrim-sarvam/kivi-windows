using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using Kivi.Core.Contracts;
using Application = System.Windows.Application;

namespace Kivi.Platform.Tray;

/// <summary>
/// PHASE P6 (M7) — notification-area tray icon: the real kivi.ico, plain (per explicit user
/// instruction — no breathing pill/badge compositing). Loaded directly from the file copied next
/// to the exe (Kivi.App.csproj: `&lt;None Include="kivi.ico" CopyToOutputDirectory="PreserveNewest"/&gt;`).
/// `UpdateState` is a no-op hook kept only so the DictationOrchestrator call site compiles — the
/// icon does not change with dictation phase.
/// </summary>
public sealed class NotifyIconTrayHost : ITrayHost, IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;
    private readonly Icon? _icon;
    private TrayPopover? _popover;
    private bool _disposed;

    public NotifyIconTrayHost()
    {
        _icon = LoadKiviIcon();

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Visible = false,
            Text = "Kivi",
            Icon = _icon ?? SystemIcons.Application,
        };
        // MouseUp (not Click) fires reliably for a plain left-click on WinForms NotifyIcon across
        // Windows versions; Click alone can silently no-op on some shell versions/right-click chords.
        _notifyIcon.MouseUp += OnNotifyIconMouseUp;
    }

    /// <summary>Loads kivi.ico from beside the running exe. Never throws — falls back to the OS default.</summary>
    private static Icon? LoadKiviIcon()
    {
        try
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName
                ?? Assembly.GetEntryAssembly()?.Location
                ?? string.Empty;
            string exeDir = Path.GetDirectoryName(exePath) ?? ".";
            string icoPath = Path.Combine(exeDir, "kivi.ico");
            return File.Exists(icoPath) ? new Icon(icoPath) : null;
        }
        catch
        {
            return null;
        }
    }

    public void Show() => _notifyIcon.Visible = true;

    public void Hide() => _notifyIcon.Visible = false;

    /// <summary>No-op — the tray icon is the plain Kivi logo regardless of dictation state.</summary>
    public void UpdateState(string phaseName, (byte R, byte G, byte B) baseColor) { }

    private void OnNotifyIconMouseUp(object? sender, System.Windows.Forms.MouseEventArgs e)
    {
        // Only react to the left button — right-click is reserved for a future context menu, and
        // WinForms surfaces both under MouseUp on some shell versions.
        if (e.Button != System.Windows.Forms.MouseButtons.Left) return;

        Application.Current?.Dispatcher.Invoke(() =>
        {
            _popover ??= new TrayPopover();
            _popover.ShowNearCursor();
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.MouseUp -= OnNotifyIconMouseUp;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon?.Dispose();
    }
}

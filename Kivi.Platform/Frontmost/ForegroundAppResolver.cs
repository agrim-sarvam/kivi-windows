using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Kivi.Core.Contracts;

namespace Kivi.Platform.Frontmost;

/// <summary>
/// REAL foreground-app resolver. Resolves the current foreground window to an <see cref="AppTarget"/>
/// via GetForegroundWindow → GetWindowThreadProcessId → QueryFullProcessImageName (exe path) +
/// GetWindowText (title).
///
/// Memoizes the last NON-Kivi foreground app so a take is still attributed to (and pasted into) the
/// real target even while the orb is transiently frontmost (platform-coupling-audit §3). The
/// orchestrator reads <see cref="Current"/> at key-down.
/// </summary>
public sealed class ForegroundAppResolver : IFrontmostApp
{
    private readonly int _ownProcessId;
    private AppTarget? _lastExternal;

    public ForegroundAppResolver()
    {
        using var p = Process.GetCurrentProcess();
        _ownProcessId = p.Id;
    }

    /// <summary>
    /// The current target. Resolves the live foreground window; if it belongs to this (Kivi) process
    /// the memoized last-external app is returned instead so paste/context still target the real app.
    /// </summary>
    public AppTarget? Current
    {
        get
        {
            var resolved = Resolve();
            if (resolved is { } t && !IsOwnProcess(t.ExePath))
                _lastExternal = t;
            return _lastExternal ?? resolved;
        }
    }

    private bool IsOwnProcess(string? exePath)
    {
        // We compare by resolved PID at resolve-time (below); this string check is a cheap secondary
        // guard for the current process's own exe path.
        if (string.IsNullOrEmpty(exePath)) return false;
        try
        {
            using var p = Process.GetCurrentProcess();
            var own = p.MainModule?.FileName;
            return own is not null && string.Equals(own, exePath, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private AppTarget? Resolve()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return null;
        if (pid == (uint)_ownProcessId) return null; // our own window — caller falls back to memo

        string? exePath = TryGetProcessImagePath(pid);
        string? appName = exePath is not null
            ? Path.GetFileNameWithoutExtension(exePath)
            : null;
        string? title = TryGetWindowText(hwnd);

        return new AppTarget(appName, exePath, title, hwnd);
    }

    private static string? TryGetProcessImagePath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    private static string? TryGetWindowText(IntPtr hwnd)
    {
        int len = GetWindowTextLength(hwnd);
        if (len <= 0) return null;
        var sb = new StringBuilder(len + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    // --- Win32 ---

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(IntPtr hWnd);
}

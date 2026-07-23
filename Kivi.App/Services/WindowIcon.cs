// Kivi.App/Services/WindowIcon.cs
using Microsoft.UI;
using Microsoft.UI.Windowing;
using WinRT.Interop;

namespace Kivi.App.Services;

/// <summary>
/// Sets a WinUI3 window's title-bar/taskbar/Alt-Tab icon. Kivi.App.csproj's
/// ApplicationIcon only brands the exe file itself (Explorer, shortcuts) -- each
/// AppWindow needs its icon set explicitly to show it in its own title bar and taskbar
/// entry, which OnboardingWindow/MainAppWindow don't do without this.
/// </summary>
public static class WindowIcon
{
    private const string IconPath = "Assets\\Icons\\kivi.ico";

    public static void Apply(Microsoft.UI.Xaml.Window window)
    {
        nint hwnd = WindowNative.GetWindowHandle(window);
        var appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(hwnd));
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, IconPath);
        if (System.IO.File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }
    }
}

// Kivi.App/Services/MicPermission.cs
using Windows.Security.Authorization.AppCapabilityAccess;

namespace Kivi.App.Services;

/// <summary>
/// Wraps the WinRT AppCapability API for checking/requesting microphone access.
/// Fails OPEN on exception: an unpackaged app on some Windows configs can't query
/// AppCapability at all, and we must not permanently block a user whose mic actually
/// works. The real gate is the actual capture succeeding at dictation time.
/// </summary>
public static class MicPermission
{
    public static Task<bool> CheckAsync()
    {
        try
        {
            var cap = AppCapability.Create("microphone");
            var status = cap.CheckAccess();
            return Task.FromResult(status == AppCapabilityAccessStatus.Allowed);
        }
        catch
        {
            return Task.FromResult(true);
        }
    }

    public static async Task<bool> RequestAsync()
    {
        try
        {
            var cap = AppCapability.Create("microphone");
            var status = await cap.RequestAccessAsync();
            return status == AppCapabilityAccessStatus.Allowed;
        }
        catch
        {
            return true;
        }
    }

    public static void OpenSettings()
    {
        _ = Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-microphone"));
    }
}

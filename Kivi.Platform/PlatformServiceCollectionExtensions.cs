using Kivi.Core.Contracts;
using Kivi.Platform.Audio;
using Kivi.Platform.Frontmost;
using Kivi.Platform.Hotkey;
using Kivi.Platform.Overlay;
using Kivi.Platform.Paste;
using Kivi.Platform.Secrets;
using Kivi.Platform.Tray;
using Microsoft.Extensions.DependencyInjection;

namespace Kivi.Platform;

/// <summary>
/// Registers the Windows-native platform seams against the Kivi.Core.Contracts interfaces.
/// The DI/tripwire-T1+T4 boundary: Kivi.Core depends only on the interfaces; this wires the impls.
/// </summary>
public static class PlatformServiceCollectionExtensions
{
    public static IServiceCollection AddKiviPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IHotkeyService, LowLevelKeyboardHookService>();
        services.AddSingleton<IPasteService, SendInputPasteService>();
        services.AddSingleton<IFrontmostApp, ForegroundAppResolver>();
        services.AddSingleton<IOverlayHost, LayeredOrbHost>();
        services.AddSingleton<IAudioCapture, WasapiCaptureService>();
        services.AddSingleton<ISecretStore, DpapiSecretStore>();
        services.AddSingleton<ITrayHost, NotifyIconTrayHost>();
        return services;
    }
}

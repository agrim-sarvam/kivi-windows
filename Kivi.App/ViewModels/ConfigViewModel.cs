// Kivi.App/ViewModels/ConfigViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;

namespace Kivi.App.ViewModels;

/// <summary>
/// Bindable config state for the onboarding Config page. Property changes write straight
/// through to the shared AppConfig singleton (not yet persisted); Persist() flips
/// OnboardingCompleted and saves via IAppConfigStore.
/// </summary>
public partial class ConfigViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly IAppConfigStore _store;
    private readonly IHotkeyService _hotkey;

    public ConfigViewModel(AppConfig config, IAppConfigStore store, IHotkeyService hotkey)
    {
        _config = config; _store = store; _hotkey = hotkey;
        OrbAccentColor = config.OrbAccentColor;
        TranscriptionLanguage = config.TranscriptionLanguage ?? "auto";
        ScreenContextEnabled = config.ScreenContextEnabled;
        HotkeyVk = config.HotkeyVirtualKeyCode;
        LaunchAtLogin = Services.StartupLauncher.IsEnabled();
    }

    [ObservableProperty] private string _orbAccentColor = "#41691E";
    [ObservableProperty] private string _transcriptionLanguage = "auto";
    [ObservableProperty] private bool _screenContextEnabled = true;
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private uint _hotkeyVk = 0xA3;

    partial void OnOrbAccentColorChanged(string value) => _config.OrbAccentColor = value;

    partial void OnTranscriptionLanguageChanged(string value)
        => _config.TranscriptionLanguage = value == "auto" ? null : value;

    partial void OnScreenContextEnabledChanged(bool value) => _config.ScreenContextEnabled = value;

    partial void OnLaunchAtLoginChanged(bool value) => Services.StartupLauncher.SetEnabled(value);

    partial void OnHotkeyVkChanged(uint value)
    {
        _config.HotkeyVirtualKeyCode = value;
        _hotkey.SetHotkey(value);
    }

    public void Persist()
    {
        _config.OnboardingCompleted = true;
        _store.Save(_config);
    }
}

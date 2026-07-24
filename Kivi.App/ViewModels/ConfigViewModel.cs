// Kivi.App/ViewModels/ConfigViewModel.cs
using CommunityToolkit.Mvvm.ComponentModel;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;

namespace Kivi.App.ViewModels;

/// <summary>
/// Bindable config state shared by onboarding's ConfigPage and the always-available
/// SettingsPage. Property changes write straight through to the shared AppConfig singleton
/// AND persist immediately via IAppConfigStore -- a shared view model used from two
/// different hosting pages should not have close-only-persists semantics for one of its two
/// hosts (SettingsPage has no terminal "Done" step, so it must persist per-change).
/// Persist() additionally flips OnboardingCompleted, for onboarding's own "Done" button.
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
        EnglishHotkeyVk = config.EnglishHotkeyVirtualKeyCode;
        SoundOnPasteEnabled = config.SoundOnPasteEnabled;
        IncognitoDictationEnabled = config.IncognitoDictationEnabled;
        PressAndHoldDelayMs = config.PressAndHoldDelayMs;
        LaunchAtLogin = Services.StartupLauncher.IsEnabled();
    }

    [ObservableProperty] private string _orbAccentColor = "#41691E";
    [ObservableProperty] private string _transcriptionLanguage = "auto";
    [ObservableProperty] private bool _screenContextEnabled = true;
    [ObservableProperty] private bool _launchAtLogin;
    [ObservableProperty] private uint _hotkeyVk = 0xA3;
    [ObservableProperty] private uint _englishHotkeyVk = 0xA5;
    [ObservableProperty] private bool _soundOnPasteEnabled = true;
    [ObservableProperty] private bool _incognitoDictationEnabled;
    [ObservableProperty] private int _pressAndHoldDelayMs = 100;

    partial void OnOrbAccentColorChanged(string value) { _config.OrbAccentColor = value; _store.Save(_config); }

    partial void OnTranscriptionLanguageChanged(string value)
    {
        _config.TranscriptionLanguage = value == "auto" ? null : value;
        _store.Save(_config);
    }

    partial void OnScreenContextEnabledChanged(bool value) { _config.ScreenContextEnabled = value; _store.Save(_config); }

    partial void OnLaunchAtLoginChanged(bool value) => Services.StartupLauncher.SetEnabled(value);

    partial void OnHotkeyVkChanged(uint value)
    {
        _config.HotkeyVirtualKeyCode = value;
        _hotkey.SetHotkey(value);
        _store.Save(_config);
    }

    partial void OnEnglishHotkeyVkChanged(uint value)
    {
        _config.EnglishHotkeyVirtualKeyCode = value;
        _hotkey.SetEnglishHotkey(value);
        _store.Save(_config);
    }

    partial void OnSoundOnPasteEnabledChanged(bool value) { _config.SoundOnPasteEnabled = value; _store.Save(_config); }

    partial void OnIncognitoDictationEnabledChanged(bool value) { _config.IncognitoDictationEnabled = value; _store.Save(_config); }

    partial void OnPressAndHoldDelayMsChanged(int value) { _config.PressAndHoldDelayMs = value; _store.Save(_config); }

    public void Persist()
    {
        _config.OnboardingCompleted = true;
        _store.Save(_config);
    }
}

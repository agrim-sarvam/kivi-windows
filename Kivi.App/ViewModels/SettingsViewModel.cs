using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kivi.Core.Abstractions;
using Kivi.Core.Config;

namespace Kivi.App.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly AppConfig _config;
    private readonly IAppConfigStore _store;
    private readonly ISecretStore _secrets;

    public SettingsViewModel(AppConfig config, IAppConfigStore store, ISecretStore secrets)
    {
        _config = config;
        _store = store;
        _secrets = secrets;
        ApiKey = _secrets.GetApiKey() ?? "";
        SttBaseUrl = _config.TranscriptionBaseUrl;
        CleanupBaseUrl = _config.ChatBaseUrl;
        TranscriptionModel = _config.TranscriptionModel;
        CleanupModel = _config.CleanupModel;
        CustomVocabulary = _config.CustomVocabulary;
        PressEnterEnabled = _config.PressEnterCommandEnabled;
    }

    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string _sttBaseUrl = "";
    [ObservableProperty] private string _cleanupBaseUrl = "";
    [ObservableProperty] private string _transcriptionModel = "";
    [ObservableProperty] private string _cleanupModel = "";
    [ObservableProperty] private string _customVocabulary = "";
    [ObservableProperty] private bool _pressEnterEnabled;

    partial void OnApiKeyChanged(string value) => _secrets.SetApiKey(value);
    partial void OnSttBaseUrlChanged(string value) => _config.TranscriptionBaseUrl = value;
    partial void OnCleanupBaseUrlChanged(string value) => _config.ChatBaseUrl = value;
    partial void OnTranscriptionModelChanged(string value) => _config.TranscriptionModel = value;
    partial void OnCleanupModelChanged(string value) => _config.CleanupModel = value;
    partial void OnCustomVocabularyChanged(string value) => _config.CustomVocabulary = value;
    partial void OnPressEnterEnabledChanged(bool value) => _config.PressEnterCommandEnabled = value;

    [RelayCommand]
    private void Save() => _store.Save(_config);
}

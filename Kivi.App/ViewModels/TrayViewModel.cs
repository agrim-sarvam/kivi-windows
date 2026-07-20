using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kivi.Core.Orchestration;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kivi.App.ViewModels;

public partial class TrayViewModel : ObservableObject
{
    private readonly IDictationOrchestrator _orch;
    private readonly DispatcherQueue _ui;
    private readonly Action _openSettings;

    public TrayViewModel(IDictationOrchestrator orch, DispatcherQueue ui, Action openSettings)
    {
        _orch = orch;
        _ui = ui;
        _openSettings = openSettings;
        _orch.StateChanged += s => _ui.TryEnqueue(() => Apply(s));
        Apply(_orch.State);
    }

    [ObservableProperty] private BitmapImage? _trayIcon;
    [ObservableProperty] private string _startStopLabel = "Start dictation";

    private void Apply(RecordingState state)
    {
        StartStopLabel = state == RecordingState.Idle ? "Start dictation" : "Stop dictation";
        var asset = state switch
        {
            RecordingState.Idle  => "ms-appx:///Assets/Tray/idle.ico",
            RecordingState.Error => "ms-appx:///Assets/Tray/error.ico",
            _                    => "ms-appx:///Assets/Tray/active.ico"
        };
        TrayIcon = new BitmapImage(new Uri(asset));
    }

    [RelayCommand]
    private void ToggleDictation()
    {
        if (_orch.State == RecordingState.Idle) _orch.Start();
        else _orch.Stop();
    }

    [RelayCommand]
    private void Quit() => Microsoft.UI.Xaml.Application.Current.Exit();

    [RelayCommand]
    private void OpenSettings() => _openSettings();
}

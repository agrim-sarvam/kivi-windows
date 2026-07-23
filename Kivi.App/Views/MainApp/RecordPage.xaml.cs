// Kivi.App/Views/MainApp/RecordPage.xaml.cs
using Kivi.App.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace Kivi.App.Views.MainApp;

/// <summary>
/// Live view of the real dictation pipeline: DictationBox mirrors the orchestrator's
/// partial transcript while listening, and shows the final cleaned text once done.
/// The hotkey (Right Ctrl) works globally regardless of which window has focus, so this
/// page's only job is to render state -- it doesn't need to own any hotkey logic itself.
/// </summary>
public sealed partial class RecordPage : Page
{
    private readonly OverlayViewModel _vm;

    public RecordPage()
    {
        InitializeComponent();
        var orchestrator = Kivi.App.App.Services.GetRequiredService<Kivi.Core.Orchestration.IDictationOrchestrator>();
        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _vm = new OverlayViewModel(orchestrator, dispatcher);
        _vm.PropertyChanged += OnVmPropertyChanged;
        RenderState();
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => RenderState();

    private void RenderState()
    {
        if (_vm.IsListening && !string.IsNullOrEmpty(_vm.PartialTranscript))
        {
            DictationBox.Text = _vm.PartialTranscript;
        }
    }
}

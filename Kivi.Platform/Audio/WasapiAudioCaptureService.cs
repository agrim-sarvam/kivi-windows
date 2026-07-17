using System.Threading.Channels;
using Kivi.Core.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Kivi.Platform.Audio;

public sealed class WasapiAudioCaptureService : IAudioCaptureService, IDisposable
{
    private static readonly WaveFormat Format = new(16000, 16, 1);
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DeviceNotificationClient _notify;
    private readonly Channel<string> _deviceEvents = Channel.CreateUnbounded<string>();

    private WasapiCapture? _capture;
    private MemoryStream? _stream;
    private WaveFileWriter? _writer;
    private TaskCompletionSource? _stopped;

    public event Action<string>? DeviceChanged;

    public WasapiAudioCaptureService()
    {
        _notify = new DeviceNotificationClient(_deviceEvents.Writer);
        _enumerator.RegisterEndpointNotificationCallback(_notify);
        _ = Task.Run(DeviceWorkerAsync); // reinit off the callback thread
    }

    public Task StartRecordingAsync(CancellationToken ct)
    {
        InitCaptureWithBackoff();
        _stream = new MemoryStream();
        _writer = new WaveFileWriter(_stream, Format);
        _capture!.DataAvailable += OnData;
        _capture.RecordingStopped += (_, __) => _stopped?.TrySetResult();
        _capture.StartRecording();
        return Task.CompletedTask;
    }

    public async Task<byte[]> StopRecordingAsync()
    {
        if (_capture is null || _writer is null || _stream is null) return Array.Empty<byte>();
        _stopped = new TaskCompletionSource();
        _capture.StopRecording();
        await _stopped.Task;
        _writer.Flush();
        var bytes = _stream.ToArray();
        _capture.DataAvailable -= OnData;
        _writer.Dispose(); _stream.Dispose(); _capture.Dispose();
        _writer = null; _stream = null; _capture = null;
        return bytes;
    }

    private void OnData(object? sender, WaveInEventArgs e) => _writer!.Write(e.Buffer, 0, e.BytesRecorded);

    private void InitCaptureWithBackoff()
    {
        int delay = 100;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                // Re-enumerate the default capture endpoint fresh each session -- no cached device handle.
                using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                _capture = new WasapiCapture(device) { WaveFormat = Format };
                return;
            }
            catch when (attempt < 4)
            {
                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 2000);
            }
        }
        throw new InvalidOperationException("No usable capture device after retries");
    }

    private async Task DeviceWorkerAsync()
    {
        await foreach (var id in _deviceEvents.Reader.ReadAllAsync())
            DeviceChanged?.Invoke(id); // re-enumeration happens at next StartRecordingAsync (no cached handle)
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_notify); } catch { }
        _capture?.Dispose(); _writer?.Dispose(); _stream?.Dispose(); _enumerator.Dispose();
    }
}

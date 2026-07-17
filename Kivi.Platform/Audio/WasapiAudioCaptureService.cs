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
    private readonly Channel<DeviceEvent> _deviceEvents = Channel.CreateUnbounded<DeviceEvent>();

    // Guards all mutation of _capture/_writer/_stream so an in-progress reconnect (on the
    // device worker) and an explicit StopRecordingAsync (called from the orchestrator) can
    // never touch these fields at the same time.
    private readonly object _gate = new();

    private WasapiCapture? _capture;
    private MemoryStream? _stream;
    private WaveFileWriter? _writer;
    private TaskCompletionSource? _stopped;

    // True only while a StartRecordingAsync...StopRecordingAsync span is open. Distinct from
    // "_capture != null" because during a reconnect attempt _capture is briefly null while we
    // are still logically recording (the writer/stream keep accumulating regardless).
    private volatile bool _isRecording;

    // Endpoint id we're currently bound to -- used to dedupe the OnDefaultDeviceChanged storm
    // (fires once per role, up to 3x per user change) and to ignore events about devices that
    // aren't the one we're using.
    private string? _currentEndpointId;

    public event Action<string>? DeviceChanged;

    public WasapiAudioCaptureService()
    {
        _notify = new DeviceNotificationClient(_deviceEvents.Writer);
        _enumerator.RegisterEndpointNotificationCallback(_notify);
        _ = Task.Run(DeviceWorkerAsync); // reinit off the callback thread
    }

    public Task StartRecordingAsync(CancellationToken ct)
    {
        using var device = InitCaptureWithBackoff(out var capture);
        lock (_gate)
        {
            _capture = capture;
            _currentEndpointId = device.ID;
            _stream = new MemoryStream();
            _writer = new WaveFileWriter(_stream, Format);
            _capture.DataAvailable += OnData;
            _capture.RecordingStopped += (_, __) => _stopped?.TrySetResult();
            _capture.StartRecording();
            _isRecording = true;
        }
        return Task.CompletedTask;
    }

    public async Task<byte[]> StopRecordingAsync()
    {
        // Signal the worker not to attempt any further reconnects for this session before we
        // start tearing state down, but do the actual field mutation under _gate so a reconnect
        // that's already past this check can't race us.
        _isRecording = false;

        WasapiCapture? captureToStop;
        lock (_gate)
        {
            captureToStop = _capture;
        }

        if (captureToStop is not null)
        {
            _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            captureToStop.StopRecording();
            await _stopped.Task;
        }

        lock (_gate)
        {
            if (_writer is null || _stream is null)
            {
                // Nothing was ever started, or a concurrent path already flushed.
                _capture = null;
                return Array.Empty<byte>();
            }

            if (_capture is not null)
            {
                _capture.DataAvailable -= OnData;
                _capture.Dispose();
                _capture = null;
            }

            _writer.Flush();
            var bytes = _stream.ToArray();
            _writer.Dispose();
            _stream.Dispose();
            _writer = null;
            _stream = null;
            _currentEndpointId = null;
            return bytes;
        }
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        lock (_gate)
        {
            _writer?.Write(e.Buffer, 0, e.BytesRecorded);
        }
    }

    // Acquires a fresh MMDevice + WasapiCapture for the current default capture endpoint, with
    // retry/backoff for transient device-busy/not-found conditions. Returns the MMDevice (caller
    // owns disposal) and hands back the constructed-but-not-yet-started WasapiCapture via `out`.
    private MMDevice InitCaptureWithBackoff(out WasapiCapture capture)
    {
        int delay = 100;
        Exception? lastError = null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                // Re-enumerate the default capture endpoint fresh each attempt -- no cached device handle.
                var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                capture = new WasapiCapture(device) { WaveFormat = Format };
                return device;
            }
            catch (Exception ex)
            {
                lastError = ex;
                if (attempt < 4)
                {
                    Thread.Sleep(delay);
                    delay = Math.Min(delay * 2, 2000);
                }
            }
        }
        throw new InvalidOperationException("No usable capture device after retries", lastError);
    }

    private async Task DeviceWorkerAsync()
    {
        await foreach (var ev in _deviceEvents.Reader.ReadAllAsync())
        {
            // Dedupe the OnDefaultDeviceChanged storm (fires once per role -- up to 3x per user
            // change) and ignore events about endpoints that aren't the one we're bound to.
            bool affectsUs = ev.Kind switch
            {
                DeviceEventKind.DefaultChanged => !string.Equals(ev.DeviceId, _currentEndpointId, StringComparison.Ordinal),
                DeviceEventKind.Removed => string.Equals(ev.DeviceId, _currentEndpointId, StringComparison.Ordinal),
                DeviceEventKind.StateChanged => string.Equals(ev.DeviceId, _currentEndpointId, StringComparison.Ordinal),
                _ => false,
            };

            if (!affectsUs) continue;

            if (_isRecording)
                TryReconnect();

            DeviceChanged?.Invoke(ev.DeviceId);
        }
    }

    // Runs entirely on the worker thread (never inside an IMMNotificationClient callback).
    // Tears down the old capture, re-enumerates + reopens on the new default endpoint with
    // backoff, and resumes appending into the SAME _writer/_stream so the in-progress
    // dictation survives a mid-recording driver swap. Never throws: if reconnect ultimately
    // fails, leaves _capture null so the next StopRecordingAsync flushes the partial recording.
    private void TryReconnect()
    {
        // Stop + detach the OLD capture first (unsubscribe before Dispose to avoid a race with
        // a trailing DataAvailable firing on the old object), but never touch _writer/_stream --
        // those keep accumulating across the swap.
        lock (_gate)
        {
            if (!_isRecording) return; // StopRecordingAsync raced us and already finished.

            var old = _capture;
            _capture = null;
            if (old is not null)
            {
                old.DataAvailable -= OnData;
                try { old.StopRecording(); } catch { /* device may already be gone */ }
                old.Dispose();
            }
        }

        MMDevice? newDevice = null;
        WasapiCapture? newCapture = null;
        try
        {
            newDevice = InitCaptureWithBackoff(out newCapture);
        }
        catch
        {
            // Retries exhausted -- mic genuinely gone. Leave _capture null; the next explicit
            // StopRecordingAsync will flush whatever was captured before the failure.
            newDevice?.Dispose();
            return;
        }

        using (newDevice)
        {
            lock (_gate)
            {
                if (!_isRecording || _writer is null)
                {
                    // Stopped while we were reconnecting -- discard the fresh capture, nothing to resume.
                    newCapture.Dispose();
                    return;
                }

                _capture = newCapture;
                _currentEndpointId = newDevice.ID;
                _capture.DataAvailable += OnData;
                _capture.RecordingStopped += (_, __) => _stopped?.TrySetResult();
                _capture.StartRecording();
            }
        }
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_notify); } catch { }
        _capture?.Dispose(); _writer?.Dispose(); _stream?.Dispose(); _enumerator.Dispose();
    }
}

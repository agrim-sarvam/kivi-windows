using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Kivi.Platform.Audio;

// What the callback observed. Deliberately tiny + immutable so the callback allocates once
// and returns immediately (IMMNotificationClient rule: must be non-blocking).
internal enum DeviceEventKind { DefaultChanged, Removed, StateChanged }

internal readonly record struct DeviceEvent(DeviceEventKind Kind, string DeviceId, DeviceState State);

// Non-blocking IMMNotificationClient: callbacks ONLY enqueue an event; never block,
// never (un)register here, never release the final MMDevice ref here (spec §3.1 rules).
// NAudio 2.3.0's IMMNotificationClient members match the brief 1:1 (all void, same params).
internal sealed class DeviceNotificationClient : IMMNotificationClient
{
    private readonly ChannelWriter<DeviceEvent> _events;
    public DeviceNotificationClient(ChannelWriter<DeviceEvent> events) => _events = events;

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Capture)
            _events.TryWrite(new DeviceEvent(DeviceEventKind.DefaultChanged, defaultDeviceId ?? "", DeviceState.Active));
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        if (newState is DeviceState.Unplugged or DeviceState.NotPresent)
            _events.TryWrite(new DeviceEvent(DeviceEventKind.StateChanged, deviceId ?? "", newState));
    }

    public void OnDeviceRemoved(string deviceId)
        => _events.TryWrite(new DeviceEvent(DeviceEventKind.Removed, deviceId ?? "", DeviceState.NotPresent));

    public void OnDeviceAdded(string deviceId) { }
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
}

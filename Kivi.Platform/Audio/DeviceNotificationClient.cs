using System.Threading.Channels;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Kivi.Platform.Audio;

// Non-blocking IMMNotificationClient: callbacks ONLY enqueue an endpoint id; never block,
// never (un)register here, never release the final MMDevice ref here (spec §4.2 rules).
// NAudio 2.3.0's IMMNotificationClient members match the brief 1:1 (all void, same params).
internal sealed class DeviceNotificationClient : IMMNotificationClient
{
    private readonly ChannelWriter<string> _events;
    public DeviceNotificationClient(ChannelWriter<string> events) => _events = events;

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    { if (flow == DataFlow.Capture) _events.TryWrite(defaultDeviceId ?? ""); }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    { if (newState is DeviceState.Unplugged or DeviceState.NotPresent) _events.TryWrite(deviceId ?? ""); }
    public void OnDeviceRemoved(string deviceId) => _events.TryWrite(deviceId ?? "");
    public void OnDeviceAdded(string deviceId) { }
    public void OnPropertyValueChanged(string deviceId, PropertyKey key) { }
}

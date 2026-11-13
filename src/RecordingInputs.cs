using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace ShotInk
{
    internal sealed class RecordingInput
    {
        internal readonly string Id, Name;
        internal RecordingInput(string id, string name) { Id = id; Name = name; }
        public override string ToString() { return Name; }
    }
    internal static class RecordingInputs
    {
        internal static List<RecordingInput> List(string selected)
        {
            var result = new List<RecordingInput> { new RecordingInput("", "Windows 默认输入设备") };
            try
            {
                using (var enumerator = new MMDeviceEnumerator())
                    foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                        using (device) result.Add(new RecordingInput(device.ID, device.FriendlyName));
            }
            catch (COMException) { }
            if (!string.IsNullOrEmpty(selected) && !result.Exists(delegate(RecordingInput item) { return item.Id == selected; }))
                result.Add(new RecordingInput(selected, "此前选择的麦克风（当前不可用）"));
            return result;
        }
    }
}

#if NEW_API
using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VcuSwimLab.VREmotionDetection;

public class DeviceManagerHarnessTests
{
    private sealed class ThrowingDevice : Device
    {
        internal int Polls;

        internal ThrowingDevice() : base(null) { }

        internal override (InputType ty, int width) InputSpec => (InputType.Sound, 4);

        internal override (float interval, bool valid) Poll(float[] data)
        {
            Polls++;
            throw new InvalidOperationException("sensor fault");
        }
    }

    [Test]
    public async Task One_failing_device_does_not_stop_the_others_and_is_reported_once()
    {
        var failing = new ThrowingDevice();
        var previous = DeviceRegistryHook.Replace(InputType.Sound, _ => failing);
        try
        {
            using var rig = new HarnessRig();
            rig.Devices.Require(InputType.Sound);
            rig.Show(1);

            var outcome = await rig.Model.TryPredict(saveResult: false);
            await Task.Delay(1500);

            Assert.IsTrue(outcome.Ok);
            Assert.GreaterOrEqual(failing.Polls, 1);
            lock (Debug.Warnings) Assert.AreEqual(1, Debug.Warnings.Count(w => w.Contains("sensor fault")));
        }
        finally
        {
            DeviceRegistryHook.Replace(InputType.Sound, previous);
        }
    }

    [Test]
    public void Face_only_use_never_starts_the_microphone()
    {
        using var rig = new HarnessRig();

        Assert.AreEqual(0, Microphone.StartCalls);
        Assert.AreEqual(1, rig.Devices.devices.Count);
    }

    [Test]
    public void The_sound_device_starts_the_microphone_only_when_asked_for()
    {
        using var rig = new HarnessRig();

        rig.Devices.Require(InputType.Sound);

        Assert.AreEqual(1, Microphone.StartCalls);
    }

    [Test]
    public void A_missing_face_component_is_reported_once_when_the_device_is_created()
    {
        var host = new GameObject("no-face");
        lock (Debug.Warnings) Debug.Warnings.Clear();

        var device = new AUDevice(host);

        lock (Debug.Warnings) Assert.AreEqual(1, Debug.Warnings.Count(w => w.Contains("OVRFaceExpressions")));
        Assert.IsFalse(device.Poll(new float[70]).valid);
    }

    private sealed class SilentDevice : Device
    {
        internal int Polls;

        internal SilentDevice() : base(null) { }

        internal override (InputType ty, int width) InputSpec => (InputType.Sound, 4);

        internal override (float interval, bool valid) Poll(float[] data)
        {
            Polls++;
            return (0.1f, false);
        }
    }

    [Test]
    public async Task A_device_added_without_a_schedule_is_polled_and_does_not_break_the_update_loop()
    {
        using var rig = new HarnessRig();
        var device = new SilentDevice();

        rig.Devices.devices[InputType.Sound] = device;
        await Task.Delay(300);

        Assert.GreaterOrEqual(device.Polls, 1);
        lock (Debug.Warnings) Assert.IsEmpty(Debug.Warnings.Where(w => w.Contains("polling")));
    }
}
#endif

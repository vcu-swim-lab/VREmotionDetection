using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VcuSwimLab.VREmotionDetection;

// Devices that hold hardware (the microphone) are stopped when their DeviceManager is disabled or destroyed, and a
// startup that is still waiting (for samples, or for the permission dialog on Android) does not carry on afterwards.
public class DeviceLifecycleTests
{
    private sealed class ThrowingRelease : Device
    {
        internal ThrowingRelease() : base(null) { }

        internal override (InputType ty, int width) InputSpec => (InputType.FaceAU, 4);

        internal override (float interval, bool valid) Poll(float[] data) => (0.1f, true);

        internal override bool Release() => throw new InvalidOperationException("cannot release");
    }

    [SetUp]
    public void SetUp()
    {
        Microphone.Reset();
        lock (Debug.Warnings) Debug.Warnings.Clear();
    }

    [TearDown]
    public void TearDown() => Microphone.Reset();

    private static DeviceManager NewManager()
    {
        var host = new GameObject("device-lifecycle");
        host.AddComponent<OVRFaceExpressions>();
        return host.AddComponent<DeviceManager>();
    }

    [Test]
    public void Releasing_the_sound_device_stops_the_microphone_once_and_ends_its_readings()
    {
        var device = new SoundDevice(new GameObject("mic"));
        Assert.IsTrue(Microphone.Recording);

        Assert.IsTrue(device.Release(), "a stopped microphone cannot be reused, so the device must be created again");
        Assert.IsFalse(Microphone.Recording);
        Assert.AreEqual(1, Microphone.EndCalls);

        Assert.IsTrue(device.Release());
        Assert.AreEqual(1, Microphone.EndCalls, "releasing twice is safe");
        Assert.IsFalse(device.Poll(new float[SoundDevice.SampleRateHz]).valid);
    }

    [Test]
    public async Task Releasing_while_the_microphone_is_still_starting_ends_the_wait_and_never_restarts_it()
    {
        Microphone.Position = 0;   // no samples yet: the device is waiting for the microphone to start
        var device = new SoundDevice(new GameObject("mic"));
        await Task.Delay(30);
        Assert.AreEqual(1, Microphone.StartCalls);
        Assert.Greater(Microphone.PositionCalls, 0, "the device is polling the microphone");

        device.Release();
        await Task.Delay(30);
        int polledAfterRelease = Microphone.PositionCalls;
        Microphone.Position = 1;   // would have let the wait finish normally
        await Task.Delay(100);

        Assert.AreEqual(polledAfterRelease, Microphone.PositionCalls, "the startup wait stopped when the device was released");
        Assert.AreEqual(1, Microphone.StartCalls, "the microphone was not started again");
        Assert.AreEqual(1, Microphone.EndCalls);
        Assert.IsFalse(Microphone.Recording);
    }

    [Test]
    public void Destroying_the_manager_releases_and_forgets_every_device()
    {
        var manager = NewManager();
        manager.Require(InputType.FaceAU);
        manager.Require(InputType.Sound);
        Assert.IsTrue(Microphone.Recording);

        HarnessRig.Call(manager, "OnDestroy");

        Assert.IsFalse(Microphone.Recording, "no recording is left active after teardown");
        Assert.AreEqual(1, Microphone.EndCalls);
        CollectionAssert.IsEmpty(manager.devices.Keys);
    }

    [Test]
    public void Disabling_the_manager_stops_the_microphone_but_keeps_the_face_device()
    {
        var manager = NewManager();
        var face = manager.Require(InputType.FaceAU);
        var firstSound = manager.Require(InputType.Sound);

        HarnessRig.Call(manager, "OnDisable");

        Assert.IsFalse(Microphone.Recording);
        Assert.AreSame(face, manager.devices[InputType.FaceAU], "the window source listens to this device, so it stays");
        Assert.IsFalse(manager.devices.ContainsKey(InputType.Sound));

        var secondSound = manager.Require(InputType.Sound);
        Assert.AreNotSame(firstSound, secondSound, "a stopped microphone device is replaced, not reused");
        Assert.IsTrue(Microphone.Recording);
        Assert.AreEqual(2, Microphone.StartCalls);
    }

    [Test]
    public void A_manager_with_only_the_face_device_has_no_microphone_to_stop()
    {
        var manager = NewManager();
        manager.Require(InputType.FaceAU);

        HarnessRig.Call(manager, "OnDisable");
        HarnessRig.Call(manager, "OnDestroy");

        Assert.AreEqual(0, Microphone.StartCalls);
        Assert.AreEqual(0, Microphone.EndCalls);
    }

    [Test]
    public void A_device_that_fails_to_release_does_not_stop_the_others_from_being_released()
    {
        var previous = DeviceRegistryHook.Replace(InputType.FaceAU, go => new ThrowingRelease());
        try
        {
            var manager = NewManager();
            manager.Require(InputType.FaceAU);   // released first, and throws
            manager.Require(InputType.Sound);

            Assert.DoesNotThrow(() => HarnessRig.Call(manager, "OnDestroy"));

            Assert.IsFalse(Microphone.Recording, "the microphone was still stopped");
            lock (Debug.Warnings) Assert.IsTrue(Debug.Warnings.Exists(w => w.Contains("releasing FaceAU failed")));
        }
        finally
        {
            DeviceRegistryHook.Replace(InputType.FaceAU, previous);
        }
    }
}

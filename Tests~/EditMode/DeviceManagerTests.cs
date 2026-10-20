using System;
using NUnit.Framework;
using UnityEngine;
using VcuSeCenter.VREmotionDetection;

public class DeviceManagerTests
{
    private sealed class CountingDevice : Device
    {
        internal CountingDevice(InputType type) : base(null)
        {
            Type = type;
        }

        internal InputType Type { get; }

        internal override (InputType ty, int width) InputSpec => (Type, 4);

        internal override (float interval, bool valid) Poll(float[] data) => (0.1f, true);
    }

    private GameObject host;
    private DeviceManager manager;
    private Func<GameObject, Device> faceFactory;
    private Func<GameObject, Device> soundFactory;
    private int faceCreated;
    private int soundCreated;

    [SetUp]
    public void SetUp()
    {
        host = new GameObject("device-manager-test");
        manager = host.AddComponent<DeviceManager>();
        faceCreated = 0;
        soundCreated = 0;
        faceFactory = DeviceRegistryHook.Replace(InputType.FaceAU, go => { faceCreated++; return new CountingDevice(InputType.FaceAU); });
        soundFactory = DeviceRegistryHook.Replace(InputType.Sound, go => { soundCreated++; return new CountingDevice(InputType.Sound); });
    }

    [TearDown]
    public void TearDown()
    {
        DeviceRegistryHook.Replace(InputType.FaceAU, faceFactory);
        DeviceRegistryHook.Replace(InputType.Sound, soundFactory);
        UnityEngine.Object.DestroyImmediate(host);
    }

    [Test]
    public void Requiring_the_face_device_never_creates_the_sound_device()
    {
        manager.Require(InputType.FaceAU);

        Assert.AreEqual(1, faceCreated);
        Assert.AreEqual(0, soundCreated);
    }

    [Test]
    public void A_device_is_created_once_and_shared()
    {
        var first = manager.Require(InputType.FaceAU);
        var second = manager.Require(InputType.FaceAU);

        Assert.AreSame(first, second);
        Assert.AreEqual(1, faceCreated);
    }

    [Test]
    public void A_device_can_be_created_again_after_it_is_disabled()
    {
        var first = manager.Require(InputType.FaceAU);
        // DeviceManager.Disable was an internal test seam, removed from the package (214fe73): it only dropped the entry.
        manager.devices.Remove(InputType.FaceAU);
        var second = manager.Require(InputType.FaceAU);

        Assert.AreNotSame(first, second);
        Assert.AreEqual(2, faceCreated);
    }

    [Test]
    public void An_input_type_that_is_not_a_device_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Require(InputType.Count));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Require((InputType)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Require((InputType)(-1)));
    }

    [Test]
    public void A_listener_receives_only_valid_readings_through_the_manager()
    {
        var device = (CountingDevice)manager.Require(InputType.FaceAU);
        int received = 0;
        device.OnPoll += (type, data) => received++;

        float interval = DeviceManager.PollAndDispatch(device);

        Assert.AreEqual(1, received);
        Assert.AreEqual(0.1f, interval, 1e-6f);
    }
}

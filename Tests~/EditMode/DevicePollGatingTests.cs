using System;
using NUnit.Framework;
using VcuSwimLab.VREmotionDetection;

public class DevicePollGatingTests
{
    private sealed class FakeDevice : Device
    {
        private readonly int width;

        internal bool ReportValid = true;
        internal float FrameValue;
        internal int PollCount;

        internal FakeDevice(int width) : base(null)
        {
            this.width = width;
        }

        internal override (InputType ty, int width) InputSpec => (InputType.FaceAU, width);

        internal override (float interval, bool valid) Poll(float[] data)
        {
            PollCount++;

            if (ReportValid)
            {
                for (int i = 0; i < data.Length; ++i)
                {
                    data[i] = FrameValue;
                }
            }

            return (0.1f, ReportValid);
        }
    }

    [Test]
    public void PollAndDispatch_ForwardsValidReadings()
    {
        var ring = new RingBuffer<float[]>(5, () => new float[2]);
        var device = new FakeDevice(2);
        ring.Listen(device);

        device.ReportValid = true;
        device.FrameValue = 1f;
        DeviceManager.PollAndDispatch(device);

        Assert.That(device.PollCount, Is.EqualTo(1));
        Assert.That(ring.Generation, Is.EqualTo(1));
    }

    [Test]
    public void PollAndDispatch_DropsInvalidReadings()
    {
        var ring = new RingBuffer<float[]>(5, () => new float[2]);
        var device = new FakeDevice(2);
        ring.Listen(device);

        device.ReportValid = true;
        device.FrameValue = 7f;
        DeviceManager.PollAndDispatch(device);

        device.ReportValid = false;
        for (int i = 0; i < 10; ++i)
        {
            DeviceManager.PollAndDispatch(device);
        }

        Assert.That(device.PollCount, Is.EqualTo(11), "The device should still be polled.");
        Assert.That(ring.Generation, Is.EqualTo(1), "Invalid readings must not reach the buffer.");

        CollectionAssert.AreEqual(new[] { 7f, 7f }, ring.buffer[ring.buffer.Length - 1]);
    }

    [Test]
    public void PollAndDispatch_NeverStoresAnAllZeroTrackingDropout()
    {
        var ring = new RingBuffer<float[]>(5, () => new float[2]);
        var device = new FakeDevice(2);
        ring.Listen(device);

        device.ReportValid = true;
        for (int frame = 1; frame <= 5; frame++)
        {
            device.FrameValue = frame;
            DeviceManager.PollAndDispatch(device);
        }

        device.ReportValid = false;
        for (int i = 0; i < 5; ++i)
        {
            DeviceManager.PollAndDispatch(device);
        }

        using var tensor = ring.ToTensor();
        CollectionAssert.AreEqual(
            new[] { 1f, 1f, 2f, 2f, 3f, 3f, 4f, 4f, 5f, 5f },
            tensor.DownloadToArray(),
            "The window should still hold the last five valid frames.");
    }

    [Test]
    public void PollAndDispatch_ReturnsTheDeviceInterval()
    {
        var device = new FakeDevice(2) { ReportValid = false };

        Assert.That(DeviceManager.PollAndDispatch(device), Is.EqualTo(0.1f).Within(1e-6f));
    }
}

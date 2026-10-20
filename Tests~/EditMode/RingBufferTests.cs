using System;
using NUnit.Framework;
using VcuSeCenter.VREmotionDetection;

public class RingBufferTests
{
    [Test]
    public void Push_KeepsLastFiveFramesInArrivalOrder()
    {
        var ring = new RingBuffer<float[]>(5, () => new float[2]);

        for (int frame = 1; frame <= 7; frame++)
        {
            ring.Push(new[] { (float)frame, frame + 0.25f });
        }

        using var tensor = ring.ToTensor();
        CollectionAssert.AreEqual(
            new[]
            {
                3f, 3.25f,
                4f, 4.25f,
                5f, 5.25f,
                6f, 6.25f,
                7f, 7.25f,
            },
            tensor.DownloadToArray());
        Assert.That(ring.Full, Is.True);
    }

    [Test]
    public void Push_RejectsFramesWithTheWrongLength()
    {
        var ring = new RingBuffer<float[]>(5, () => new float[2]);

        var exception = Assert.Throws<ArgumentException>(
            () => ring.Push(new float[3]));

        StringAssert.Contains("Expected frame length 2, received 3", exception.Message);
    }

    [Test]
    public void Push_KeepsEveryFrameBackedByADistinctArray()
    {
        var ring = new RingBuffer<float[]>(5, () => new float[2]);

        for (int frame = 1; frame <= 20; frame++)
        {
            ring.Push(new[] { (float)frame, frame + 0.25f });
        }

        for (int i = 0; i < ring.buffer.Length; ++i)
        {
            for (int j = i + 1; j < ring.buffer.Length; ++j)
            {
                Assert.That(
                    ReferenceEquals(ring.buffer[i], ring.buffer[j]), Is.False,
                    $"Slots {i} and {j} share one array; the window has collapsed.");
            }
        }
    }

    [Test]
    public void LegacyShift_CollapsesEverySlotOntoOneArray()
    {
        var buffer = new float[5][];
        for (int i = 0; i < buffer.Length; ++i)
        {
            buffer[i] = new float[2];
        }

        for (int frame = 1; frame <= 5; frame++)
        {
            for (int i = 0; i < buffer.Length - 1; ++i)
            {
                buffer[i] = buffer[i + 1];
            }

            var tmp = buffer[buffer.Length - 1];
            tmp[0] = frame;
            tmp[1] = frame + 0.25f;
        }

        for (int i = 1; i < buffer.Length; ++i)
        {
            Assert.That(ReferenceEquals(buffer[0], buffer[i]), Is.True,
                "Expected the legacy algorithm to alias every slot onto a single array.");
        }
    }

    [Test]
    public void Snapshot_IsIndependentOfLaterPushes()
    {
        var ring = new RingBuffer<float[]>(5, () => new float[2]);

        for (int frame = 1; frame <= 5; frame++)
        {
            ring.Push(new[] { (float)frame, frame + 0.25f });
        }

        var snapshot = ring.Snapshot();

        for (int frame = 6; frame <= 10; frame++)
        {
            ring.Push(new[] { (float)frame, frame + 0.25f });
        }

        CollectionAssert.AreEqual(new[] { 1f, 1.25f }, snapshot[0]);
        CollectionAssert.AreEqual(new[] { 5f, 5.25f }, snapshot[4]);
    }

    [Test]
    public void Snapshot_ToTensor_MatchesTheLiveWindow()
    {
        var ring = new RingBuffer<float[]>(5, () => new float[2]);

        for (int frame = 1; frame <= 7; frame++)
        {
            ring.Push(new[] { (float)frame, frame + 0.25f });
        }

        using var fromRing = ring.ToTensor();
        using var fromSnapshot = ring.Snapshot().ToTensor();

        CollectionAssert.AreEqual(fromRing.DownloadToArray(), fromSnapshot.DownloadToArray());
        Assert.That(fromSnapshot.shape, Is.EqualTo(fromRing.shape));
    }

    [Test]
    public void Generation_IsMonotonicAcrossClear()
    {
        var ring = new RingBuffer<float[]>(5, () => new float[2]);

        for (int frame = 1; frame <= 5; frame++)
        {
            ring.Push(new float[2]);
        }
        Assert.That(ring.Generation, Is.EqualTo(5));

        ring.Clear();
        Assert.That(ring.Full, Is.False, "Clear should reset the fill counter.");
        Assert.That(ring.Generation, Is.EqualTo(5), "Clear must not reset the generation.");

        ring.Push(new float[2]);
        Assert.That(ring.Generation, Is.EqualTo(6));
    }
}

using System;
using NUnit.Framework;

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

    // The original bug was reference aliasing, so assert reference identity directly
    // rather than inferring it from the values.
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

    // Documents the defect this class exists to prevent. It reimplements the original
    // four-line shift on its own array, so it keeps passing regardless of the production
    // code and cannot silently rot.
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
            // The original implementation: assigning slots without first saving the
            // array being displaced, so each assignment aliases two slots together.
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

        // Rotate the whole window out from under the snapshot.
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

    // `counter` is reset by Clear so a prediction can wait for a fresh window;
    // `generation` must not be, because it is what reveals dropped frames.
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

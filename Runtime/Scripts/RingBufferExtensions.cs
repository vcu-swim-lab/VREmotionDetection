using System;
using Unity.InferenceEngine;
using VcuSeCenter.Backends.SparseVr;

namespace VcuSeCenter.VREmotionDetection
{
    public static class RingBufferExtensions
    {
        public static Tensor<float> ToTensor(this RingBuffer<float[]> ring)
        {
            return WindowTensor.From(ring.buffer);
        }

        public static Tensor<float> ToTensor(this float[][] window)
        {
            return WindowTensor.From(window);
        }

        public static void Push(this RingBuffer<float[]> ring, float[] data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var tmp = ring.buffer[0];
            if (data.Length != tmp.Length)
            {
                throw new ArgumentException(
                    $"Expected frame length {tmp.Length}, received {data.Length}.",
                    nameof(data));
            }

            for (int i = 0; i < ring.buffer.Length - 1; ++i)
            {
                ring.buffer[i] = ring.buffer[i + 1];
            }
            ring.buffer[ring.buffer.Length - 1] = tmp;

            Array.Copy(data, tmp, data.Length);
            ring.counter++;
            ring.generation++;
        }

        public static float[][] Snapshot(this RingBuffer<float[]> ring)
        {
            var copy = new float[ring.buffer.Length][];
            for (int i = 0; i < ring.buffer.Length; ++i)
            {
                copy[i] = (float[])ring.buffer[i].Clone();
            }
            return copy;
        }

        public static void Listen(this RingBuffer<float[]> ring, Device device)
        {
            device.OnPoll += (type, data) => ring.Push(data);
        }
    }
}

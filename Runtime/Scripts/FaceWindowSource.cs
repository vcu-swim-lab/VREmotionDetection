using System;
using UnityEngine;

namespace VcuSwimLab.VREmotionDetection
{
    internal sealed class FaceWindowSource : IDisposable
    {
        private readonly RingBuffer<float[]> buffer;
        private readonly Device device;
        private readonly Device.PollDelegate handler;

        internal FaceWindowSource(Device device, int frames, int features)
        {
            this.device = device ?? throw new ArgumentNullException(nameof(device));
            buffer = new RingBuffer<float[]>(frames, () => new float[features]);
            handler = (type, data) => buffer.Push(data);
            device.OnPoll += handler;
        }

        internal long Generation => buffer.Generation;

        internal async Awaitable<float[][]> AwaitWindowAsync(float timeoutSeconds)
        {
            buffer.Clear();

            float deadline = Time.realtimeSinceStartup + timeoutSeconds;

            while (!buffer.Full)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    return null;
                }

                await Awaitable.NextFrameAsync();
            }

            return buffer.Snapshot();
        }

        public void Dispose()
        {
            device.OnPoll -= handler;
        }
    }
}

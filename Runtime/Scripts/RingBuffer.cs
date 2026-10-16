using System;

namespace VcuSwimLab.VREmotionDetection
{
    public class RingBuffer<T>
    {
        internal readonly T[] buffer;
        internal int counter = 0;
        internal long generation = 0;

        public RingBuffer(int size, Func<T> defaultVal)
        {
            buffer = new T[size];
            for (int i = 0; i < size; ++i)
            {
                buffer[i] = defaultVal();
            }
        }

        public bool Full => counter >= buffer.Length;

        public long Generation => generation;

        public int Capacity => buffer.Length;

        public void Clear()
        {
            counter = 0;
        }
    }
}

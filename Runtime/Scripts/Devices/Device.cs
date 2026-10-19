using UnityEngine;

namespace VcuSwimLab.VREmotionDetection
{
    public abstract class Device
    {
        internal Device(GameObject parent) { }

        internal abstract (InputType ty, int width) InputSpec { get; }

        internal abstract (float interval, bool valid) Poll(float[] data);

        internal delegate void PollDelegate(InputType type, float[] data);

        internal PollDelegate OnPoll;
    }
}

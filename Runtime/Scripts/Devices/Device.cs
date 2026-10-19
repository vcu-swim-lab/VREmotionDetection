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

        // Called when the owner shuts down. A device that holds hardware (the microphone) stops it here and returns true
        // when it cannot be used any more, so DeviceManager.Require makes a new one next time.
        internal virtual bool Release() => false;
    }
}

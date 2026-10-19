using System;
using System.Collections.Generic;
using UnityEngine;

namespace VcuSwimLab.VREmotionDetection
{
    public class DeviceManager : MonoBehaviour
    {
        private const float FailureBackoffSeconds = 1f;

        internal readonly Dictionary<InputType, Device> devices = new Dictionary<InputType, Device>();

        private readonly Dictionary<InputType, float> nextPollTime = new Dictionary<InputType, float>();
        private readonly List<InputType> pending = new List<InputType>();
        private readonly HashSet<InputType> reported = new HashSet<InputType>();

        void Update()
        {
            pending.Clear();
            pending.AddRange(devices.Keys);

            // Real time: the headset delivers faces regardless of Time.timeScale, and the model expects about 10 readings a second.
            float now = Time.unscaledTime;
            foreach (var ty in pending)
            {
                if (!devices.TryGetValue(ty, out var device) ||
                    (nextPollTime.TryGetValue(ty, out float due) && now < due))
                {
                    continue;
                }

                try
                {
                    nextPollTime[ty] = now + PollAndDispatch(device);
                    reported.Remove(ty);
                }
                catch (Exception e)
                {
                    nextPollTime[ty] = now + FailureBackoffSeconds;
                    if (reported.Add(ty))
                    {
                        Debug.LogWarning($"{nameof(DeviceManager)}: polling {ty} failed ({e.GetType().Name}: {e.Message}); it will be retried.");
                    }
                }
            }
        }

        void OnDisable()
        {
            ReleaseDevices(destroying: false);
        }

        void OnDestroy()
        {
            ReleaseDevices(destroying: true);
        }

        // Stops what the devices hold (the microphone). On destroy every device is dropped; on disable only those that cannot
        // be used any more, so Require makes a new one, while the face device stays because the window source listens to it.
        private void ReleaseDevices(bool destroying)
        {
            pending.Clear();
            pending.AddRange(devices.Keys);

            foreach (var ty in pending)
            {
                bool unusable = false;
                try
                {
                    unusable = devices[ty].Release();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{nameof(DeviceManager)}: releasing {ty} failed ({e.GetType().Name}: {e.Message}).");
                }

                if (destroying || unusable)
                {
                    devices.Remove(ty);
                    nextPollTime.Remove(ty);
                    reported.Remove(ty);
                }
            }
        }

        internal static float PollAndDispatch(Device device)
        {
            var buf = new float[device.InputSpec.width];
            var (interval, valid) = device.Poll(buf);

            if (valid)
            {
                device.OnPoll?.Invoke(device.InputSpec.ty, buf);
            }

            return interval;
        }

        public Device Require(InputType ty)
        {
            if (ty < 0 || ty >= InputType.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(ty), ty, "not a device input type");
            }

            if (devices.TryGetValue(ty, out var existing))
            {
                return existing;
            }

            if (!DeviceRegistry.IsAvailable(ty))
            {
                throw new InvalidOperationException($"no device is available for input type {ty}");
            }

            var device = DeviceRegistry.Create(ty, gameObject);
            devices[ty] = device;
            nextPollTime[ty] = Time.unscaledTime;
            return device;
        }
    }
}

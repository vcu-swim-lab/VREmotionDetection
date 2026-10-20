using System;
using UnityEngine;

#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace VcuSeCenter.VREmotionDetection
{
    public class SoundDevice : Device
    {
        public const int SampleRateHz = 16000;
        public const int ClipSeconds = 30;
        public const float PollSeconds = 1f;
        public const float StartupTimeoutSeconds = 5f;
        public const float PermissionTimeoutSeconds = 60f;

        private AudioClip sound;
        private volatile bool released;
        private bool recording;

        public SoundDevice(GameObject go)
            : base(go)
        {
            StartRecordingAsync();
        }

        internal override (InputType ty, int width) InputSpec => (InputType.Sound, SampleRateHz);

        internal override (float interval, bool valid) Poll(float[] data)
        {
            int pos = Microphone.GetPosition(null);
            if (sound == null || pos < data.Length)
            {
                return (PollSeconds, false);
            }

            sound.GetData(data, pos - data.Length);
            return (PollSeconds, true);
        }

        internal override bool Release()
        {
            released = true;
            if (recording)
            {
                recording = false;
                Microphone.End(null);
            }

            sound = null;
            return true;
        }

        private async void StartRecordingAsync()
        {
            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                {
                    Permission.RequestUserPermission(Permission.Microphone);

                    float permissionDeadline = Time.realtimeSinceStartup + PermissionTimeoutSeconds;
                    while (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
                    {
                        if (released)
                        {
                            return;
                        }

                        if (Time.realtimeSinceStartup >= permissionDeadline)
                        {
                            Debug.LogWarning($"{nameof(SoundDevice)}: the microphone permission was not granted.");
                            return;
                        }

                        await Awaitable.NextFrameAsync();
                    }
                }
#endif

                if (released)
                {
                    return;
                }

                sound = Microphone.Start(null, true, ClipSeconds, SampleRateHz);
                recording = true;

                float deadline = Time.realtimeSinceStartup + StartupTimeoutSeconds;
                while (!(Microphone.GetPosition(null) > 0))
                {
                    if (released)
                    {
                        return;
                    }

                    if (Time.realtimeSinceStartup >= deadline)
                    {
                        Debug.LogWarning($"{nameof(SoundDevice)}: the microphone did not start within {StartupTimeoutSeconds:F0}s.");
                        return;
                    }

                    await Awaitable.NextFrameAsync();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{nameof(SoundDevice)}: could not start the microphone ({e.GetType().Name}: {e.Message}).");
            }
        }
    }
}

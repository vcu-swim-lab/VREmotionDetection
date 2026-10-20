using System;
using System.Collections.Generic;
using UnityEngine;

namespace VcuSeCenter.VREmotionDetection
{
    internal static class DeviceRegistry
    {
        private static readonly Dictionary<InputType, Func<GameObject, Device>> Factories =
            new Dictionary<InputType, Func<GameObject, Device>>
            {
                [InputType.FaceAU] = go => new AUDevice(go),
                [InputType.Sound] = go => new SoundDevice(go),
            };

        internal static bool IsAvailable(InputType ty) => Factories.ContainsKey(ty);

        internal static Device Create(InputType ty, GameObject go) => Factories[ty](go);
    }
}

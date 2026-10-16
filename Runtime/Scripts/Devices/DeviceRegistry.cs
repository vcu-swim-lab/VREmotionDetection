using System;
using System.Collections.Generic;
using UnityEngine;

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

    internal static Func<GameObject, Device> Replace(InputType ty, Func<GameObject, Device> factory)
    {
        Factories.TryGetValue(ty, out var previous);
        Factories[ty] = factory;
        return previous;
    }
}

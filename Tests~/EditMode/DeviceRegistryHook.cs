using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VcuSeCenter.VREmotionDetection;

// DeviceRegistry.Replace was an internal test seam; it was removed from the shipped package (214fe73).
// The registry itself is unchanged - a private static dictionary of factories - so the tests reach it the
// same way here, without any change to the package.
internal static class DeviceRegistryHook
{
    private static readonly Dictionary<InputType, Func<GameObject, Device>> Factories =
        (Dictionary<InputType, Func<GameObject, Device>>)typeof(DeviceRegistry)
            .GetField("Factories", BindingFlags.NonPublic | BindingFlags.Static)
            .GetValue(null);

    internal static Func<GameObject, Device> Replace(InputType ty, Func<GameObject, Device> factory)
    {
        Factories.TryGetValue(ty, out var previous);
        Factories[ty] = factory;
        return previous;
    }
}

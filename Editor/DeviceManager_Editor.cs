
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;
using System.Linq;

[CustomEditor(typeof(DeviceManager))]
class DeviceManager_Editor : Editor
{
    private Type[] types;

    public void OnEnable()
    {
        types = GetDeviceTypes();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var comp = (DeviceManager)target;

        GUI.enabled = false;
        for (int i = 0; i < types.Length; ++i)
        {
            EditorGUILayout.Toggle(types[i].Name, comp.enabledInput[i]);
        }
        GUI.enabled = true;

        serializedObject.ApplyModifiedProperties();
    }

    private static Type[] GetDeviceTypes()
    {
        var baseType = typeof(Device);
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => a.GetTypes().Where(t => !t.IsAbstract && baseType.IsAssignableFrom(t)))
            .ToArray();
    }
}
#endif
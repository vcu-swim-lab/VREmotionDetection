using System;

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItem : Attribute
    {
        public MenuItem(string path) { }
    }

    public static class EditorUtility
    {
        public static string LastDialog;

        public static bool DisplayDialog(string title, string message, string ok)
        {
            LastDialog = message;
            return true;
        }

        public static void DisplayProgressBar(string title, string info, float progress) { }

        public static void ClearProgressBar() { }

        public static void RevealInFinder(string path) { }
    }

    public static class EditorApplication
    {
        public static string applicationContentsPath = string.Empty;
    }

    public static class EditorPrefs
    {
        public static string GetString(string key) => string.Empty;
    }

    public static class PlayerSettings
    {
        public static string GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget target) => "com.example.app";
    }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : class => null;
    }
}

namespace UnityEditor.Build
{
    public readonly struct NamedBuildTarget
    {
        public static NamedBuildTarget Android => default;
    }
}

using System.IO;
using System.Linq;

internal static class AdbLocator
{
    internal static string ExecutableName(bool windows) => windows ? "adb.exe" : "adb";

    internal static string[] Candidates(string unityContentsPath, string sdkRootPreference,
                                        string androidHome, string androidSdkRoot, string executable)
    {
        var platformTools = new[]
        {
            string.IsNullOrEmpty(unityContentsPath)
                ? null
                : Path.Combine(unityContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK"),
            sdkRootPreference,
            androidHome,
            androidSdkRoot,
        };

        return platformTools
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.Combine(root, "platform-tools", executable))
            .ToArray();
    }
}

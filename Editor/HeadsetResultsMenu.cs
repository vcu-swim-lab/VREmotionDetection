using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

using Debug = UnityEngine.Debug;

namespace VcuSeCenter.VREmotionDetection
{
    public static class HeadsetResultsMenu
    {
        private const string LocalFolderName = "HeadsetResults";

        [MenuItem("VR Emotion/Get Results From Headset")]
        public static void GetResultsFromHeadset()
        {
            string adb = FindAdb();
            if (adb == null)
            {
                Tell("Could not find adb.\n\nInstall Android Build Support for this Unity version " +
                     "(Unity Hub > Installs > Add modules), then try again.");
                return;
            }

            var (_, devicesText) = AdbRunner.Run(adb, "devices");
            var devices = AdbOutput.ParseDevices(devicesText);

            if (devices.Ready.Length == 0)
            {
                Tell(devices.Unauthorized
                    ? "The headset is connected but has not allowed this computer.\n\nPut the headset " +
                      "on and accept \"Allow USB debugging\", then try again."
                    : "No headset found.\n\nConnect the Quest with a USB cable, turn it on, and make " +
                      "sure developer mode is enabled.");
                return;
            }

            if (devices.Ready.Length > 1)
            {
                Tell("More than one device is connected:\n" + string.Join("\n", devices.Ready) +
                     "\n\nDisconnect the others so the right headset is used, then try again.");
                return;
            }

            string package = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
            string remote = AdbOutput.RemoteResultsFolder(package);
            string local = Path.Combine(Directory.GetParent(Application.dataPath).FullName, LocalFolderName);

            Directory.CreateDirectory(local);
            string gitignore = Path.Combine(local, ".gitignore");
            if (!File.Exists(gitignore))
            {
                File.WriteAllText(gitignore, "# Results describe real people: never commit them.\n*\n");
            }

            bool ok;
            string output;
            try
            {
                EditorUtility.DisplayProgressBar("VR Emotion", "Copying results from the headset...", 0.5f);
                (ok, output) = AdbRunner.Run(adb, $"-s {devices.Ready[0]} pull \"{remote}/.\" \"{local}\"", AdbRunner.PullTimeoutMs);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            if (!ok)
            {
                Tell(AdbOutput.IsMissingRemoteFolder(output)
                    ? $"There are no results on the headset yet for {package}.\n\nRun a session " +
                      "with \"Save Results\" on, then try again."
                    : $"Copying failed:\n\n{output.Trim()}");
                return;
            }

            int copied = AdbOutput.PulledFileCount(output) ?? Directory.GetFiles(local, "session_*.csv").Length;
            EditorUtility.RevealInFinder(gitignore);
            Tell($"Done. {copied} file(s) copied to:\n{local}\n\n" +
                 "Each file opens in Excel or Google Sheets: one row per prediction.");
        }

        private static void Tell(string message)
        {
            Debug.Log($"VR Emotion: {message}");
            EditorUtility.DisplayDialog("VR Emotion", message, "OK");
        }

        private static string FindAdb()
        {
            string executable = AdbLocator.ExecutableName(Application.platform == RuntimePlatform.WindowsEditor);
            var candidates = AdbLocator.Candidates(
                EditorApplication.applicationContentsPath,
                EditorPrefs.GetString("AndroidSdkRoot"),
                Environment.GetEnvironmentVariable("ANDROID_HOME"),
                Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT"),
                executable);

            string found = candidates.FirstOrDefault(File.Exists);
            if (found != null)
            {
                return found;
            }

            var (ok, _) = AdbRunner.Run(executable, "version");
            return ok ? executable : null;
        }
    }
}

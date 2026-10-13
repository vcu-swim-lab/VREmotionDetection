using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

using Debug = UnityEngine.Debug;

// Unity menu: VR Emotion > Get Results From Headset.
//
// Copies the results files the predictor saved on the connected headset into
// <project>/HeadsetResults and opens that folder. Uses adb from Unity's Android Build Support,
// so nothing else needs installing. The folder gets its own .gitignore, because the files
// describe real people and must never be committed.
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

        var (_, devicesText) = Run(adb, "devices");
        var lines = devicesText.Split('\n').Skip(1).Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        var ready = lines.Where(l => l.EndsWith("\tdevice")).Select(l => l.Split('\t')[0]).ToArray();

        if (ready.Length == 0)
        {
            Tell(lines.Any(l => l.EndsWith("unauthorized"))
                ? "The headset is connected but has not allowed this computer.\n\nPut the headset " +
                  "on and accept \"Allow USB debugging\", then try again."
                : "No headset found.\n\nConnect the Quest with a USB cable, turn it on, and make " +
                  "sure developer mode is enabled.");
            return;
        }

        string package = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        string remote = $"/sdcard/Android/data/{package}/files/VREmotionDetection/results";
        string local = Path.Combine(Directory.GetParent(Application.dataPath).FullName, LocalFolderName);

        Directory.CreateDirectory(local);
        string gitignore = Path.Combine(local, ".gitignore");
        if (!File.Exists(gitignore))
        {
            File.WriteAllText(gitignore, "# Results describe real people: never commit them.\n*\n");
        }

        try
        {
            EditorUtility.DisplayProgressBar("VR Emotion", "Copying results from the headset...", 0.5f);
            var (ok, output) = Run(adb, $"-s {ready[0]} pull \"{remote}/.\" \"{local}\"", 300000);

            if (!ok)
            {
                Tell(output.Contains("does not exist") || output.Contains("No such file")
                    ? $"There are no results on the headset yet for {package}.\n\nRun a session " +
                      "with \"Save Results\" on, then try again."
                    : $"Copying failed:\n\n{output.Trim()}");
                return;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        int sessions = Directory.GetFiles(local, "session_*.csv").Length;
        EditorUtility.RevealInFinder(Path.Combine(local, ".gitignore"));
        Tell($"Done. {sessions} session file(s) are in:\n{local}\n\n" +
             "Each file opens in Excel or Google Sheets: one row per prediction.");
    }

    private static void Tell(string message)
    {
        Debug.Log($"VR Emotion: {message}");
        EditorUtility.DisplayDialog("VR Emotion", message, "OK");
    }

    // Unity's own copy first, then the Android SDK Unity is set to, then the usual places.
    private static string FindAdb()
    {
        string exe = Application.platform == RuntimePlatform.WindowsEditor ? "adb.exe" : "adb";
        var candidates = new[]
        {
            Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer",
                         "SDK", "platform-tools", exe),
            Path.Combine(EditorPrefs.GetString("AndroidSdkRoot"), "platform-tools", exe),
            Path.Combine(Environment.GetEnvironmentVariable("ANDROID_HOME") ?? "", "platform-tools", exe),
            Path.Combine(Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT") ?? "", "platform-tools", exe),
        };

        string found = candidates.FirstOrDefault(File.Exists);
        if (found != null) return found;

        // Last resort: adb on PATH.
        var (ok, _) = Run(exe, "version");
        return ok ? exe : null;
    }

    private static (bool ok, string output) Run(string file, string arguments, int timeoutMs = 20000)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(file, arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            });

            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                process.Kill();
                return (false, "adb did not answer in time.");
            }

            return (process.ExitCode == 0, stdout.Result + stderr.Result);
        }
        catch (Exception e)
        {
            return (false, e.Message);
        }
    }
}

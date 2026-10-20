using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VcuSeCenter.VREmotionDetection;

// Small helpers for the tests that must prove something does NOT hang or does NOT keep going.
internal static class TestHelpers
{
    // Starts the work on the thread pool so a call that never returns cannot block the test.
    public static Task<T> Start<T>(Func<Awaitable<T>> work) => Task.Run(async () => await work());

    // Fails fast (instead of hanging the run) if the task is not finished in time.
    public static async Task<T> Within<T>(Task<T> task, int milliseconds, string because)
    {
        var finished = await Task.WhenAny(task, Task.Delay(milliseconds));
        Assert.AreSame(task, finished, because);
        return await task;
    }

    public static async Task WaitUntil(Func<bool> condition, string because, int milliseconds = 3000)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.Less(clock.ElapsedMilliseconds, milliseconds, because);
            await Task.Delay(2);
        }
    }

    // The results recorder keeps its file open for writing during a session, and Windows refuses a reader (such as
    // File.ReadAllLines) that does not share write access with it.
    public static string[] ReadAllLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        for (string line; (line = reader.ReadLine()) != null;)
        {
            lines.Add(line);
        }

        return lines.ToArray();
    }

    // The prompt of a calibration: shows the face and the model answer that belong to the emotion, tells the test which
    // prompt this is (1 = first), then gives the headset a moment to deliver frames of that face.
    public static Func<Emotion, Awaitable> Prompt(HarnessRig rig, Action<int> atPrompt)
    {
        int prompted = 0;
        return async emotion =>
        {
            rig.Show((int)emotion);
            Unity.InferenceEngine.Worker.Canned = HarnessRig.Scores((int)emotion);
            atPrompt(++prompted);
            await Awaitable.WaitForSecondsAsync(0.03f);
        };
    }
}

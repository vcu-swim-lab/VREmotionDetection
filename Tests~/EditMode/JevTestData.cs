using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using VcuSwimLab.Backends.Jev;
using VcuSwimLab.Calibration;
using VcuSwimLab.VREmotionDetection;

internal static class JevTestData
{
    public const int Block = 9;

    public static readonly string[] Classes = Enum.GetNames(typeof(Emotion));

    public static readonly string[] AuNames = Enumerable.Range(0, CalibrationMath.FaceActionUnitCount)
        .Select(i => $"AU{i}")
        .ToArray();

    public static float[][] Window(int cls)
    {
        var window = new float[5][];
        for (int f = 0; f < window.Length; ++f)
        {
            window[f] = new float[70];
            for (int a = 0; a < 70; ++a) window[f][a] = 0.02f;
            for (int a = cls * Block; a < cls * Block + Block; ++a) window[f][a] = 0.8f;
        }

        return window;
    }

    public static CalibrationProfile Profile()
    {
        var centroids = new double[Classes.Length][];
        for (int c = 0; c < centroids.Length; ++c)
        {
            centroids[c] = new double[CalibrationMath.FaceActionUnitCount];
            for (int a = 0; a < centroids[c].Length; ++a) centroids[c][a] = 0.02;
            for (int a = c * Block; a < c * Block + Block; ++a) centroids[c][a] = 0.8;
        }

        return new CalibrationProfile("test", Classes, centroids, centroids);
    }

    public static JObject Build(float[][] window, CalibrationProfile profile)
    {
        return JObject.Parse(JevRequestBuilder.Build("jev-test", window, AuNames, Classes, profile));
    }

    public static string Answers(params double[] values)
    {
        var answers = new JObject();
        for (int e = 0; e < values.Length; ++e)
        {
            answers[Classes[e]] = new JObject { ["type"] = "noul", ["noul"] = values[e] };
        }

        return new JObject { ["answers"] = answers }.ToString();
    }

    public static T Complete<T>(Awaitable<T> pending)
    {
        var awaiter = pending.GetAwaiter();
        NUnit.Framework.Assert.IsTrue(awaiter.IsCompleted, "the call should finish without waiting for a frame");
        return awaiter.GetResult();
    }
}

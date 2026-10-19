using System;

using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEditor;
using VcuSwimLab.Backends.SparseVr;
using VcuSwimLab.Calibration;

using Random = System.Random;
using VcuSwimLab.VREmotionDetection;

public class ModelCalibrationEndToEndTests
{
    private const string ModelPath = NewFaceAuModel.DefaultModelPath;

    private static readonly string[] Names = Enum.GetNames(typeof(Emotion));

    private static float[][] Window(int cls, Random rng)
    {
        var window = new float[NewFaceAuModel.WindowFrames][];
        for (int f = 0; f < window.Length; ++f)
        {
            window[f] = new float[NewFaceAuModel.FeaturesPerFrame];
            for (int a = 0; a < window[f].Length; ++a)
            {
                bool active = a < CalibrationMath.FaceActionUnitCount && a % Names.Length == cls;
                window[f][a] = (float)((active ? 0.6 : 0.05) + rng.NextDouble() * 0.1);
            }
        }

        return window;
    }

    private static float[] Scores(Worker worker, float[][] window)
    {
        using var input = window.ToTensor();
        worker.Schedule(input);
        using var output = worker.PeekOutput().ReadbackAndClone() as Tensor<float>;
        return output.DownloadToArray();
    }

    [Test]
    public void The_shipped_model_runs_uncalibrated_then_calibrated()
    {
        var asset = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelPath);
        Assert.IsNotNull(asset, $"the shipped model is missing at {ModelPath}");

        using var worker = new Worker(OnnxModelLoader.Prepare(asset, appendSoftmax: false), BackendType.CPU);
        var rng = new Random(20261012);

        var calibrator = new MovementCalibrator(Names);
        var first = Window(0, rng);
        var scores = Scores(worker, first);
        Assert.AreEqual(Names.Length, scores.Length);
        float sum = 0f;
        foreach (float s in scores) sum += s;
        Assert.That(sum, Is.EqualTo(1f).Within(1e-4f));
        Assert.IsNull(calibrator.Classify(scores, first), "no calibrated answer before calibration");

        for (int cls = 0; cls < Names.Length; ++cls)
        {
            calibrator.BeginEmotion(cls);
            while (!calibrator.IsEmotionComplete(cls))
            {
                var window = Window(cls, rng);
                calibrator.Observe(Scores(worker, window), window);
            }
        }

        Assert.IsTrue(calibrator.Finish(), calibrator.Error);

        for (int cls = 0; cls < Names.Length; ++cls)
        {
            var window = Window(cls, rng);
            var decision = calibrator.Classify(Scores(worker, window), window);
            Assert.IsTrue(decision.HasValue);
            Assert.AreEqual(cls, decision.Value.ClassIndex, $"{Names[cls]} after calibration");
        }
    }
}

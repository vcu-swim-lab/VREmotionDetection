using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
#if !LEGACY_GLOBAL_NAMESPACE
using VcuSeCenter.VREmotionDetection;
#endif

public class GoldenScenario
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private volatile float[] face = Face(0);
    private volatile bool valid = true;

    private static float[] Face(int cls)
    {
        var frame = new float[70];
        for (int a = 0; a < 63; ++a) frame[a] = 0.02f;
        for (int a = cls * 9; a < cls * 9 + 9; ++a) frame[a] = 0.8f;
        return frame;
    }

    private static float[] Scores(int peak)
    {
        var scores = Enumerable.Repeat(0.04f, 7).ToArray();
        scores[peak] = 0.76f;
        return scores;
    }

    // A copy of TestHelpers.ReadAllLinesShared: Golden.csproj compiles only this file and HarnessRig.cs, so that it also builds
    // against older versions. The results recorder keeps its file open for writing, and Windows refuses a reader that does not
    // share write access with it.
    private static string[] ReadAllLinesShared(string path)
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

    private static void Field(object target, string name, object value) =>
        target.GetType().GetField(name, Instance).SetValue(target, value);

    private static void Invoke(object target, string name) =>
        target.GetType().GetMethod(name, Instance)?.Invoke(target, null);

    private static double Round(double value) => Math.Round(value, 6);

    private static object Describe(FacePrediction? prediction) => prediction == null
        ? null
        : new
        {
            emotion = prediction.Value.Emotion.ToString(),
            confidence = Round(prediction.Value.Confidence),
            scores = prediction.Value.Scores.Select(s => Round(s)).ToArray(),
            window = prediction.Value.Window.Length + "x" + prediction.Value.Window[0].Length,
        };

    private async Task Settle() => await Task.Delay(40);

    [Test]
    public async Task Full_flow_transcript()
    {
        Application.persistentDataPath = Path.Combine(Path.GetTempPath(), "golden-" + Guid.NewGuid().ToString("N"));
        Time.Override = 0f;

        var go = new GameObject();
        var ovr = go.AddComponent<OVRFaceExpressions>();
        var devices = go.AddComponent<DeviceManager>();
        var model = go.AddComponent<NewFaceAuModel>();
        Field(model, "faceModel", new ModelAsset { name = "golden-model" });
        Field(model, "useCalibration", true);
        Invoke(devices, "Awake");
        model.Start();

        float clock = 0f;
        var stop = new CancellationTokenSource();
        var feeder = Task.Run(async () =>
        {
            while (!stop.IsCancellationRequested)
            {
                clock += 0.1f;
                Time.Override = clock;
                ovr.ValidExpressions = valid;
                ovr.Values = (float[])face.Clone();
                Invoke(devices, "Update");
                await Task.Delay(1);
            }
        });

        var transcript = new List<object>();
        void Add(string step, object value) => transcript.Add(new { step, value });

        for (int e = 0; e < 7; ++e)
        {
            face = Face(e);
            Worker.Canned = Scores(e);
            await Settle();
            Add("uncalibrated " + (Emotion)e, Describe(await model.PredictDetailed()));
        }

        face = Face(3);
        Worker.Canned = Scores(3);
        await Settle();
        var (predictedEmotion, predictedConfidence) = await model.Predict();
        Add("predict", new { emotion = predictedEmotion.ToString(), confidence = Round(predictedConfidence) });

        valid = false;
        await Settle();
        Add("no tracking", Describe(await model.PredictDetailed(0.3f)));
        var (noTrackingEmotion, noTrackingConfidence) = await model.Predict();
        Add("predict without tracking", new { emotion = noTrackingEmotion.ToString(), confidence = Round(noTrackingConfidence) });
        valid = true;
        await Settle();

        model.StartCalibration();
        Add("calibration started", new { model.IsCalibrated, model.CalibrationError });
        for (int e = 0; e < 7; ++e)
        {
            face = Face(e);
            Worker.Canned = Scores(e);
            await Settle();
            Add("calibrate " + (Emotion)e, await model.CalibrateEmotion((Emotion)e, 0.001f));
        }

        Add("counts", model.CalibrationSampleCounts());
        Add("finish", new { finished = model.FinishCalibration(), model.IsCalibrated, model.CalibrationError });

        for (int e = 0; e < 7; ++e)
        {
            face = Face((e + 2) % 7);
            Worker.Canned = Scores(e);
            await Settle();
            var (emotion, confidence) = await model.Predict();
            var prediction = await model.PredictDetailed(saveResult: false);
            var decision = model.CalibratedDecision(prediction.Value);
            Add("calibrated " + e, new
            {
                emotion = emotion.ToString(),
                confidence = Round(confidence),
                modelEmotion = prediction.Value.Emotion.ToString(),
                decision = decision == null ? null : new
                {
                    decision.Value.ClassIndex,
                    winner = Round(decision.Value.WinnerSimilarity),
                    runnerUp = Round(decision.Value.RunnerUpSimilarity),
                    margin = Round(decision.Value.Margin),
                },
            });
        }

        Add("has before save", model.HasSavedCalibration("golden"));
        Add("save", model.SaveCalibration("golden"));
        string json = File.ReadAllText(NewFaceAuModel.CalibrationPath("golden"));
        json = Regex.Replace(json, "\"savedUtc\":\"[^\"]*\"", "\"savedUtc\":\"X\"");
        Add("saved file", json);
        Add("has after save", model.HasSavedCalibration("golden"));

        model.ClearCalibration();
        Add("cleared", model.IsCalibrated);
        Add("load", model.LoadCalibration("golden"));
        Add("loaded", new { model.IsCalibrated, model.CalibrationError });

        string tampered = File.ReadAllText(NewFaceAuModel.CalibrationPath("golden")).Replace("golden-model", "other-model");
        File.WriteAllText(NewFaceAuModel.CalibrationPath("tampered"), tampered);
        Add("load other model", model.LoadCalibration("tampered"));
        Add("load other model error", model.CalibrationError);
        Add("load missing", model.LoadCalibration("nobody"));
        Add("load missing error", model.CalibrationError);
        File.WriteAllText(NewFaceAuModel.CalibrationPath("broken"), "{ not json");
        Add("load broken", model.LoadCalibration("broken"));
        Add("load broken starts with", model.CalibrationError.Split(':')[0]);

        model.DeleteSavedCalibration("golden");
        Add("has after delete", model.HasSavedCalibration("golden"));

        model.UseCalibration = false;
        model.StartCalibration();
        Add("off error", model.CalibrationError);
        Add("off calibrate", await model.CalibrateEmotion(Emotion.Anger, 0.001f));
        Add("off decision", model.CalibratedDecision((await model.PredictDetailed(saveResult: false)).Value));
        Add("off load", model.LoadCalibration("golden"));

        stop.Cancel();
        await feeder;

        string[] rows = ReadAllLinesShared(model.ResultsFilePath);
        Add("csv", rows.Select(r => string.Join(",", r.Split(',').Skip(2))).ToArray());
        Add("summary", Regex.Replace(model.GetResultsSummary(), @"over \d+\.\d min", "over X min"));

        string text = JsonConvert.SerializeObject(transcript, Formatting.Indented);
        string output = Environment.GetEnvironmentVariable("GOLDEN_OUT");
        if (!string.IsNullOrEmpty(output))
        {
            File.WriteAllText(output, text);
        }

        Assert.Greater(transcript.Count, 40);
        Assert.IsEmpty(Debug.Errors);
    }
}

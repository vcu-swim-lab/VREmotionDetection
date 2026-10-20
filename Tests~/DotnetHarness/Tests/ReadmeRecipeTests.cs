#if NEW_API
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSeCenter.Backends;
using VcuSeCenter.Backends.Jev;
using VcuSeCenter.VREmotionDetection;

public class ReadmeRecipeTests
{
    private static readonly string[] Names = Enum.GetNames(typeof(Emotion));

    private sealed class ScriptedTransport : IHttpTransport
    {
        private int calls;

        public Func<(long status, string body)> Handler;

        public int Calls => calls;

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds)
        {
            Interlocked.Increment(ref calls);
            var (status, body) = Handler();
            return new HttpReply(status, body);
        }
    }

    private static string Answers(int peak)
    {
        var answers = new JObject();
        for (int e = 0; e < 7; ++e)
        {
            answers[Names[e]] = new JObject { ["type"] = "noul", ["noul"] = e == peak ? 0.9 : 0.0 };
        }

        return new JObject { ["answers"] = answers }.ToString();
    }

    [Test]
    public async Task Recipe_2_sparse_vr_with_calibration_in_the_documented_order()
    {
        string storage = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "recipe2-" + Guid.NewGuid().ToString("N"));

        using (var firstRun = new HarnessRig(useCalibration: false, storageFolder: storage))
        {
            var predictor = firstRun.Model;
            predictor.UseSparseVr();
            predictor.UseCalibration = true;

            Assert.IsFalse(predictor.LoadCalibration("player1"), "nothing is saved on the first run");
            Assert.IsFalse(predictor.IsCalibrated);

            var prompted = new List<Emotion>();
            bool calibrated = await predictor.CalibrateAll(async emotion =>
            {
                prompted.Add(emotion);
                firstRun.Show((int)emotion);
                Worker.Canned = HarnessRig.Scores((int)emotion);
                await Awaitable.WaitForSecondsAsync(0.05f);
            }, secondsBetweenSamples: 0.001f);

            Assert.IsTrue(calibrated, predictor.CalibrationError);
            Assert.AreEqual(7, prompted.Count);
            Assert.IsTrue(predictor.SaveCalibration("player1"));
            Assert.IsTrue(predictor.HasSavedCalibration("player1"));

            firstRun.Show(2);
            Worker.Canned = HarnessRig.Scores(0);
            await firstRun.Settle();
            var (emotion, confidence) = await predictor.Predict();

            Assert.IsTrue(predictor.IsCalibrated);
            Assert.AreEqual(Emotion.Fear, emotion, "the face decides once calibrated");
            Assert.AreEqual(0.04f, confidence, 1e-6f, "the confidence stays the model's own probability");
        }

        using (var nextRun = new HarnessRig(useCalibration: false, storageFolder: storage))
        {
            var predictor = nextRun.Model;
            predictor.UseSparseVr();
            predictor.UseCalibration = true;
            int runsBefore = Worker.Runs;

            Assert.IsTrue(predictor.LoadCalibration("player1"), predictor.CalibrationError);
            Assert.IsTrue(predictor.IsCalibrated);
            Assert.AreEqual(runsBefore, Worker.Runs, "loading needs no calibration run");

            nextRun.Show(5);
            Worker.Canned = HarnessRig.Scores(0);
            await nextRun.Settle();
            var (emotion, _) = await predictor.Predict();

            Assert.AreEqual(Emotion.Sadness, emotion);
        }
    }

    [Test]
    public async Task Recipe_4_jev_with_calibration_in_the_documented_order()
    {
        string storage = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "recipe4-" + Guid.NewGuid().ToString("N"));
        int truth = 0;
        var transport = new ScriptedTransport { Handler = () => (200, Answers(truth)) };

        JevBackend Backend() => new JevBackend(new JevOptions("recipe-key") { MaxRetries = 0 }, FaceAuNames.Movement, transport);

        using (var firstRun = new HarnessRig(useCalibration: false, storageFolder: storage))
        {
            var predictor = firstRun.Model;
            predictor.UseBackend(Backend());
            predictor.UseCalibration = true;

            Assert.IsFalse(predictor.LoadCalibration("player1"), "nothing is saved on the first run");

            bool calibrated = await predictor.CalibrateAll(async emotion =>
            {
                truth = (int)emotion;
                firstRun.Show((int)emotion);
                await Awaitable.WaitForSecondsAsync(0.05f);
            }, secondsBetweenSamples: 0.001f);

            Assert.IsTrue(calibrated, predictor.CalibrationError);
            Assert.GreaterOrEqual(transport.Calls, 70, "every sample is one request");
            Assert.IsTrue(predictor.SaveCalibration("player1"));
            StringAssert.Contains("jev:jev-1.13.0:p1", System.IO.File.ReadAllText(NewFaceAuModel.CalibrationPath("player1")));

            truth = 4;
            firstRun.Show(4);
            await firstRun.Settle();
            var (emotion, confidence) = await predictor.Predict();

            Assert.IsTrue(predictor.IsCalibrated);
            Assert.IsNull(predictor.LastError);
            Assert.AreEqual(Emotion.Neutral, emotion);
            Assert.AreEqual(0.9f, confidence, 1e-6f, "the confidence is Jev's own answer");
        }

        int callsAfterFirstRun = transport.Calls;

        using (var nextRun = new HarnessRig(useCalibration: false, storageFolder: storage))
        {
            var predictor = nextRun.Model;
            predictor.UseBackend(Backend());
            predictor.UseCalibration = true;

            Assert.IsTrue(predictor.LoadCalibration("player1"), predictor.CalibrationError);
            Assert.AreEqual(callsAfterFirstRun, transport.Calls, "loading makes no requests");

            truth = 1;
            nextRun.Show(1);
            await nextRun.Settle();
            var (emotion, _) = await predictor.Predict();

            Assert.AreEqual(Emotion.Disgust, emotion);
            Assert.IsTrue(predictor.IsCalibrated);
        }
    }

    [Test]
    public async Task Recipe_4_a_sparse_vr_calibration_is_refused_by_jev_and_the_player_is_asked_to_calibrate_again()
    {
        string storage = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "recipe4b-" + Guid.NewGuid().ToString("N"));

        using (var sparse = new HarnessRig(storageFolder: storage))
        {
            Assert.IsTrue(await sparse.Model.CalibrateAll(async emotion =>
            {
                sparse.Show((int)emotion);
                Worker.Canned = HarnessRig.Scores((int)emotion);
                await Awaitable.WaitForSecondsAsync(0.05f);
            }, secondsBetweenSamples: 0.001f));
            Assert.IsTrue(sparse.Model.SaveCalibration("player1"));
        }

        using (var jev = new HarnessRig(storageFolder: storage))
        {
            var transport = new ScriptedTransport { Handler = () => (200, Answers(0)) };
            jev.Model.UseBackend(new JevBackend(new JevOptions("recipe-key"), FaceAuNames.Movement, transport));

            Assert.IsFalse(jev.Model.LoadCalibration("player1"));
            StringAssert.EndsWith("calibrate again", jev.Model.CalibrationError);
        }
    }

    [Test]
    public async Task Recipe_1_and_3_without_calibration_return_the_documented_types()
    {
        using var rig = new HarnessRig(useCalibration: false);
        rig.Show(3);
        Worker.Canned = HarnessRig.Scores(3);
        await rig.Settle();

        PredictionOutcome outcome = await rig.Model.TryPredict();
        (Emotion emotion, float confidence) = await rig.Model.Predict();
        FacePrediction? detailed = await rig.Model.PredictDetailed();

        Assert.IsTrue(outcome.Ok);
        Assert.AreEqual(Emotion.Happiness, emotion);
        Assert.AreEqual(0.76f, confidence, 1e-6f);
        Assert.AreEqual(7, detailed.Value.Scores.Length);
        Assert.AreEqual(5, detailed.Value.Window.Length);
        Assert.AreEqual(70, detailed.Value.Window[0].Length);
        Assert.IsFalse(rig.Model.IsCalibrated);
        Assert.IsFalse(rig.Model.LoadCalibration("anyone"), "calibration calls do nothing while it is off");
    }
}
#endif

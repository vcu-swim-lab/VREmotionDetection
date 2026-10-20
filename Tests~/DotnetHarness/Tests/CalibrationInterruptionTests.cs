using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSeCenter.Backends;
using VcuSeCenter.VREmotionDetection;

// Since 3.0.0 a calibration that is stopped by ClearCalibration, a backend switch or a disable ends with an
// explanatory CalibrationError wherever it was (not only inside a prediction), and an old run never writes its
// error onto a newer calibration.
public class CalibrationInterruptionTests
{
    private sealed class QuietBackend : IEmotionBackend
    {
        public QuietBackend(string id, Func<BackendResult> answer = null)
        {
            Id = id;
            this.answer = answer ?? (() => BackendResult.Success(HarnessRig.Scores(0)));
        }

        private readonly Func<BackendResult> answer;

        public string Id { get; }

        public async Awaitable<BackendResult> ScoreAsync(BackendRequest request) => answer();

        public float[] PrepareForCalibration(float[] scores) => scores;

        public void Dispose() { }
    }

    [TearDown]
    public void TearDown()
    {
        Worker.ReadbackDelayMs = 0;
        Awaitable.FrameDelayMs = 1;
    }

    private static Func<Emotion, Awaitable> Prompt(HarnessRig rig, Action<int> atPrompt)
    {
        int prompted = 0;
        return async emotion =>
        {
            rig.Show((int)emotion);
            Worker.Canned = HarnessRig.Scores((int)emotion);
            atPrompt(++prompted);
            await Awaitable.WaitForSecondsAsync(0.03f);
        };
    }

    [Test]
    public async Task Clearing_the_calibration_during_CalibrateAll_stops_it_and_says_why()
    {
        using var rig = new HarnessRig();

        bool calibrated = await rig.Model.CalibrateAll(Prompt(rig, n => { if (n == 2) rig.Model.ClearCalibration(); }),
                                                       secondsBetweenSamples: 0.001f);

        Assert.IsFalse(calibrated);
        Assert.IsFalse(rig.Model.IsCalibrated);
        StringAssert.Contains("interrupted", rig.Model.CalibrationError);
    }

    [Test]
    public async Task Disabling_the_predictor_during_CalibrateAll_stops_it_and_says_why()
    {
        using var rig = new HarnessRig();

        bool calibrated = await rig.Model.CalibrateAll(Prompt(rig, n => { if (n == 3) HarnessRig.Call(rig.Model, "OnDisable"); }),
                                                       secondsBetweenSamples: 0.001f);

        Assert.IsFalse(calibrated);
        Assert.IsFalse(rig.Model.IsCalibrated);
        StringAssert.Contains("interrupted", rig.Model.CalibrationError);
        StringAssert.Contains("disabled", rig.Model.CalibrationError);
    }

    [Test]
    public async Task Switching_the_backend_during_CalibrateAll_stops_it_and_says_why()
    {
        using var rig = new HarnessRig();

        bool calibrated = await rig.Model.CalibrateAll(Prompt(rig, n => { if (n == 2) rig.Model.UseBackend(new QuietBackend("other")); }),
                                                       secondsBetweenSamples: 0.001f);

        Assert.IsFalse(calibrated);
        Assert.IsFalse(rig.Model.IsCalibrated);
        StringAssert.Contains("interrupted", rig.Model.CalibrationError);
        StringAssert.Contains("backend changed", rig.Model.CalibrationError);
        Assert.IsNull(rig.Model.LastError, "stopping a calibration is not a prediction failure");
    }

    [Test]
    public async Task An_old_run_does_not_write_its_error_onto_a_newer_calibration()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        Worker.ReadbackDelayMs = 300;
        rig.Model.StartCalibration();

        var oldRun = rig.Model.CalibrateEmotion(Emotion.Anger, 0.001f);
        await Task.Delay(100);
        rig.Model.StartCalibration();
        bool finished = await oldRun;

        Assert.IsFalse(finished);
        // UsageGuide: CalibrationError is "empty or null if nothing failed".
        Assert.IsTrue(string.IsNullOrEmpty(rig.Model.CalibrationError), "the newer calibration has no error of its own");
    }

    [Test]
    public async Task A_calibration_that_cannot_collect_samples_says_how_many_attempts_it_made_and_where_to_look()
    {
        using var rig = new HarnessRig();
        rig.Model.UseBackend(new QuietBackend("failing", () =>
            BackendResult.Failure(PredictionErrorCode.Unreachable, "no network", "failing")));
        rig.Model.StartCalibration();

        bool calibrated = await rig.Model.CalibrateEmotion(Emotion.Anger, 0f, maxAttempts: 3);

        Assert.IsFalse(calibrated);
        StringAssert.Contains("could not collect enough samples for Anger in 3 attempts", rig.Model.CalibrationError);
        StringAssert.Contains("LastError", rig.Model.CalibrationError);
        Assert.AreEqual(PredictionErrorCode.Unreachable, rig.Model.LastError.Value.Code);
    }

    [Test]
    public void Calibration_arguments_are_checked()
    {
        using var rig = new HarnessRig();
        rig.Model.StartCalibration();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await rig.Model.CalibrateEmotion(Emotion.Anger, -1f));
        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await rig.Model.CalibrateEmotion(Emotion.Anger, 0f, maxAttempts: 0));
    }

    [Test]
    public async Task A_calibration_started_before_the_predictor_starts_waits_for_it_and_completes()
    {
        Awaitable.FrameDelayMs = 150;
        using var rig = new HarnessRig(start: false);
        rig.Show(0);

        rig.Model.StartCalibration();
        var running = rig.Model.CalibrateEmotion(Emotion.Anger, 0.001f);
        rig.Model.Start();
        bool calibrated = await running;

        Assert.IsTrue(calibrated, rig.Model.CalibrationError);
        Assert.IsTrue(string.IsNullOrEmpty(rig.Model.CalibrationError));
    }

    [Test]
    public async Task Choosing_the_same_backend_again_does_not_interrupt_a_calibration()
    {
        using var rig = new HarnessRig();
        var active = rig.Model.ActiveBackendId;

        bool calibrated = await rig.Model.CalibrateAll(
            Prompt(rig, n => { if (n == 2) rig.Model.UseLocalModel(); }),
            secondsBetweenSamples: 0.001f);

        Assert.AreEqual(active, rig.Model.ActiveBackendId);
        Assert.IsTrue(calibrated, rig.Model.CalibrationError);
        Assert.IsTrue(rig.Model.IsCalibrated);
    }
}

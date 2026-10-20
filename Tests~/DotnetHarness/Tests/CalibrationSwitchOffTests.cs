using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSeCenter.Backends.Jev;
using VcuSeCenter.VREmotionDetection;

// Setting UseCalibration to false stops a calibration that is running - wherever it is (a prompt, the wait between
// samples, a Jev request, a Jev retry delay) - instead of letting it finish with calibration switched off. Only the
// calibration's own prediction is canceled; unrelated predictions are left alone.
public class CalibrationSwitchOffTests
{
    private const string Key = "switch-off-key-1234";

    private static readonly string Good = JevTestData.Answers(0.9, 0, 0, 0, 0, 0, 0);

    // Waits until its token is canceled, as UnityWebRequestTransport does.
    private sealed class HoldingTransport : ICancelableHttpTransport
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public CancellationToken LastToken;

        public Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds) =>
            PostJsonAsync(url, bearerToken, jsonBody, timeoutSeconds, CancellationToken.None);

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds,
                                                        CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            Interlocked.Increment(ref calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpReply(200, Good);
        }
    }

    // Answers 503 (a transient failure, so the backend waits and retries) and counts the requests.
    private sealed class FailingTransport : IHttpTransport
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds)
        {
            Interlocked.Increment(ref calls);
            return new HttpReply(503, "overloaded");
        }
    }

    private static JevBackend Jev(IHttpTransport transport) =>
        new JevBackend(new JevOptions(Key) { MaxRetries = 2 }, FaceAuNames.Movement, transport);

    [TearDown]
    public void TearDown()
    {
        Worker.ReadbackDelayMs = 0;
        Awaitable.FrameDelayMs = 1;
        Awaitable.ScaledWaits = false;
    }

    [Test]
    public async Task Switching_off_during_a_prompt_stops_the_run_before_the_next_prompt()
    {
        using var rig = new HarnessRig();
        int prompts = 0;

        bool calibrated = await rig.Model.CalibrateAll(
            TestHelpers.Prompt(rig, n => { prompts = n; if (n == 2) rig.Model.UseCalibration = false; }),
            secondsBetweenSamples: 0.001f);

        Assert.IsFalse(calibrated, "a calibration that was switched off must not report success");
        Assert.AreEqual(2, prompts, "no prompt after the switch");
        Assert.IsFalse(rig.Model.IsCalibrated);
        StringAssert.Contains("switched off", rig.Model.CalibrationError);
    }

    [Test]
    public async Task Switching_off_during_the_wait_between_samples_ends_the_wait()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        rig.Model.StartCalibration();

        // 600 s between samples would take about six seconds of this test's fast clock per sample.
        var calibrating = TestHelpers.Start(() => rig.Model.CalibrateEmotion(Emotion.Anger, secondsBetweenSamples: 600f));
        await Task.Delay(150);
        rig.Model.UseCalibration = false;
        bool calibrated = await TestHelpers.Within(calibrating, 2000, "switching calibration off must end the wait between samples");

        Assert.IsFalse(calibrated);
        StringAssert.Contains("switched off", rig.Model.CalibrationError);
    }

    [Test]
    public async Task Switching_off_during_a_Jev_request_aborts_it_and_sends_no_more()
    {
        using var rig = new HarnessRig();
        var transport = new HoldingTransport();
        rig.Model.UseBackend(Jev(transport));
        rig.Model.StartCalibration();

        var calibrating = TestHelpers.Start(() => rig.Model.CalibrateEmotion(Emotion.Anger, 0.001f));
        await TestHelpers.WaitUntil(() => transport.Calls == 1, "the calibration should have sent its first request");
        rig.Model.UseCalibration = false;
        bool calibrated = await TestHelpers.Within(calibrating, 2000, "the request of a switched-off calibration must be aborted");

        Assert.IsFalse(calibrated);
        Assert.IsTrue(transport.LastToken.IsCancellationRequested, "the Jev request was told to stop");
        await Task.Delay(100);
        Assert.AreEqual(1, transport.Calls, "no further request after the switch");
        StringAssert.Contains("switched off", rig.Model.CalibrationError);
        Assert.IsNull(rig.Model.LastError, "stopping a calibration is not a prediction failure");
    }

    [Test]
    public async Task Switching_off_during_a_Jev_retry_delay_stops_the_retries()
    {
        using var rig = new HarnessRig();
        var transport = new FailingTransport();
        rig.Model.UseBackend(Jev(transport));
        rig.Model.StartCalibration();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var calibrating = TestHelpers.Start(() => rig.Model.CalibrateEmotion(Emotion.Anger, 0.001f));
        await TestHelpers.WaitUntil(() => transport.Calls == 1, "the calibration should have sent its first request");
        await Task.Delay(100);
        rig.Model.UseCalibration = false;
        bool calibrated = await TestHelpers.Within(calibrating, 2000, "the retry delay of a switched-off calibration must be cut short");

        Assert.IsFalse(calibrated);
        Assert.AreEqual(1, transport.Calls, "the retry never happens");
        Assert.Less(clock.ElapsedMilliseconds, JevBackend.InitialBackoffSeconds * 1000, "the first backoff (0.5 s) was cut short");
    }

    [Test]
    public async Task An_unrelated_prediction_is_not_canceled_when_calibration_is_switched_off()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        Task<PredictionOutcome> unrelated = null;

        bool calibrated = await rig.Model.CalibrateAll(async emotion =>
        {
            rig.Show((int)emotion);
            Worker.Canned = HarnessRig.Scores((int)emotion);

            // At the first prompt the calibration is between samples (not predicting) while somebody else's prediction runs.
            if (unrelated == null)
            {
                Worker.ReadbackDelayMs = 300;
                unrelated = TestHelpers.Start(() => rig.Model.TryPredict(saveResult: false));
                await Task.Delay(100);
                rig.Model.UseCalibration = false;
            }
        }, secondsBetweenSamples: 0.001f);

        var outcome = await unrelated;
        Assert.IsFalse(calibrated);
        StringAssert.Contains("switched off", rig.Model.CalibrationError, "the run stopped because of the switch, not for another reason");
        Assert.IsTrue(outcome.Ok, "the other prediction was not the calibration's, so it keeps running: " +
                                  (outcome.Error.HasValue ? outcome.Error.Value.ToString() : ""));
    }

    [Test]
    public async Task Switching_off_and_straight_back_on_still_stops_the_run()
    {
        using var rig = new HarnessRig();

        bool calibrated = await rig.Model.CalibrateAll(
            TestHelpers.Prompt(rig, n => { if (n == 2) { rig.Model.UseCalibration = false; rig.Model.UseCalibration = true; } }),
            secondsBetweenSamples: 0.001f);

        Assert.IsFalse(calibrated, "the run was told to stop even though the checkbox is on again");
        StringAssert.Contains("switched off", rig.Model.CalibrationError);
    }

    [Test]
    public async Task Unticking_the_checkbox_in_the_inspector_stops_the_run_at_the_next_checkpoint()
    {
        using var rig = new HarnessRig();
        int prompts = 0;

        // The Inspector writes the serialized field directly, not the property.
        bool calibrated = await rig.Model.CalibrateAll(
            TestHelpers.Prompt(rig, n => { prompts = n; if (n == 2) HarnessRig.Field(rig.Model, "useCalibration", false); }),
            secondsBetweenSamples: 0.001f);

        Assert.IsFalse(calibrated);
        Assert.AreEqual(2, prompts);
        StringAssert.Contains("switched off", rig.Model.CalibrationError);
    }

    [Test]
    public async Task A_new_run_after_switching_back_on_succeeds()
    {
        using var rig = new HarnessRig();
        Assert.IsFalse(await rig.Model.CalibrateAll(
            TestHelpers.Prompt(rig, n => { if (n == 3) rig.Model.UseCalibration = false; }), secondsBetweenSamples: 0.001f));

        rig.Model.UseCalibration = true;
        bool calibrated = await rig.Model.CalibrateAll(TestHelpers.Prompt(rig, n => { }), secondsBetweenSamples: 0.001f);

        Assert.IsTrue(calibrated, rig.Model.CalibrationError);
        Assert.IsTrue(rig.Model.IsCalibrated);
    }

    [Test]
    public async Task A_finished_calibration_is_kept_while_calibration_is_off_and_applies_again_when_it_is_on()
    {
        using var rig = new HarnessRig();
        Assert.IsTrue(await rig.Model.CalibrateAll(TestHelpers.Prompt(rig, n => { }), secondsBetweenSamples: 0.001f), rig.Model.CalibrationError);
        Assert.IsTrue(rig.Model.SaveCalibration("kept"));

        rig.Model.UseCalibration = false;
        Assert.IsFalse(rig.Model.IsCalibrated, "not applied while it is off");
        Assert.IsTrue(rig.Model.HasSavedCalibration("kept"), "the saved file is untouched");

        rig.Model.UseCalibration = true;
        Assert.IsTrue(rig.Model.IsCalibrated, "the profile was kept and applies again");
    }

    [Test]
    public async Task Switching_calibration_off_when_nothing_is_running_does_nothing_else()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        var running = TestHelpers.Start(() => rig.Model.TryPredict(saveResult: false));

        rig.Model.UseCalibration = false;
        rig.Model.UseCalibration = true;

        Assert.IsTrue((await running).Ok);
        Assert.IsNull(rig.Model.LastError);
    }
}

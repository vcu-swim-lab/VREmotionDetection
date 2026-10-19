using System.Threading.Tasks;
using NUnit.Framework;
using Unity.InferenceEngine;
using UnityEngine;
using VcuSwimLab.VREmotionDetection;

// The headset delivers face data in real time, so pausing or slowing the game (Time.timeScale) must not change how
// often the face is sampled, whether a prediction finds tracking, or how long a calibration takes.
public class TimeScaleTests
{
    [TearDown]
    public void TearDown()
    {
        Time.Override = null;
        Time.UnscaledOverride = null;
        Awaitable.ScaledWaits = false;
        Worker.ReadbackDelayMs = 0;
    }

    // Drives DeviceManager.Update by hand with two clocks, so the test needs no real waiting: every "frame" is 0.11 s of
    // unscaled time (a little more than the 0.1 s poll interval, so every frame is due) and gameScale times that on the game clock.
    private static int FaceSamplesInTwentyFrames(float gameScale, float gameStart = 5f)
    {
        var host = new GameObject("time-scale");
        var ovr = host.AddComponent<OVRFaceExpressions>();
        ovr.ValidExpressions = true;
        ovr.Values = HarnessRig.Face(0);
        var manager = host.AddComponent<DeviceManager>();

        float unscaled = 5f, game = gameStart;
        Time.UnscaledOverride = unscaled;
        Time.Override = game;
        var device = manager.Require(InputType.FaceAU);
        int samples = 0;
        device.OnPoll += (type, data) => samples++;

        for (int frame = 0; frame < 20; ++frame)
        {
            unscaled += 0.11f;
            game += 0.11f * gameScale;
            Time.UnscaledOverride = unscaled;
            Time.Override = game;
            HarnessRig.Call(manager, "Update");
        }

        return samples;
    }

    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(1f)]
    [TestCase(2f)]
    public void Face_sampling_does_not_depend_on_the_game_time_scale(float timeScale)
    {
        Assert.AreEqual(20, FaceSamplesInTwentyFrames(timeScale), $"time scale {timeScale}");
    }

    [TestCase(100f)]
    [TestCase(0f)]
    public void The_first_poll_is_scheduled_on_the_same_clock_as_the_later_ones(float gameClockAtStart)
    {
        // If Require used the game clock while Update used the unscaled one, a game clock far ahead of (or behind) the
        // real one would delay (or skip) the first reading.
        Assert.AreEqual(20, FaceSamplesInTwentyFrames(1f, gameClockAtStart), $"game clock at {gameClockAtStart}");
    }

    [Test]
    public async Task A_paused_game_still_gets_face_predictions()
    {
        using var rig = new HarnessRig(gameClockScale: 0f);
        rig.Show(3);
        Worker.Canned = HarnessRig.Scores(3);

        var outcome = await rig.Model.TryPredict(timeoutSeconds: 1.5f, saveResult: false);

        Assert.IsTrue(outcome.Ok, outcome.Error.HasValue ? outcome.Error.Value.ToString() : null);
        Assert.AreEqual(Emotion.Happiness, outcome.Prediction.Emotion);
    }

    [TestCase(0f)]
    [TestCase(0.5f)]
    [TestCase(2f)]
    public async Task A_calibration_takes_the_same_real_time_whatever_the_game_time_scale(float timeScale)
    {
        // With ScaledWaits the stand-in behaves like a Unity wait scaled by Time.timeScale: it follows the game clock.
        Awaitable.ScaledWaits = true;
        using var rig = new HarnessRig(gameClockScale: timeScale);
        rig.Show(0);
        Worker.Canned = HarnessRig.Scores(0);
        await rig.Settle();
        rig.Model.StartCalibration();

        var calibrating = TestHelpers.Start(() => rig.Model.CalibrateEmotion(Emotion.Anger, secondsBetweenSamples: 0.5f));
        bool calibrated = await TestHelpers.Within(calibrating, 8000, $"a calibration must not stall when the time scale is {timeScale}");

        Assert.IsTrue(calibrated, rig.Model.CalibrationError);
    }
}

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VcuSwimLab.VREmotionDetection;

// CalibrateEmotion and CalibrateAll follow one contract: with calibration off they return false; otherwise a bad
// interval (negative, NaN, infinite) or attempt count (below 1) throws ArgumentOutOfRangeException - and a rejected
// call changes nothing, in particular it does not throw away a calibration that already exists.
public class CalibrationArgumentTests
{
    private static async Task<HarnessRig> RigWithFinishedCalibration()
    {
        var rig = new HarnessRig();
        Assert.IsTrue(await rig.Model.CalibrateAll(TestHelpers.Prompt(rig, n => { }), secondsBetweenSamples: 0.001f), rig.Model.CalibrationError);
        Assert.IsTrue(rig.Model.IsCalibrated);
        return rig;
    }

    // A call that wrongly accepts a bad argument could run for ever; bound it so the test fails instead of hanging.
    private static async Task Refused(Func<Awaitable<bool>> call)
    {
        await TestHelpers.Within(TestHelpers.Start(call), 3000, "the call should have been refused straight away");
    }

    [TestCase(-1f)]
    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void A_bad_interval_is_refused_by_both_methods(float interval)
    {
        using var rig = new HarnessRig();
        rig.Model.StartCalibration();

        var single = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await Refused(() => rig.Model.CalibrateEmotion(Emotion.Anger, interval)));
        var all = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await Refused(() => rig.Model.CalibrateAll(null, interval)));

        // The parameter name proves the caller's argument was rejected, not some failure further down.
        Assert.AreEqual("secondsBetweenSamples", single.ParamName);
        Assert.AreEqual("secondsBetweenSamples", all.ParamName);
    }

    [TestCase(0)]
    [TestCase(-3)]
    public void A_bad_attempt_count_is_refused_by_both_methods(int attempts)
    {
        using var rig = new HarnessRig();
        rig.Model.StartCalibration();

        var single = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await Refused(() => rig.Model.CalibrateEmotion(Emotion.Anger, 1f, attempts)));
        var all = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await Refused(() => rig.Model.CalibrateAll(null, 1f, attempts)));

        Assert.AreEqual("maxAttempts", single.ParamName);
        Assert.AreEqual("maxAttempts", all.ParamName);
    }

    [Test]
    public async Task The_error_names_the_argument()
    {
        using var rig = new HarnessRig();

        var interval = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await rig.Model.CalibrateAll(null, float.NaN));
        var attempts = Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await rig.Model.CalibrateAll(null, 1f, 0));

        Assert.AreEqual("secondsBetweenSamples", interval.ParamName);
        Assert.AreEqual("maxAttempts", attempts.ParamName);
        await Task.CompletedTask;
    }

    [Test]
    public async Task A_zero_interval_is_valid()
    {
        using var rig = new HarnessRig();
        rig.Show(0);
        await rig.Settle();
        rig.Model.StartCalibration();

        Assert.IsTrue(await rig.Model.CalibrateEmotion(Emotion.Anger, 0f), rig.Model.CalibrationError);
    }

    [Test]
    public async Task A_rejected_CalibrateAll_keeps_the_calibration_that_already_exists()
    {
        using var rig = await RigWithFinishedCalibration();

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () => await rig.Model.CalibrateAll(null, -1f, 0));

        Assert.IsTrue(rig.Model.IsCalibrated, "a caller mistake must not discard a working calibration");
        Assert.IsTrue(string.IsNullOrEmpty(rig.Model.CalibrationError));
    }

    [Test]
    public async Task The_arguments_are_checked_before_the_first_prompt()
    {
        using var rig = new HarnessRig();
        int prompts = 0;

        Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
            await rig.Model.CalibrateAll(async emotion => { prompts++; await Task.CompletedTask; }, -1f));

        Assert.AreEqual(0, prompts);
        await Task.CompletedTask;
    }

    [Test]
    public async Task With_calibration_off_both_methods_return_false_even_for_bad_arguments()
    {
        using var rig = new HarnessRig(useCalibration: false);

        Assert.IsFalse(await rig.Model.CalibrateEmotion(Emotion.Anger, -1f, 0));
        Assert.IsFalse(await rig.Model.CalibrateAll(null, -1f, 0));
        Assert.IsFalse(string.IsNullOrEmpty(rig.Model.CalibrationError), "CalibrationError says calibration is off");
    }
}

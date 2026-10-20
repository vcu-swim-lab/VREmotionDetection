using System;

using NUnit.Framework;
using VcuSeCenter.Calibration;

public class MovementCalibratorTests
{
    private static readonly string[] Names =
    {
        "Anger", "Disgust", "Fear", "Happiness", "Neutral", "Sadness", "Surprise",
    };

    private const int Classes = 7;

    private static float[] Scores(int peak)
    {
        var scores = new float[Classes];
        for (int i = 0; i < Classes; ++i) scores[i] = 0.4f / (Classes - 1);
        scores[peak] = 0.6f;
        return scores;
    }

    private static float[][] Window(int cls)
    {
        var window = new float[5][];
        for (int f = 0; f < 5; ++f)
        {
            window[f] = new float[70];
            for (int a = 0; a < 70; ++a) window[f][a] = 0.05f;
            for (int a = cls * 8; a < cls * 8 + 8; ++a) window[f][a] = 0.8f;
        }

        return window;
    }

    private static MovementCalibrator Calibrated()
    {
        var calibrator = new MovementCalibrator(Names);
        for (int cls = 0; cls < Classes; ++cls)
        {
            calibrator.BeginEmotion(cls);
            while (!calibrator.IsEmotionComplete(cls))
            {
                calibrator.Observe(Scores(cls), Window(cls));
            }
        }

        Assert.IsTrue(calibrator.Finish(), calibrator.Error);
        return calibrator;
    }

    [Test]
    public void An_emotion_takes_ten_samples_and_missing_ones_do_not_count()
    {
        var calibrator = new MovementCalibrator(Names);
        calibrator.BeginEmotion(0);

        Assert.IsFalse(calibrator.Observe(null, Window(0)));
        Assert.IsFalse(calibrator.Observe(Scores(0), null));

        for (int i = 0; i < MovementCalibrator.SamplesToCollectPerEmotion; ++i)
        {
            Assert.IsFalse(calibrator.IsEmotionComplete(0));
            Assert.IsTrue(calibrator.Observe(Scores(0), Window(0)));
        }

        Assert.IsTrue(calibrator.IsEmotionComplete(0));
        Assert.IsFalse(calibrator.Observe(Scores(0), Window(0)), "a complete emotion takes no more");
        Assert.AreEqual(10, MovementCalibrator.SamplesToCollectPerEmotion);
    }

    [Test]
    public void Finish_refuses_an_incomplete_calibration()
    {
        var calibrator = new MovementCalibrator(Names);
        calibrator.BeginEmotion(0);
        for (int i = 0; i < 10; ++i) calibrator.Observe(Scores(0), Window(0));

        Assert.IsFalse(calibrator.Finish());
        Assert.IsFalse(calibrator.IsReady);
        StringAssert.Contains("Disgust 0/9", calibrator.Error);
        Assert.IsNull(calibrator.Classify(Scores(0), Window(0)));
    }

    [Test]
    public void Classify_is_the_score_plus_movement_nearest_centroid()
    {
        var calibrator = Calibrated();

        var scores = Scores(0);
        var window = Window(5);
        var decision = calibrator.Classify(scores, window);
        Assert.IsTrue(decision.HasValue);

        var scoreCentroids = new double[Classes][];
        var movementCentroids = new double[Classes][];
        for (int cls = 0; cls < Classes; ++cls)
        {
            scoreCentroids[cls] = CalibrationMath.LogScore(Scores(cls));
            movementCentroids[cls] = CalibrationMath.MovementFeature(Window(cls));
        }

        var expected = CalibrationMath.NearestOfSum(scoreCentroids, CalibrationMath.LogScore(scores),
                                                    movementCentroids, CalibrationMath.MovementFeature(window));
        Assert.AreEqual(expected.ClassIndex, decision.Value.ClassIndex);
        Assert.AreEqual(expected.Margin, decision.Value.Margin, 1e-12);

        Assert.AreEqual(3, calibrator.Classify(Scores(3), Window(3)).Value.ClassIndex);
    }

    [Test]
    public void An_emotion_cannot_be_collected_twice_without_Reset()
    {
        var calibrator = Calibrated();
        Assert.Throws<InvalidOperationException>(() => calibrator.BeginEmotion(0));

        calibrator.Reset();
        Assert.IsFalse(calibrator.IsReady);
        Assert.DoesNotThrow(() => calibrator.BeginEmotion(0));
    }

    [Test]
    public void ObserveAt_drops_position_0_and_a_missed_position_stays_missed()
    {
        var calibrator = new MovementCalibrator(Names);
        for (int cls = 0; cls < Classes; ++cls)
        {
            calibrator.BeginEmotion(cls);
            Assert.IsFalse(calibrator.ObserveAt(0, Scores(cls), Window(cls)), "position 0 is dropped");
            for (int i = 1; i <= 9; ++i)
            {
                bool missed = cls == 0 && i == 5;
                Assert.AreEqual(!missed, calibrator.ObserveAt(i, missed ? null : Scores(cls), Window(cls)));
            }

            Assert.IsTrue(calibrator.IsEmotionComplete(cls));
        }

        CollectionAssert.AreEqual(new[] { 8, 9, 9, 9, 9, 9, 9 }, calibrator.KeptSampleCounts());
        Assert.IsFalse(calibrator.Finish(), "a class with 8 of 9 samples must fail");
        StringAssert.Contains("Anger 8/9", calibrator.Error);
    }

    [Test]
    public void A_saved_profile_restores_identical_decisions()
    {
        var original = Calibrated();
        var restored = MovementCalibrator.FromProfile(original.ExportProfile(), Names, out string error);
        Assert.IsNotNull(restored, error);
        Assert.IsTrue(restored.IsReady);

        var rng = new Random(7);
        for (int i = 0; i < 50; ++i)
        {
            var scores = Scores(rng.Next(Classes));
            var window = Window(rng.Next(Classes));
            var a = original.Classify(scores, window).Value;
            var b = restored.Classify(scores, window).Value;
            Assert.AreEqual(a.ClassIndex, b.ClassIndex);
            Assert.AreEqual(a.Margin, b.Margin, 0.0);
        }
    }

    [Test]
    public void A_mismatched_or_damaged_profile_is_refused()
    {
        var profile = Calibrated().ExportProfile();
        Assert.IsNull(new MovementCalibrator(Names).ExportProfile(), "nothing to export before Finish");

        var reordered = (string[])Names.Clone();
        (reordered[0], reordered[1]) = (reordered[1], reordered[0]);
        Assert.IsNull(MovementCalibrator.FromProfile(profile, reordered, out _));

        var otherVersion = new CalibrationProfile("runtime-calibration-v0", profile.ClassNames,
                                                  profile.ScoreCentroids, profile.MovementCentroids);
        Assert.IsNull(MovementCalibrator.FromProfile(otherVersion, Names, out _));

        profile.MovementCentroids[2][5] = double.NaN;
        Assert.IsNull(MovementCalibrator.FromProfile(profile, Names, out string error));
        StringAssert.Contains("not finite", error);
    }

    [Test]
    public void Observe_before_BeginEmotion_is_refused()
    {
        var calibrator = new MovementCalibrator(Names);
        Assert.Throws<InvalidOperationException>(() => calibrator.Observe(Scores(0), Window(0)));
    }
}

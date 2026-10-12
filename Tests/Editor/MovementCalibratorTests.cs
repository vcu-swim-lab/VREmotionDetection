using System;

using NUnit.Framework;
using VcuSwimLab.Calibration;

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

    // Five frames of 70 values; emotion `cls` moves its own block of face AUs.
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

        // The model says Anger, the face moves like Sadness: the decision is the sum of both cosines.
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
    public void Observe_before_BeginEmotion_is_refused()
    {
        var calibrator = new MovementCalibrator(Names);
        Assert.Throws<InvalidOperationException>(() => calibrator.Observe(Scores(0), Window(0)));
    }
}

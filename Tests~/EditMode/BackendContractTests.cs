using System;
using System.Linq;
using NUnit.Framework;
using VcuSeCenter.Backends;
using VcuSeCenter.VREmotionDetection;

public class BackendContractTests
{
    [Test]
    public void Scores_must_have_the_right_count_and_be_finite_and_non_negative()
    {
        Assert.IsTrue(ScoreValidation.TryValidate(new[] { 0.2f, 0.8f }, 2, out var problem), problem);
        Assert.IsFalse(ScoreValidation.TryValidate(null, 2, out problem));
        Assert.IsNotEmpty(problem);
        Assert.IsFalse(ScoreValidation.TryValidate(new[] { 0.2f }, 2, out problem));
        StringAssert.Contains("expected 2", problem);
        Assert.IsFalse(ScoreValidation.TryValidate(new[] { 0.2f, float.NaN }, 2, out problem));
        StringAssert.Contains("score 1", problem);
        Assert.IsFalse(ScoreValidation.TryValidate(new[] { float.PositiveInfinity, 0.2f }, 2, out _));
        Assert.IsFalse(ScoreValidation.TryValidate(new[] { 0.2f, -0.1f }, 2, out problem));
        StringAssert.Contains("negative", problem);
    }

    [Test]
    public void Argmax_picks_the_largest_the_first_of_a_tie_and_refuses_bad_input()
    {
        Assert.AreEqual(1, ScoreValidation.Argmax(new[] { 0.1f, 0.6f, 0.3f }));
        Assert.AreEqual(0, ScoreValidation.Argmax(new[] { 0.5f, 0.5f, 0.1f }));
        Assert.AreEqual(-1, ScoreValidation.Argmax(null));
        Assert.AreEqual(-1, ScoreValidation.Argmax(new float[0]));
        Assert.AreEqual(-1, ScoreValidation.Argmax(new[] { 0.1f, float.NaN, 0.9f }));
        Assert.AreEqual(-1, ScoreValidation.Argmax(new[] { float.NaN, 0.9f }));
    }

    [Test]
    public void A_window_needs_enough_finite_frames_and_values()
    {
        var good = new[] { new float[70], new float[70] };

        Assert.IsTrue(WindowValidation.TryValidate(good, 2, 70, out var problem), problem);
        Assert.IsFalse(WindowValidation.TryValidate(null, 1, 1, out problem));
        Assert.IsFalse(WindowValidation.TryValidate(good, 3, 70, out problem));
        StringAssert.Contains("2 frames", problem);
        Assert.IsFalse(WindowValidation.TryValidate(good, 2, 71, out problem));
        StringAssert.Contains("frame 0", problem);
        Assert.IsFalse(WindowValidation.TryValidate(new[] { null, new float[70] }, 1, 70, out _));

        var withNaN = new[] { new float[70], new float[70] };
        withNaN[1][5] = float.NaN;
        Assert.IsFalse(WindowValidation.TryValidate(withNaN, 2, 70, out problem));
        StringAssert.Contains("frame 1 value 5", problem);
    }

    [Test]
    public void Only_connection_and_load_errors_are_transient()
    {
        foreach (var code in new[]
                 {
                     PredictionErrorCode.RateLimited, PredictionErrorCode.Timeout,
                     PredictionErrorCode.Unreachable, PredictionErrorCode.ServerError,
                 })
        {
            Assert.IsTrue(new PredictionError(code, "x").IsTransient, code.ToString());
        }

        foreach (var code in new[]
                 {
                     PredictionErrorCode.Unauthorized, PredictionErrorCode.InvalidRequest,
                     PredictionErrorCode.NotConfigured, PredictionErrorCode.NoTracking,
                     PredictionErrorCode.InvalidResponse, PredictionErrorCode.Internal,
                     PredictionErrorCode.Canceled, PredictionErrorCode.Busy,
                     PredictionErrorCode.NotReady, PredictionErrorCode.InvalidInput,
                 })
        {
            Assert.IsFalse(new PredictionError(code, "x").IsTransient, code.ToString());
        }
    }

    [Test]
    public void Exactly_four_codes_are_transient_so_a_new_code_has_to_be_classified_on_purpose()
    {
        var transient = Enum.GetValues(typeof(PredictionErrorCode)).Cast<PredictionErrorCode>()
            .Where(code => new PredictionError(code, "x").IsTransient)
            .OrderBy(code => code)
            .ToArray();

        CollectionAssert.AreEqual(
            new[]
            {
                PredictionErrorCode.RateLimited, PredictionErrorCode.Timeout,
                PredictionErrorCode.Unreachable, PredictionErrorCode.ServerError,
            }.OrderBy(code => code).ToArray(),
            transient);
    }

    [Test]
    public void An_error_prints_its_backend_code_and_message()
    {
        Assert.AreEqual("Unauthorized: bad key", new PredictionError(PredictionErrorCode.Unauthorized, "bad key").ToString());
        Assert.AreEqual("jev Timeout: slow", new PredictionError(PredictionErrorCode.Timeout, "slow", "jev").ToString());
        Assert.AreEqual(string.Empty, new PredictionError(PredictionErrorCode.Internal, null).Message);
    }

    [Test]
    public void A_backend_result_is_either_scores_or_an_error()
    {
        var ok = BackendResult.Success(new[] { 1f }, new[] { 0.5f }, true);
        var failed = BackendResult.Failure(PredictionErrorCode.Busy, "later", "id");

        Assert.IsTrue(ok.Ok);
        Assert.IsTrue(ok.ReferenceFacesSent);
        Assert.IsNull(ok.Error);
        Assert.IsFalse(failed.Ok);
        Assert.IsNull(failed.Scores);
        Assert.AreEqual(PredictionErrorCode.Busy, failed.Error.Value.Code);
        Assert.AreEqual("id", failed.Error.Value.BackendId);
        Assert.Throws<ArgumentNullException>(() => BackendResult.Success(null));
        Assert.IsFalse(default(BackendResult).Ok);
    }

    [Test]
    public void A_prediction_outcome_is_either_a_prediction_or_an_error()
    {
        var prediction = new FacePrediction(Emotion.Fear, 0.5f, new float[7], null, 3, null, "id");
        var ok = PredictionOutcome.Success(prediction);
        var failed = PredictionOutcome.Failure(new PredictionError(PredictionErrorCode.NoTracking, "lost"));

        Assert.IsTrue(ok.Ok);
        Assert.AreEqual(Emotion.Fear, ok.PredictionOrNull.Value.Emotion);
        Assert.AreEqual("id", ok.Prediction.BackendId);
        Assert.IsFalse(failed.Ok);
        Assert.IsNull(failed.PredictionOrNull);
        Assert.AreEqual(PredictionErrorCode.NoTracking, failed.Error.Value.Code);
        Assert.IsFalse(default(PredictionOutcome).Ok);
    }

    [Test]
    public void ScoreOf_is_the_raw_answer_when_there_is_one()
    {
        var scores = new[] { 0.4f, 0.6f };
        var raw = new[] { 0.1f, 0.3f };

        Assert.AreEqual(0.3f, new FacePrediction(Emotion.Disgust, 0.3f, scores, null, 0, raw).ScoreOf(Emotion.Disgust));
        Assert.AreEqual(0.6f, new FacePrediction(Emotion.Disgust, 0.6f, scores, null, 0).ScoreOf(Emotion.Disgust));
    }
}

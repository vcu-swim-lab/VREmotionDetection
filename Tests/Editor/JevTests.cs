using System;
using System.Linq;

using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VcuSwimLab.Calibration;
using VcuSwimLab.Jev;

public class JevTests
{
    private static readonly string[] Classes = Enum.GetNames(typeof(Emotion));

    private static readonly string[] AuNames = Enumerable.Range(0, CalibrationMath.FaceActionUnitCount)
        .Select(i => $"AU{i}")
        .ToArray();

    private const int Block = 9;

    private static float[][] Window(int cls)
    {
        var window = new float[5][];
        for (int f = 0; f < window.Length; ++f)
        {
            window[f] = new float[70];
            for (int a = 0; a < 70; ++a) window[f][a] = 0.02f;
            for (int a = cls * Block; a < cls * Block + Block; ++a) window[f][a] = 0.8f;
        }

        return window;
    }

    private static CalibrationProfile Profile()
    {
        var centroids = new double[Classes.Length][];
        for (int c = 0; c < centroids.Length; ++c)
        {
            centroids[c] = new double[CalibrationMath.FaceActionUnitCount];
            for (int a = 0; a < centroids[c].Length; ++a) centroids[c][a] = 0.02;
            for (int a = c * Block; a < c * Block + Block; ++a) centroids[c][a] = 0.8;
        }

        return new CalibrationProfile("test", Classes, centroids, centroids);
    }

    private static JObject Build(float[][] window, CalibrationProfile profile)
    {
        return JObject.Parse(JevRequestBuilder.Build("jev-test", window, AuNames, Classes, profile));
    }

    private static string Answers(params double[] values)
    {
        var answers = new JObject();
        for (int e = 0; e < values.Length; ++e)
        {
            answers[Classes[e]] = new JObject { ["type"] = "noul", ["noul"] = values[e] };
        }

        return new JObject { ["answers"] = answers }.ToString();
    }

    [Test]
    public void Strength_bands_start_at_their_thresholds()
    {
        Assert.AreEqual("strong", JevFaceDescriber.Strength(0.5));
        Assert.AreEqual("moderate", JevFaceDescriber.Strength(0.2));
        Assert.AreEqual("slight", JevFaceDescriber.Strength(0.05));
        Assert.IsNull(JevFaceDescriber.Strength(0.049));
    }

    [Test]
    public void TopIndices_orders_by_value_and_skips_inactive_values()
    {
        var values = new[] { 0.3, 0.01, 0.9, 0.3, 0.6 };

        CollectionAssert.AreEqual(new[] { 2, 4, 0 }, JevFaceDescriber.TopIndices(values, 3));
        CollectionAssert.AreEqual(new[] { 2, 4, 0, 3 }, JevFaceDescriber.TopIndices(values, 10));
        Assert.IsEmpty(JevFaceDescriber.TopIndices(new[] { 0.0, 0.01 }, 3));
    }

    [Test]
    public void Label_names_each_value_with_its_strength()
    {
        var values = new[] { 0.6, 0.25, 0.07 };

        CollectionAssert.AreEqual(new[] { "A (strong)", "B (moderate)", "C (slight)" },
                                  JevFaceDescriber.Label(values, new[] { "A", "B", "C" }, new[] { 0, 1, 2 }));
    }

    [Test]
    public void ActivityLevel_follows_the_mean()
    {
        Assert.AreEqual("low", JevFaceDescriber.ActivityLevel(new double[63]));
        Assert.AreEqual("moderate", JevFaceDescriber.ActivityLevel(Enumerable.Repeat(0.03, 63).ToArray()));
        Assert.AreEqual("high", JevFaceDescriber.ActivityLevel(Enumerable.Repeat(0.1, 63).ToArray()));
    }

    [Test]
    public void ActiveNow_counts_signature_movements_that_reach_half_the_reference()
    {
        var reference = new[] { 0.4, 0.4, 0.0 };
        var current = new[] { 0.3, 0.1, 0.0 };

        Assert.AreEqual("1 of 2", JevFaceDescriber.ActiveNow(current, reference, new[] { 0, 1 }));
    }

    [Test]
    public void Ranks_put_the_most_similar_first_and_ties_go_to_the_lower_index()
    {
        CollectionAssert.AreEqual(new[] { 2, 1, 3 }, JevFaceDescriber.Ranks(new[] { 0.9, 0.95, 0.8 }));
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, JevFaceDescriber.Ranks(new[] { 0.9, 0.9, 0.5 }));
    }

    [Test]
    public void GapWord_is_narrow_when_the_top_two_are_close()
    {
        Assert.AreEqual("clear", JevFaceDescriber.GapWord(new[] { 0.95, 0.9, 0.8 }));
        Assert.AreEqual("narrow", JevFaceDescriber.GapWord(new[] { 0.95, 0.94, 0.8 }));
    }

    [Test]
    public void Every_emotion_has_criteria_and_an_unknown_one_is_refused()
    {
        foreach (var emotion in Classes)
        {
            var (yes, no) = JevPrompt.CriteriaFor(emotion);
            Assert.IsNotEmpty(yes, emotion);
            Assert.IsNotEmpty(no, emotion);
        }

        Assert.Throws<ArgumentException>(() => JevPrompt.CriteriaFor("Contempt"));
    }

    [Test]
    public void Request_without_calibration_has_the_face_and_seven_questions_only()
    {
        var request = Build(Window(3), null);

        Assert.AreEqual("jev-test", (string)request["model"]);
        Assert.IsNotNull(request["state"]["current_face"]);
        Assert.IsNull(request["state"]["players_own_faces"]);

        var questions = (JObject)request["questions"];
        CollectionAssert.AreEqual(Classes, questions.Properties().Select(p => p.Name).ToArray());
        foreach (var emotion in Classes)
        {
            var question = questions[emotion];
            Assert.AreEqual("noul", (string)question["type"]);
            StringAssert.Contains(emotion, (string)question["instructions"]["question"]);
            StringAssert.Contains(emotion, (string)question["instructions"]["how_to_judge"]);
            Assert.IsNotEmpty((string)question["criteria"]["true"]);
            Assert.IsNotEmpty((string)question["criteria"]["false"]);
        }
    }

    [Test]
    public void Current_face_lists_the_strongest_movements_first()
    {
        var face = Build(Window(2), null)["state"]["current_face"];
        var active = face["active_aus"].Select(t => (string)t).ToArray();

        Assert.AreEqual("high", (string)face["overall_activity"]);
        Assert.AreEqual(JevRequestBuilder.ActiveAusShown, active.Length);
        Assert.AreEqual($"AU{2 * Block} (strong)", active[0]);
    }

    [Test]
    public void Request_with_calibration_describes_each_emotion_in_order()
    {
        const int shown = 3;
        var window = Window(shown);
        var profile = Profile();

        var faces = (JObject)Build(window, profile)["state"]["players_own_faces"];

        CollectionAssert.AreEqual(new[] { "closest_match_gap" }.Concat(Classes).ToArray(),
                                  faces.Properties().Select(p => p.Name).ToArray());

        var feature = CalibrationMath.MovementFeature(window);
        for (int e = 0; e < Classes.Length; ++e)
        {
            var face = faces[Classes[e]];
            double expected = Math.Round(CalibrationMath.CosineSimilarity(feature, profile.MovementCentroids[e]),
                                         JevRequestBuilder.SimilarityDecimals);

            Assert.AreEqual(expected, (double)face["similarity"], 1e-9, Classes[e]);
            Assert.AreEqual(JevRequestBuilder.SignatureAusShown, face["signature_aus"].Count(), Classes[e]);
        }

        Assert.AreEqual(1, (int)faces[Classes[shown]]["rank"]);
        Assert.AreEqual("clear", (string)faces["closest_match_gap"]);
        CollectionAssert.AreEquivalent(Enumerable.Range(1, Classes.Length),
                                       Classes.Select(c => (int)faces[c]["rank"]).ToArray());
        Assert.AreEqual($"{JevRequestBuilder.SignatureAusShown} of {JevRequestBuilder.SignatureAusShown}",
                        (string)faces[Classes[shown]]["signature_aus_active_now"]);
        Assert.AreEqual($"0 of {JevRequestBuilder.SignatureAusShown}", (string)faces[Classes[0]]["signature_aus_active_now"]);
    }

    [Test]
    public void Request_refuses_an_empty_window()
    {
        Assert.Throws<ArgumentException>(() => JevRequestBuilder.Build("m", new float[0][], AuNames, Classes, null));
    }

    [Test]
    public void Parse_normalises_the_seven_answers_to_probabilities()
    {
        Assert.IsTrue(JevResponseParser.TryParse(Answers(0.1, 0.1, 0.1, 0.5, 0.1, 0.05, 0.05), Classes,
                                                 out var scores, out var error), error);

        Assert.AreEqual(1f, scores.Sum(), 1e-5f);
        Assert.AreEqual(0.5f, scores[3], 1e-5f);
        Assert.AreEqual(3, Utils.Argmax(scores));
    }

    [Test]
    public void Parse_refuses_missing_negative_and_all_zero_answers()
    {
        var missing = JObject.Parse(Answers(0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1));
        ((JObject)missing["answers"]).Remove("Surprise");

        Assert.IsFalse(JevResponseParser.TryParse(missing.ToString(), Classes, out _, out var error));
        StringAssert.Contains("Surprise", error);

        Assert.IsFalse(JevResponseParser.TryParse(Answers(0.1, 0.1, 0.1, -0.2, 0.1, 0.1, 0.1), Classes, out _, out error));
        StringAssert.Contains("Happiness", error);

        Assert.IsFalse(JevResponseParser.TryParse(Answers(0, 0, 0, 0, 0, 0, 0), Classes, out _, out error));
        StringAssert.Contains("no emotion", error);
    }

    [Test]
    public void Parse_refuses_text_that_is_not_a_response()
    {
        Assert.IsFalse(JevResponseParser.TryParse("<html>", Classes, out var scores, out var error));
        Assert.IsNull(scores);
        Assert.IsNotEmpty(error);
        Assert.IsFalse(JevResponseParser.TryParse("{}", Classes, out _, out _));
        Assert.IsFalse(JevResponseParser.TryParse(null, Classes, out _, out _));
    }

    [Test]
    public void Failure_messages_name_the_cause_and_keep_the_servers_detail()
    {
        StringAssert.Contains("API key", JevResponseParser.FailureMessage(401, "{\"detail\":\"Invalid key\"}"));
        StringAssert.Contains("Invalid key", JevResponseParser.FailureMessage(401, "{\"detail\":\"Invalid key\"}"));
        StringAssert.Contains("Field required", JevResponseParser.FailureMessage(
            422, "{\"detail\":[{\"loc\":[\"body\",\"state\"],\"msg\":\"Field required\",\"type\":\"missing\"}]}"));
        StringAssert.Contains("rate limit", JevResponseParser.FailureMessage(429, ""));
        StringAssert.Contains("no response", JevResponseParser.FailureMessage(0, "Cannot connect to destination host"));
        StringAssert.Contains("500", JevResponseParser.FailureMessage(500, "boom"));
        StringAssert.Contains("boom", JevResponseParser.FailureMessage(500, "boom"));
        StringAssert.Contains("nested", JevResponseParser.FailureMessage(400, "{\"error\":{\"message\":\"nested\"}}"));
    }

    [Test]
    public void Only_transient_failures_are_retried()
    {
        foreach (long status in new long[] { 0, 408, 429, 500, 503, 529 })
        {
            Assert.IsTrue(JevClient.ShouldRetry(status), status.ToString());
        }

        foreach (long status in new long[] { 400, 401, 403, 404, 422 })
        {
            Assert.IsFalse(JevClient.ShouldRetry(status), status.ToString());
        }
    }

    [Test]
    public void Backoff_doubles_and_is_capped()
    {
        Assert.AreEqual(0.5f, JevClient.BackoffSeconds(0), 1e-6f);
        Assert.AreEqual(1f, JevClient.BackoffSeconds(1), 1e-6f);
        Assert.AreEqual(2f, JevClient.BackoffSeconds(2), 1e-6f);
        Assert.AreEqual(JevClient.MaxBackoffSeconds, JevClient.BackoffSeconds(20), 1e-6f);
    }

    [Test]
    public void The_asset_key_wins_over_the_environment_and_blanks_count_as_missing()
    {
        Assert.AreEqual("asset", JevSettings.Resolve("  asset ", "env"));
        Assert.AreEqual("env", JevSettings.Resolve("", "env"));
        Assert.AreEqual("env", JevSettings.Resolve(null, " env "));
        Assert.IsNull(JevSettings.Resolve(" ", null));
        Assert.IsNull(JevSettings.Resolve(null, ""));
    }

    [Test]
    public void Argmax_picks_the_largest_and_the_first_of_a_tie()
    {
        Assert.AreEqual(1, Utils.Argmax(new[] { 0.1f, 0.6f, 0.3f }));
        Assert.AreEqual(0, Utils.Argmax(new[] { 0.5f, 0.5f, 0.1f }));
    }

    private enum Ordered
    {
        Invalid = -1,
        First = 0,
        Second = 1,
        Third = 2,
        Max = 3,
    }

    private enum Scrambled
    {
        Second = 1,
        Invalid = -1,
        First = 0,
    }

    [Test]
    public void Parse_keeps_jevs_own_answers_next_to_the_normalised_scores()
    {
        Assert.IsTrue(JevResponseParser.TryParse(Answers(0.2, 0, 0, 0.6, 0, 0, 0), Classes,
                                                 out var scores, out var raw, out var error), error);

        Assert.AreEqual(0.25f, scores[0], 1e-5f);
        Assert.AreEqual(0.75f, scores[3], 1e-5f);
        Assert.AreEqual(0.2f, raw[0], 1e-5f);
        Assert.AreEqual(0.6f, raw[3], 1e-5f);
        Assert.IsTrue(JevResponseParser.TryParse(Answers(0.2, 0, 0, 0.6, 0, 0, 0), Classes, out var plain, out _));
        CollectionAssert.AreEqual(scores, plain);
    }

    [Test]
    public void A_lone_weak_answer_is_one_when_normalised_and_small_when_raw()
    {
        Assert.IsTrue(JevResponseParser.TryParse(Answers(0, 0, 0, 0.1, 0, 0, 0), Classes,
                                                 out var scores, out var raw, out _));

        Assert.AreEqual(1f, scores[3], 1e-5f);
        Assert.AreEqual(0.1f, raw[3], 1e-5f);
    }

    [Test]
    public void Action_unit_names_follow_the_enum_values_and_skip_negative_members()
    {
        CollectionAssert.AreEqual(new[] { "First", "Second", "Third", "Max" }, JevClient.AuNamesOf(typeof(Ordered)));
        CollectionAssert.AreEqual(new[] { "First", "Second" }, JevClient.AuNamesOf(typeof(Scrambled)));
    }

    [Test]
    public void Calibration_scores_for_jev_have_no_zeros_and_keep_their_order()
    {
        var scores = JevScores.ForCalibration(new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f });

        Assert.AreEqual(1f, scores.Sum(), 1e-5f);
        Assert.AreEqual(3, Utils.Argmax(scores));
        Assert.IsTrue(scores.All(s => s > 0.009f));
        Assert.IsNull(JevScores.ForCalibration(null));
    }

    [Test]
    public void A_calibration_is_tied_to_the_model_it_was_made_with()
    {
        Assert.AreEqual("local-model", NewFaceAuModel.CalibrationModelId(PredictionBackend.LocalModel, "local-model", "jev-1"));
        Assert.AreEqual("jev:jev-1", NewFaceAuModel.CalibrationModelId(PredictionBackend.Jev, "local-model", "jev-1"));
        Assert.AreNotEqual(NewFaceAuModel.CalibrationModelId(PredictionBackend.Jev, "m", "jev-1"),
                           NewFaceAuModel.CalibrationModelId(PredictionBackend.Jev, "m", "jev-2"));
    }

    [Test]
    public void Request_refuses_a_profile_that_does_not_match_the_emotions()
    {
        var centroids = new[] { new double[CalibrationMath.FaceActionUnitCount] };
        var wrongRows = new CalibrationProfile("test", Classes, centroids, centroids);

        Assert.Throws<ArgumentException>(() => JevRequestBuilder.Build("m", Window(0), AuNames, Classes, wrongRows));
    }

    [Test]
    public void Preflight_names_a_missing_settings_asset_and_accepts_a_key()
    {
        Assert.IsNotEmpty(JevClient.Preflight(null));

        var settings = UnityEngine.ScriptableObject.CreateInstance<JevSettings>();
        settings.ApiKey = "test-key";
        try
        {
            Assert.IsNull(JevClient.Preflight(settings));
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(settings);
        }
    }

    [Test]
    public void ScoreOf_is_the_raw_answer_when_there_is_one()
    {
        var scores = new[] { 0.4f, 0.6f };
        var raw = new[] { 0.1f, 0.3f };

        Assert.AreEqual(0.3f, new FacePrediction(Emotion.Disgust, 0.3f, scores, null, 0, raw).ScoreOf(Emotion.Disgust));
        Assert.AreEqual(0.6f, new FacePrediction(Emotion.Disgust, 0.6f, scores, null, 0).ScoreOf(Emotion.Disgust));
    }

    [Test]
    public void A_result_remembers_the_answers_and_whether_a_profile_was_sent()
    {
        var result = JevResult.Success(new[] { 1f }, new[] { 0.5f }).WithProfileSent(true);

        Assert.IsTrue(result.Ok);
        Assert.IsTrue(result.ProfileSent);
        CollectionAssert.AreEqual(new[] { 0.5f }, result.Raw);
        Assert.IsFalse(JevResult.Failure("x").Ok);
        Assert.IsFalse(JevResult.Success(new[] { 1f }).ProfileSent);
    }
}

using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VcuSwimLab.Backends.Jev;
using VcuSwimLab.Calibration;

public class JevRequestTests
{
    private static readonly string[] Classes = JevTestData.Classes;
    private static readonly string[] AuNames = JevTestData.AuNames;
    private const int Block = JevTestData.Block;

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
        var request = JevTestData.Build(JevTestData.Window(3), null);

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
        var face = JevTestData.Build(JevTestData.Window(2), null)["state"]["current_face"];
        var active = face["active_aus"].Select(t => (string)t).ToArray();

        Assert.AreEqual("high", (string)face["overall_activity"]);
        Assert.AreEqual(JevRequestBuilder.ActiveAusShown, active.Length);
        Assert.AreEqual($"AU{2 * Block} (strong)", active[0]);
    }

    [Test]
    public void Request_with_calibration_describes_each_emotion_in_order()
    {
        const int shown = 3;
        var window = JevTestData.Window(shown);
        var profile = JevTestData.Profile();

        var faces = (JObject)JevTestData.Build(window, profile)["state"]["players_own_faces"];

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
    public void Request_refuses_a_profile_that_does_not_match_the_emotions()
    {
        var centroids = new[] { new double[CalibrationMath.FaceActionUnitCount] };
        var wrongRows = new CalibrationProfile("test", Classes, centroids, centroids);

        Assert.Throws<ArgumentException>(() => JevRequestBuilder.Build("m", JevTestData.Window(0), AuNames, Classes, wrongRows));
    }

    [Test]
    public void Calibration_scores_for_jev_have_no_zeros_and_keep_their_order()
    {
        var scores = JevScores.ForCalibration(new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f });

        Assert.AreEqual(1f, scores.Sum(), 1e-5f);
        Assert.AreEqual(3, VcuSwimLab.Backends.ScoreValidation.Argmax(scores));
        Assert.IsTrue(scores.All(s => s > 0.009f));
        Assert.IsNull(JevScores.ForCalibration(null));
    }
}

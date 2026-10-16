using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VcuSwimLab.Backends;
using VcuSwimLab.Backends.Jev;

public class JevResponseTests
{
    private static readonly string[] Classes = JevTestData.Classes;

    [Test]
    public void Parse_normalises_the_seven_answers_to_probabilities()
    {
        Assert.IsTrue(JevResponseParser.TryParse(JevTestData.Answers(0.1, 0.1, 0.1, 0.5, 0.1, 0.05, 0.05), Classes,
                                                 out var scores, out _, out var error), error);

        Assert.AreEqual(1f, scores.Sum(), 1e-5f);
        Assert.AreEqual(0.5f, scores[3], 1e-5f);
        Assert.AreEqual(3, ScoreValidation.Argmax(scores));
    }

    [Test]
    public void Parse_keeps_jevs_own_answers_next_to_the_normalised_scores()
    {
        Assert.IsTrue(JevResponseParser.TryParse(JevTestData.Answers(0.2, 0, 0, 0.6, 0, 0, 0), Classes,
                                                 out var scores, out var raw, out var error), error);

        Assert.AreEqual(0.25f, scores[0], 1e-5f);
        Assert.AreEqual(0.75f, scores[3], 1e-5f);
        Assert.AreEqual(0.2f, raw[0], 1e-5f);
        Assert.AreEqual(0.6f, raw[3], 1e-5f);
    }

    [Test]
    public void A_lone_weak_answer_is_one_when_normalised_and_small_when_raw()
    {
        Assert.IsTrue(JevResponseParser.TryParse(JevTestData.Answers(0, 0, 0, 0.1, 0, 0, 0), Classes,
                                                 out var scores, out var raw, out _));

        Assert.AreEqual(1f, scores[3], 1e-5f);
        Assert.AreEqual(0.1f, raw[3], 1e-5f);
    }

    [Test]
    public void Parse_refuses_missing_negative_and_all_zero_answers()
    {
        var missing = JObject.Parse(JevTestData.Answers(0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1));
        ((JObject)missing["answers"]).Remove("Surprise");

        Assert.IsFalse(JevResponseParser.TryParse(missing.ToString(), Classes, out _, out _, out var error));
        StringAssert.Contains("Surprise", error);

        Assert.IsFalse(JevResponseParser.TryParse(JevTestData.Answers(0.1, 0.1, 0.1, -0.2, 0.1, 0.1, 0.1), Classes,
                                                  out _, out _, out error));
        StringAssert.Contains("Happiness", error);

        Assert.IsFalse(JevResponseParser.TryParse(JevTestData.Answers(0, 0, 0, 0, 0, 0, 0), Classes, out _, out _, out error));
        StringAssert.Contains("no emotion", error);
    }

    [Test]
    public void Parse_refuses_text_that_is_not_a_response()
    {
        Assert.IsFalse(JevResponseParser.TryParse("<html>", Classes, out var scores, out var raw, out var error));
        Assert.IsNull(scores);
        Assert.IsNull(raw);
        Assert.IsNotEmpty(error);
        Assert.IsFalse(JevResponseParser.TryParse("{}", Classes, out _, out _, out _));
        Assert.IsFalse(JevResponseParser.TryParse(null, Classes, out _, out _, out _));
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
    public void A_long_error_body_is_cut_short()
    {
        string message = JevResponseParser.FailureMessage(500, new string('x', 5000));

        Assert.Less(message.Length, 300);
    }

    [TestCase(0, PredictionErrorCode.Unreachable)]
    [TestCase(408, PredictionErrorCode.Timeout)]
    [TestCase(429, PredictionErrorCode.RateLimited)]
    [TestCase(401, PredictionErrorCode.Unauthorized)]
    [TestCase(403, PredictionErrorCode.Unauthorized)]
    [TestCase(400, PredictionErrorCode.InvalidRequest)]
    [TestCase(404, PredictionErrorCode.InvalidRequest)]
    [TestCase(422, PredictionErrorCode.InvalidRequest)]
    [TestCase(500, PredictionErrorCode.ServerError)]
    [TestCase(529, PredictionErrorCode.ServerError)]
    [TestCase(302, PredictionErrorCode.InvalidResponse)]
    public void Each_status_maps_to_one_error_code(int status, PredictionErrorCode expected)
    {
        Assert.AreEqual(expected, JevResponseParser.CodeFor(status));
    }
}

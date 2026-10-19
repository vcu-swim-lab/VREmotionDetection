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

    // Since 3.0.0 an answer above 1 (or below 0, or not a number) is an InvalidResponse, so confidence can never exceed 1.
    // Note: the bound is exact - 1.0000001 is refused too (no tolerance).
    [TestCase(1.0000001)]
    [TestCase(1.0001)]
    [TestCase(1.5)]
    [TestCase(2)]
    [TestCase(1e9)]
    [TestCase(-0.0001)]
    public void An_answer_outside_zero_to_one_is_refused_and_names_the_emotion(double bad)
    {
        Assert.IsFalse(JevResponseParser.TryParse(JevTestData.Answers(0.1, 0.1, 0.1, bad, 0.1, 0.1, 0.1), Classes,
                                                  out var scores, out var raw, out var error));

        Assert.IsNull(scores);
        Assert.IsNull(raw);
        StringAssert.Contains("invalid answer", error);
        StringAssert.Contains("Happiness", error);
    }

    [TestCase(0.0)]
    [TestCase(1.0)]
    public void The_boundaries_zero_and_one_are_accepted(double edge)
    {
        Assert.IsTrue(JevResponseParser.TryParse(JevTestData.Answers(0.5, 0.5, 0.5, edge, 0.5, 0.5, 0.5), Classes,
                                                 out var scores, out var raw, out var error), error);

        Assert.AreEqual((float)edge, raw[3], 1e-6f);
        Assert.AreEqual(1f, scores.Sum(), 1e-5f);
    }

    [Test]
    public void Whole_numbers_are_numbers_too_so_1_is_accepted_and_2_is_refused()
    {
        string With(int value)
        {
            var json = JObject.Parse(JevTestData.Answers(0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1));
            ((JObject)json["answers"]["Happiness"])["noul"] = value;
            return json.ToString();
        }

        Assert.IsTrue(JevResponseParser.TryParse(With(1), Classes, out _, out _, out var error), error);
        Assert.IsFalse(JevResponseParser.TryParse(With(2), Classes, out _, out _, out error));
        StringAssert.Contains("Happiness", error);
    }

    [TestCase("\"high\"")]
    [TestCase("\"NaN\"")]
    [TestCase("null")]
    [TestCase("true")]
    [TestCase("[1]")]
    [TestCase("{}")]
    public void An_answer_that_is_not_a_number_is_refused(string token)
    {
        var json = JObject.Parse(JevTestData.Answers(0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1));
        ((JObject)json["answers"]["Happiness"])["noul"] = JToken.Parse(token);

        Assert.IsFalse(JevResponseParser.TryParse(json.ToString(), Classes, out var scores, out _, out var error));

        Assert.IsNull(scores);
        StringAssert.Contains("Happiness", error);
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

    private const string Key = "sk-test-key-1234567890";

    // Since 3.0 the service's own text is left out of an error unless JevOptions.IncludeServerDetails is on.
    [Test]
    public void Failure_messages_name_the_cause_and_leave_the_servers_text_out_by_default()
    {
        string unauthorized = JevResponseParser.FailureMessage(401, "{\"detail\":\"Invalid key\"}", Key, false);
        StringAssert.Contains("API key", unauthorized);
        StringAssert.DoesNotContain("Invalid key", unauthorized);

        string invalid = JevResponseParser.FailureMessage(
            422, "{\"detail\":[{\"loc\":[\"body\",\"state\"],\"msg\":\"Field required\",\"type\":\"missing\"}]}", Key, false);
        StringAssert.Contains("rejected the request", invalid);
        StringAssert.DoesNotContain("Field required", invalid);

        StringAssert.Contains("rate limit", JevResponseParser.FailureMessage(429, "", Key, false));
        StringAssert.Contains("timed out", JevResponseParser.FailureMessage(408, "", Key, false));
        StringAssert.Contains("overloaded", JevResponseParser.FailureMessage(529, "", Key, false));

        string server = JevResponseParser.FailureMessage(500, "boom", Key, false);
        StringAssert.Contains("500", server);
        StringAssert.DoesNotContain("boom", server);

        StringAssert.DoesNotContain("nested", JevResponseParser.FailureMessage(400, "{\"error\":{\"message\":\"nested\"}}", Key, false));
    }

    [Test]
    public void Failure_messages_keep_the_servers_detail_when_asked()
    {
        StringAssert.Contains("API key", JevResponseParser.FailureMessage(401, "{\"detail\":\"Invalid key\"}", Key, true));
        StringAssert.Contains("Invalid key", JevResponseParser.FailureMessage(401, "{\"detail\":\"Invalid key\"}", Key, true));
        StringAssert.Contains("Field required", JevResponseParser.FailureMessage(
            422, "{\"detail\":[{\"loc\":[\"body\",\"state\"],\"msg\":\"Field required\",\"type\":\"missing\"}]}", Key, true));
        StringAssert.Contains("rate limit", JevResponseParser.FailureMessage(429, "", Key, true));
        StringAssert.Contains("500", JevResponseParser.FailureMessage(500, "boom", Key, true));
        StringAssert.Contains("boom", JevResponseParser.FailureMessage(500, "boom", Key, true));
        StringAssert.Contains("nested", JevResponseParser.FailureMessage(400, "{\"error\":{\"message\":\"nested\"}}", Key, true));
    }

    [Test]
    public void A_failed_connection_always_carries_the_transport_text_which_is_cleaned_of_the_key()
    {
        string message = JevResponseParser.FailureMessage(0, "Cannot connect to destination host " + Key, Key, false);

        StringAssert.Contains("no response", message);
        StringAssert.Contains("Cannot connect to destination host", message);
        StringAssert.DoesNotContain(Key, message);
    }

    [Test]
    public void The_servers_detail_never_contains_the_key_even_when_included()
    {
        string message = JevResponseParser.FailureMessage(500, "{\"error\":\"bad " + Key + " here\"}", Key, true);

        StringAssert.Contains("bad", message);
        StringAssert.DoesNotContain(Key, message);
    }

    [Test]
    public void A_long_error_body_is_cut_short()
    {
        string message = JevResponseParser.FailureMessage(500, new string('x', 5000), Key, true);

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

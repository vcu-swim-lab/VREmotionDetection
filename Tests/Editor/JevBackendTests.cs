using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using VcuSwimLab.Backends;
using VcuSwimLab.Backends.Jev;

public class JevBackendTests
{
    private const string Key = "test-key-123";

    private sealed class FakeTransport : IHttpTransport
    {
        private readonly Queue<HttpReply> replies = new Queue<HttpReply>();

        public readonly List<(string url, string token, string body, float timeout)> Calls =
            new List<(string url, string token, string body, float timeout)>();

        public FakeTransport Reply(long status, string body, float? retryAfter = null)
        {
            replies.Enqueue(new HttpReply(status, body, retryAfter));
            return this;
        }

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds)
        {
            Calls.Add((url, bearerToken, jsonBody, timeoutSeconds));
            return replies.Dequeue();
        }
    }

    private static JevBackend Backend(FakeTransport transport, Action<JevOptions> configure = null)
    {
        var options = new JevOptions(Key) { MaxRetries = 0 };
        configure?.Invoke(options);
        return new JevBackend(options, JevTestData.AuNames, transport);
    }

    private static BackendRequest Request(bool withProfile = false, int face = 3)
    {
        return new BackendRequest(JevTestData.Window(face), JevTestData.Classes, withProfile ? JevTestData.Profile() : null);
    }

    [Test]
    public void A_good_answer_gives_normalised_scores_and_the_raw_answers()
    {
        var transport = new FakeTransport().Reply(200, JevTestData.Answers(0.2, 0, 0, 0.6, 0, 0, 0));

        var result = JevTestData.Complete(Backend(transport).ScoreAsync(Request()));

        Assert.IsTrue(result.Ok);
        Assert.AreEqual(0.75f, result.Scores[3], 1e-5f);
        Assert.AreEqual(0.6f, result.Raw[3], 1e-5f);
        Assert.IsNull(result.Error);
    }

    [Test]
    public void The_request_goes_to_the_documented_endpoint_with_the_bearer_key()
    {
        var transport = new FakeTransport().Reply(200, JevTestData.Answers(0.9, 0, 0, 0, 0, 0, 0));

        JevTestData.Complete(Backend(transport, o => o.BaseUrl = "https://proxy.example.com/typesafe/").ScoreAsync(Request()));

        Assert.AreEqual(1, transport.Calls.Count);
        Assert.AreEqual("https://proxy.example.com/typesafe/v1/systemone", transport.Calls[0].url);
        Assert.AreEqual(Key, transport.Calls[0].token);
        Assert.AreEqual(JevOptions.DefaultTimeoutSeconds, transport.Calls[0].timeout);

        var body = JObject.Parse(transport.Calls[0].body);
        Assert.AreEqual("jev-1.13.0", (string)body["model"]);
        Assert.AreEqual(7, ((JObject)body["questions"]).Count);
        StringAssert.DoesNotContain(Key, transport.Calls[0].body);
    }

    [Test]
    public void The_players_own_faces_are_sent_only_when_asked_for()
    {
        var withoutFaces = new FakeTransport().Reply(200, JevTestData.Answers(0.9, 0, 0, 0, 0, 0, 0));
        var plain = JevTestData.Complete(Backend(withoutFaces).ScoreAsync(Request(withProfile: true)));

        Assert.IsFalse(plain.ReferenceFacesSent);
        Assert.IsNull(JObject.Parse(withoutFaces.Calls[0].body)["state"]["players_own_faces"]);

        var withFaces = new FakeTransport().Reply(200, JevTestData.Answers(0.9, 0, 0, 0, 0, 0, 0));
        var referenced = JevTestData.Complete(
            Backend(withFaces, o => o.SendOwnFacesInRequest = true).ScoreAsync(Request(withProfile: true)));

        Assert.IsTrue(referenced.ReferenceFacesSent);
        Assert.IsNotNull(JObject.Parse(withFaces.Calls[0].body)["state"]["players_own_faces"]);
    }

    [TestCase(401, PredictionErrorCode.Unauthorized)]
    [TestCase(403, PredictionErrorCode.Unauthorized)]
    [TestCase(422, PredictionErrorCode.InvalidRequest)]
    [TestCase(429, PredictionErrorCode.RateLimited)]
    [TestCase(503, PredictionErrorCode.ServerError)]
    [TestCase(408, PredictionErrorCode.Timeout)]
    [TestCase(0, PredictionErrorCode.Unreachable)]
    public void A_failed_call_returns_a_typed_error_and_no_scores(int status, PredictionErrorCode expected)
    {
        var transport = new FakeTransport().Reply(status, "{\"detail\":\"nope\"}");

        var result = JevTestData.Complete(Backend(transport).ScoreAsync(Request()));

        Assert.IsFalse(result.Ok);
        Assert.IsNull(result.Scores);
        Assert.AreEqual(expected, result.Error.Value.Code);
        Assert.AreEqual("jev:jev-1.13.0:p1", result.Error.Value.BackendId);
        StringAssert.DoesNotContain(Key, result.Error.Value.ToString());
    }

    [Test]
    public void An_unusable_answer_is_an_invalid_response()
    {
        var garbage = JevTestData.Complete(Backend(new FakeTransport().Reply(200, "<html>")).ScoreAsync(Request()));
        var nothing = JevTestData.Complete(
            Backend(new FakeTransport().Reply(200, JevTestData.Answers(0, 0, 0, 0, 0, 0, 0))).ScoreAsync(Request()));

        Assert.AreEqual(PredictionErrorCode.InvalidResponse, garbage.Error.Value.Code);
        Assert.AreEqual(PredictionErrorCode.InvalidResponse, nothing.Error.Value.Code);
    }

    [Test]
    public void A_bad_window_is_rejected_before_any_network_call()
    {
        var transport = new FakeTransport();
        var shortFrames = new BackendRequest(new[] { new float[10] }, JevTestData.Classes);
        var noFrames = new BackendRequest(null, JevTestData.Classes);
        var noClasses = new BackendRequest(JevTestData.Window(0), new string[0]);

        foreach (var request in new[] { shortFrames, noFrames, noClasses })
        {
            var result = JevTestData.Complete(Backend(transport).ScoreAsync(request));
            Assert.AreEqual(PredictionErrorCode.InvalidInput, result.Error.Value.Code);
        }

        Assert.AreEqual(0, transport.Calls.Count);
    }

    [Test]
    public void The_transport_failing_is_reported_not_thrown()
    {
        var result = JevTestData.Complete(Backend(new FakeTransport()).ScoreAsync(Request()));

        Assert.AreEqual(PredictionErrorCode.Internal, result.Error.Value.Code);
    }

    [Test]
    public void The_id_names_the_model_and_the_prompt_version()
    {
        Assert.AreEqual("jev:jev-1.13.0:p1", Backend(new FakeTransport()).Id);
        Assert.AreEqual("jev:other:p1", Backend(new FakeTransport(), o => o.Model = " other ").Id);
    }

    [Test]
    public void Calibration_scores_get_the_floor_so_zeros_cannot_dominate()
    {
        var scores = Backend(new FakeTransport()).PrepareForCalibration(new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f });

        Assert.IsTrue(scores.All(s => s > 0.009f));
        Assert.AreEqual(1f, scores.Sum(), 1e-5f);
    }

    [Test]
    public void Construction_is_validated()
    {
        Assert.Throws<ArgumentNullException>(() => new JevBackend(null, JevTestData.AuNames));
        Assert.Throws<ArgumentNullException>(() => new JevBackend(new JevOptions(Key), null));
        Assert.Throws<ArgumentException>(() => new JevBackend(new JevOptions(Key), JevTestData.AuNames.Take(10).ToArray()));
        Assert.Throws<ArgumentException>(() => new JevBackend(new JevOptions(Key) { BaseUrl = "http://remote.example.com" }, JevTestData.AuNames));
    }

    [Test]
    public void Only_transient_failures_are_retried()
    {
        foreach (long status in new long[] { 0, 408, 429, 500, 503, 529 })
        {
            Assert.IsTrue(JevBackend.IsTransient(status), status.ToString());
        }

        foreach (long status in new long[] { 400, 401, 403, 404, 422 })
        {
            Assert.IsFalse(JevBackend.IsTransient(status), status.ToString());
        }
    }

    [Test]
    public void Backoff_doubles_is_capped_and_honours_retry_after()
    {
        Assert.AreEqual(0.5f, JevBackend.BackoffSeconds(0), 1e-6f);
        Assert.AreEqual(1f, JevBackend.BackoffSeconds(1), 1e-6f);
        Assert.AreEqual(2f, JevBackend.BackoffSeconds(2), 1e-6f);
        Assert.AreEqual(JevBackend.MaxBackoffSeconds, JevBackend.BackoffSeconds(20), 1e-6f);
        Assert.AreEqual(7f, JevBackend.BackoffSeconds(0, 7f), 1e-6f);
        Assert.AreEqual(JevBackend.MaxRetryAfterSeconds, JevBackend.BackoffSeconds(0, 600f), 1e-6f);
        Assert.AreEqual(0.5f, JevBackend.BackoffSeconds(0, 0.1f), 1e-6f);
    }
}

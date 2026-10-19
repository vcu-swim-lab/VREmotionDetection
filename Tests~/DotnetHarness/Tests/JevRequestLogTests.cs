using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using VcuSwimLab.Backends;
using VcuSwimLab.Backends.Jev;

// JevOptions.LogRequests: one Console line per request (status, time, attempt), without the key.
public class JevRequestLogTests
{
    private const string Key = "log-test-key-98765";

    private sealed class Replies : IHttpTransport
    {
        private readonly Queue<HttpReply> queue = new Queue<HttpReply>();

        public Replies Add(long status, string body)
        {
            queue.Enqueue(new HttpReply(status, body));
            return this;
        }

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds) =>
            queue.Dequeue();
    }

    private static readonly string Good = JevTestData.Answers(0.9, 0, 0, 0, 0, 0, 0);

    [SetUp]
    public void SetUp()
    {
        lock (Debug.Logs) Debug.Logs.Clear();
    }

    private static string[] JevLines()
    {
        lock (Debug.Logs) return Debug.Logs.Where(l => l.StartsWith("[Jev]")).ToArray();
    }

    private static async Task<BackendResult> Score(IHttpTransport transport, System.Action<JevOptions> configure)
    {
        var options = new JevOptions(Key) { MaxRetries = 2 };
        configure(options);
        using var backend = new JevBackend(options, JevTestData.AuNames, transport);
        return await backend.ScoreAsync(new BackendRequest(JevTestData.Window(0), JevTestData.Classes, null));
    }

    [Test]
    public async Task Nothing_is_logged_unless_asked_for()
    {
        await Score(new Replies().Add(200, Good), o => { });

        CollectionAssert.IsEmpty(JevLines());
    }

    [Test]
    public async Task One_line_per_request_names_the_url_status_time_and_attempt()
    {
        await Score(new Replies().Add(200, Good), o => o.LogRequests = true);

        var lines = JevLines();
        Assert.AreEqual(1, lines.Length);
        StringAssert.StartsWith("[Jev] POST https://api.typesafe.ai/v1/systemone -> 200 in ", lines[0]);
        StringAssert.EndsWith(" ms (attempt 1 of 3)", lines[0]);
    }

    [Test]
    public async Task Every_attempt_of_a_retried_request_is_logged()
    {
        var result = await Score(new Replies().Add(503, "overloaded").Add(200, Good), o => o.LogRequests = true);

        Assert.IsTrue(result.Ok);
        var lines = JevLines();
        Assert.AreEqual(2, lines.Length);
        StringAssert.Contains("-> 503 in", lines[0]);
        StringAssert.Contains("(attempt 1 of 3)", lines[0]);
        StringAssert.Contains("-> 200 in", lines[1]);
        StringAssert.Contains("(attempt 2 of 3)", lines[1]);
    }

    [Test]
    public async Task A_failed_request_is_logged_with_its_reason_but_not_the_servers_text()
    {
        await Score(new Replies().Add(401, "{\"detail\":\"Invalid key " + Key + "\"}"), o => { o.LogRequests = true; o.MaxRetries = 0; });

        var line = JevLines().Single();
        StringAssert.Contains("-> 401 in", line);
        StringAssert.Contains("API key is missing or invalid", line);
        StringAssert.DoesNotContain("Invalid key", line);
        StringAssert.DoesNotContain(Key, line);
    }

    [Test]
    public async Task The_servers_text_is_added_only_with_IncludeServerDetails_and_is_still_cleaned_of_the_key()
    {
        await Score(new Replies().Add(401, "{\"detail\":\"Invalid key " + Key + "\"}"),
                    o => { o.LogRequests = true; o.MaxRetries = 0; o.IncludeServerDetails = true; });

        var line = JevLines().Single();
        StringAssert.Contains("Invalid key", line);
        StringAssert.DoesNotContain(Key, line);
    }

    [Test]
    public async Task A_request_that_got_no_answer_is_logged_with_the_transport_text_cleaned_of_the_key()
    {
        await Score(new Replies().Add(0, "Cannot connect to destination host " + Key),
                    o => { o.LogRequests = true; o.MaxRetries = 0; });

        var line = JevLines().Single();
        StringAssert.Contains("-> 0 in", line);
        StringAssert.Contains("Cannot connect to destination host", line);
        StringAssert.DoesNotContain(Key, line);
    }
}

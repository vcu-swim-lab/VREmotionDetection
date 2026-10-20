#if NEW_API
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.Networking;
using VcuSeCenter.Backends.Jev;

public class TransportTests
{
    private string lastBody;
    private string lastAuthorization;

    [SetUp]
    public void SetUp()
    {
        UnityWebRequest.ForceDataProcessingError = false;
        UnityWebRequest.ResponseHeaders = new System.Collections.Generic.Dictionary<string, string>();
        UnityWebRequest.Handler = (url, body, headers) =>
        {
            lastBody = body;
            lastAuthorization = headers["Authorization"];
            return (200, "{\"ok\":true}");
        };
    }

    [TearDown]
    public void TearDown()
    {
        UnityWebRequest.ForceDataProcessingError = false;
    }

    private static async Task<HttpReply> Post() =>
        await new UnityWebRequestTransport().PostJsonAsync("https://example.test/v1/systemone", "token-1", "{\"a\":1}", 5f);

    [Test]
    public async Task A_good_reply_carries_the_status_and_body_and_the_request_carries_the_bearer_token()
    {
        var reply = await Post();

        Assert.AreEqual(200, reply.StatusCode);
        Assert.AreEqual("{\"ok\":true}", reply.Body);
        Assert.AreEqual("{\"a\":1}", lastBody);
        Assert.AreEqual("Bearer token-1", lastAuthorization);
        Assert.IsNull(reply.RetryAfterSeconds);
    }

    [Test]
    public async Task An_error_status_keeps_its_body_and_a_retry_after_header()
    {
        UnityWebRequest.Handler = (url, body, headers) => (429, "{\"detail\":\"slow down\"}");
        UnityWebRequest.ResponseHeaders["Retry-After"] = "3.5";

        var reply = await Post();

        Assert.AreEqual(429, reply.StatusCode);
        StringAssert.Contains("slow down", reply.Body);
        Assert.AreEqual(3.5f, reply.RetryAfterSeconds.Value, 1e-6f);
    }

    [Test]
    public async Task A_retry_after_that_is_not_a_number_is_ignored()
    {
        UnityWebRequest.Handler = (url, body, headers) => (503, "busy");
        UnityWebRequest.ResponseHeaders["Retry-After"] = "Wed, 21 Oct 2026 07:28:00 GMT";

        Assert.IsNull((await Post()).RetryAfterSeconds);
    }

    [Test]
    public async Task No_response_is_status_zero_and_a_timeout_is_status_408()
    {
        UnityWebRequest.Handler = (url, body, headers) => (0, "Cannot resolve destination host");
        var offline = await Post();

        UnityWebRequest.Handler = (url, body, headers) => (0, "Request timeout");
        var slow = await Post();

        Assert.AreEqual(0, offline.StatusCode);
        StringAssert.Contains("Cannot resolve", offline.Body);
        Assert.AreEqual(408, slow.StatusCode);
    }

    [Test]
    public async Task A_download_that_failed_after_the_headers_is_not_taken_for_success()
    {
        UnityWebRequest.ForceDataProcessingError = true;

        var reply = await Post();

        Assert.AreNotEqual(200, reply.StatusCode);
        Assert.AreEqual(0, reply.StatusCode);
        StringAssert.Contains("Failed to receive data", reply.Body);
    }
}
#endif

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Networking;
using VcuSeCenter.Backends;
using VcuSeCenter.Backends.Jev;

// PredictionErrorCode.Canceled, ICancelableBackend and ICancelableHttpTransport: a request that is still waiting when
// the predictor switches backend or is disabled is aborted and ends as Canceled (added after the baseline).
public class JevCancellationTests
{
    private const string Key = "cancel-test-key-4711";

    private static readonly string Good = JevTestData.Answers(0.9, 0, 0, 0, 0, 0, 0);

    // A transport that understands cancellation: it waits until its token is canceled, as UnityWebRequestTransport does.
    private sealed class HoldingTransport : ICancelableHttpTransport
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public CancellationToken LastToken;

        public Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds) =>
            PostJsonAsync(url, bearerToken, jsonBody, timeoutSeconds, CancellationToken.None);

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds,
                                                        CancellationToken cancellationToken)
        {
            LastToken = cancellationToken;
            Interlocked.Increment(ref calls);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpReply(200, Good);
        }
    }

    // A transport that cannot be told to stop: the reply simply arrives later.
    private sealed class GatedTransport : IHttpTransport
    {
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public readonly TaskCompletionSource<HttpReply> Gate = new TaskCompletionSource<HttpReply>();

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds)
        {
            Interlocked.Increment(ref calls);
            return await Gate.Task;
        }
    }

    private sealed class Replies : IHttpTransport
    {
        private readonly Queue<HttpReply> queue = new Queue<HttpReply>();

        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Replies Add(long status, string body)
        {
            queue.Enqueue(new HttpReply(status, body));
            return this;
        }

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds)
        {
            Interlocked.Increment(ref calls);
            return queue.Dequeue();
        }
    }

    private static JevBackend Backend(IHttpTransport transport, int retries = 0) =>
        new JevBackend(new JevOptions(Key) { MaxRetries = retries }, JevTestData.AuNames, transport);

    private static BackendRequest Request() => new BackendRequest(JevTestData.Window(2), JevTestData.Classes, null);

    private static async Task WaitUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition())
        {
            Assert.Less(clock.ElapsedMilliseconds, 3000, "timed out waiting for the request to start");
            await Task.Delay(5);
        }
    }

    [Test]
    public async Task Canceling_a_request_in_flight_ends_it_as_Canceled_and_tells_a_cancelable_transport()
    {
        var transport = new HoldingTransport();
        using var backend = Backend(transport);

        var pending = backend.ScoreAsync(Request());
        await WaitUntil(() => transport.Calls == 1);
        Assert.IsFalse(transport.LastToken.IsCancellationRequested);

        backend.CancelPending();
        var result = await pending;

        Assert.IsFalse(result.Ok);
        Assert.AreEqual(PredictionErrorCode.Canceled, result.Error.Value.Code);
        Assert.IsFalse(result.Error.Value.IsTransient, "a canceled request must not be retried or counted as an outage");
        Assert.IsTrue(transport.LastToken.IsCancellationRequested);
        Assert.AreEqual(1, transport.Calls);
    }

    [Test]
    public async Task A_reply_that_arrives_after_a_cancel_is_dropped_even_when_the_transport_cannot_be_stopped()
    {
        var transport = new GatedTransport();
        using var backend = Backend(transport);

        var pending = backend.ScoreAsync(Request());
        await WaitUntil(() => transport.Calls == 1);

        backend.CancelPending();
        transport.Gate.SetResult(new HttpReply(200, Good));
        var result = await pending;

        Assert.IsFalse(result.Ok, "a success from before the cancel must not be reported");
        Assert.AreEqual(PredictionErrorCode.Canceled, result.Error.Value.Code);
    }

    [Test]
    public async Task Canceling_during_the_wait_before_a_retry_stops_the_retries()
    {
        var transport = new Replies().Add(503, "overloaded").Add(200, Good);
        using var backend = Backend(transport, retries: 2);

        var clock = Stopwatch.StartNew();
        var pending = backend.ScoreAsync(Request());
        await WaitUntil(() => transport.Calls == 1);
        await Task.Delay(100);

        backend.CancelPending();
        var result = await pending;

        Assert.AreEqual(PredictionErrorCode.Canceled, result.Error.Value.Code);
        Assert.AreEqual(1, transport.Calls, "no second attempt after the cancel");
        Assert.Less(clock.ElapsedMilliseconds, JevBackend.InitialBackoffSeconds * 1000, "the backoff wait was cut short");
    }

    [Test]
    public async Task Disposing_the_backend_cancels_a_request_in_flight()
    {
        var transport = new HoldingTransport();
        var backend = Backend(transport);

        var pending = backend.ScoreAsync(Request());
        await WaitUntil(() => transport.Calls == 1);

        backend.Dispose();
        var result = await pending;

        Assert.AreEqual(PredictionErrorCode.Canceled, result.Error.Value.Code);
        Assert.DoesNotThrow(backend.Dispose, "disposing twice is safe");
    }

    [Test]
    public async Task A_cancel_only_affects_requests_in_flight_not_later_ones()
    {
        var transport = new Replies().Add(200, Good).Add(200, Good);
        using var backend = Backend(transport);

        Assert.DoesNotThrow(backend.CancelPending, "nothing is pending");
        backend.CancelPending();

        Assert.IsTrue((await backend.ScoreAsync(Request())).Ok);
        backend.CancelPending();
        Assert.IsTrue((await backend.ScoreAsync(Request())).Ok, "the backend is reusable after a cancel");
    }

    [Test]
    public void A_backend_that_can_be_canceled_says_so_through_the_interface()
    {
        using var backend = Backend(new Replies());

        Assert.IsInstanceOf<ICancelableBackend>(backend);
    }

    [Test]
    public async Task The_web_request_transport_aborts_the_request_when_its_token_is_canceled()
    {
        var never = new TaskCompletionSource<bool>();
        UnityWebRequest.Handler = (url, body, headers) => (200, "{}");
        UnityWebRequest.ForceDataProcessingError = false;
        UnityWebRequest.AbortCalls = 0;
        UnityWebRequest.Gate = never.Task;
        try
        {
            using var cts = new CancellationTokenSource();
            var post = new UnityWebRequestTransport().PostJsonAsync("https://example.test/v1/systemone", "k", "{}", 5f, cts.Token);
            Task waiting = Task.Run(async () => await post);
            await Task.Delay(50);
            Assert.AreEqual(0, UnityWebRequest.AbortCalls, "still waiting for the server");
            Assert.IsFalse(waiting.IsCompleted);

            cts.Cancel();

            // Bounded wait: a transport that ignores the token would otherwise block this test forever.
            Assert.AreSame(waiting, await Task.WhenAny(waiting, Task.Delay(3000)), "the transport must stop waiting once the token is canceled");
            Assert.CatchAsync<OperationCanceledException>(async () => await waiting);
            Assert.AreEqual(1, UnityWebRequest.AbortCalls);
        }
        finally
        {
            UnityWebRequest.Gate = null;
        }
    }

    [Test]
    public void The_web_request_transport_does_not_start_a_request_with_a_token_that_is_already_canceled()
    {
        UnityWebRequest.Handler = (url, body, headers) => (200, "{}");
        UnityWebRequest.AbortCalls = 0;
        int urlsBefore;
        lock (UnityWebRequest.Urls) urlsBefore = UnityWebRequest.Urls.Count;

        Assert.CatchAsync<OperationCanceledException>(async () =>
            await new UnityWebRequestTransport().PostJsonAsync("https://example.test/v1/systemone", "k", "{}", 5f, new CancellationToken(true)));

        lock (UnityWebRequest.Urls) Assert.AreEqual(urlsBefore, UnityWebRequest.Urls.Count, "no request was created");
        Assert.AreEqual(0, UnityWebRequest.AbortCalls);
    }
}

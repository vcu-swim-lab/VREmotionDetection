using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using UnityEngine;
using VcuSwimLab.Calibration;

namespace VcuSwimLab.Backends.Jev
{
    public sealed class JevBackend : ICancelableBackend
    {
        public const string SystemOnePath = "/v1/systemone";
        public const float InitialBackoffSeconds = 0.5f;
        public const float MaxBackoffSeconds = 5f;
        public const float MaxRetryAfterSeconds = 10f;

        private readonly IReadOnlyList<string> auNames;
        private readonly IHttpTransport transport;
        private readonly string apiKey;
        private readonly string model;
        private readonly string url;
        private readonly float timeoutSeconds;
        private readonly int maxRetries;
        private readonly bool sendOwnFaces;
        private readonly bool logRequests;
        private readonly bool includeServerDetails;

        private CancellationTokenSource pending = new CancellationTokenSource();

        public JevBackend(JevOptions options, IReadOnlyList<string> movementAuNames, IHttpTransport transport = null)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (movementAuNames == null)
            {
                throw new ArgumentNullException(nameof(movementAuNames));
            }

            if (movementAuNames.Count != CalibrationMath.FaceActionUnitCount)
            {
                throw new ArgumentException(
                    $"expected {CalibrationMath.FaceActionUnitCount} action unit names but got {movementAuNames.Count}",
                    nameof(movementAuNames));
            }

            options.Validate();

            auNames = new List<string>(movementAuNames);
            this.transport = transport ?? new UnityWebRequestTransport();
            apiKey = options.ApiKey;
            model = options.Model.Trim();
            url = options.BaseUrl.Trim().TrimEnd('/') + SystemOnePath;
            timeoutSeconds = options.TimeoutSeconds;
            maxRetries = options.MaxRetries;
            sendOwnFaces = options.SendOwnFacesInRequest;
            logRequests = options.LogRequests;
            includeServerDetails = options.IncludeServerDetails;
            Id = $"jev:{model}:p{JevPrompt.Version}";
        }

        public string Id { get; }

        public float[] PrepareForCalibration(float[] scores) => JevScores.ForCalibration(scores);

        public async Awaitable<BackendResult> ScoreAsync(BackendRequest request)
        {
            var token = Volatile.Read(ref pending).Token;

            try
            {
                if (!WindowValidation.TryValidate(request.Window, 1, CalibrationMath.FaceActionUnitCount, out string problem))
                {
                    return Fail(PredictionErrorCode.InvalidInput, problem);
                }

                if (request.ClassNames == null || request.ClassNames.Count == 0)
                {
                    return Fail(PredictionErrorCode.InvalidInput, "no class names were provided");
                }

                var profile = sendOwnFaces ? request.ReferenceProfile : null;

                string body;
                try
                {
                    body = JevRequestBuilder.Build(model, request.Window, auNames, request.ClassNames, profile);
                }
                catch (ArgumentException e)
                {
                    return Fail(PredictionErrorCode.InvalidInput, e.Message);
                }

                for (int attempt = 0; ; ++attempt)
                {
                    if (token.IsCancellationRequested)
                    {
                        return Canceled();
                    }

                    var clock = Stopwatch.StartNew();
                    var reply = await Post(body, token);

                    if (logRequests)
                    {
                        JevRequestLog.Write(url, reply, clock.ElapsedMilliseconds, attempt, maxRetries + 1, apiKey, includeServerDetails);
                    }

                    if (token.IsCancellationRequested)
                    {
                        return Canceled();
                    }

                    if (reply.StatusCode >= 200 && reply.StatusCode < 300)
                    {
                        if (!JevResponseParser.TryParse(reply.Body, request.ClassNames, out float[] scores, out float[] raw, out string error))
                        {
                            return Fail(PredictionErrorCode.InvalidResponse, error);
                        }

                        return BackendResult.Success(scores, raw, profile != null);
                    }

                    if (attempt >= maxRetries || !IsTransient(reply.StatusCode))
                    {
                        return Fail(
                            JevResponseParser.CodeFor(reply.StatusCode),
                            JevResponseParser.FailureMessage(reply.StatusCode, reply.Body, apiKey, includeServerDetails));
                    }

                    await Awaitable.WaitForSecondsAsync(BackoffSeconds(attempt, reply.RetryAfterSeconds), token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return Canceled();
            }
            catch (Exception e)
            {
                return Fail(PredictionErrorCode.Internal, JevText.Sanitize($"{e.GetType().Name}: {e.Message}", apiKey));
            }
        }

        public void CancelPending()
        {
            try
            {
                Interlocked.Exchange(ref pending, new CancellationTokenSource()).Cancel();
            }
            catch (AggregateException)
            {
            }
        }

        public void Dispose()
        {
            CancelPending();
        }

        internal static bool IsTransient(long statusCode)
        {
            return statusCode == 0 || statusCode == 408 || statusCode == 429 || statusCode >= 500;
        }

        internal static float BackoffSeconds(int attempt, float? retryAfterSeconds = null)
        {
            float backoff = Mathf.Min(MaxBackoffSeconds, InitialBackoffSeconds * Mathf.Pow(2f, attempt));
            return retryAfterSeconds.HasValue
                ? Mathf.Max(backoff, Mathf.Min(retryAfterSeconds.Value, MaxRetryAfterSeconds))
                : backoff;
        }

        private Awaitable<HttpReply> Post(string body, CancellationToken token)
        {
            return transport is ICancelableHttpTransport cancelable
                ? cancelable.PostJsonAsync(url, apiKey, body, timeoutSeconds, token)
                : transport.PostJsonAsync(url, apiKey, body, timeoutSeconds);
        }

        private BackendResult Canceled()
        {
            return Fail(PredictionErrorCode.Canceled, "the Jev request was canceled");
        }

        private BackendResult Fail(PredictionErrorCode code, string message)
        {
            return BackendResult.Failure(code, message, Id);
        }
    }
}

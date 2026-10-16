using System;
using System.Collections.Generic;
using UnityEngine;
using VcuSwimLab.Calibration;

namespace VcuSwimLab.Backends.Jev
{
    public sealed class JevBackend : IEmotionBackend
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
            url = options.BaseUrl.TrimEnd('/') + SystemOnePath;
            timeoutSeconds = options.TimeoutSeconds;
            maxRetries = options.MaxRetries;
            sendOwnFaces = options.SendOwnFacesInRequest;
            Id = $"jev:{model}:p{JevPrompt.Version}";
        }

        public string Id { get; }

        public float[] PrepareForCalibration(float[] scores) => JevScores.ForCalibration(scores);

        public async Awaitable<BackendResult> ScoreAsync(BackendRequest request)
        {
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
                    var reply = await transport.PostJsonAsync(url, apiKey, body, timeoutSeconds);

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
                            JevResponseParser.FailureMessage(reply.StatusCode, reply.Body));
                    }

                    await Awaitable.WaitForSecondsAsync(BackoffSeconds(attempt, reply.RetryAfterSeconds));
                }
            }
            catch (Exception e)
            {
                return Fail(PredictionErrorCode.Internal, $"{e.GetType().Name}: {e.Message}");
            }
        }

        public void Dispose()
        {
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

        private BackendResult Fail(PredictionErrorCode code, string message)
        {
            return BackendResult.Failure(code, message, Id);
        }
    }
}

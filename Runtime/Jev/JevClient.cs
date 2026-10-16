using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using VcuSwimLab.Calibration;

namespace VcuSwimLab.Jev
{
    public readonly struct JevResult
    {
        public readonly float[] Scores;
        public readonly float[] Raw;
        public readonly string Error;
        public readonly bool ProfileSent;

        private JevResult(float[] scores, float[] raw, string error, bool profileSent)
        {
            Scores = scores;
            Raw = raw;
            Error = error;
            ProfileSent = profileSent;
        }

        public bool Ok => Scores != null;

        public static JevResult Success(float[] scores) => new JevResult(scores, null, null, false);

        public static JevResult Success(float[] scores, float[] raw) => new JevResult(scores, raw, null, false);

        public static JevResult Failure(string error) => new JevResult(null, null, error, false);

        public JevResult WithProfileSent(bool profileSent) => new JevResult(Scores, Raw, Error, profileSent);
    }

    public static class JevClient
    {
        public const string SystemOnePath = "/v1/systemone";
        public const float InitialBackoffSeconds = 0.5f;
        public const float MaxBackoffSeconds = 5f;

        private static readonly string[] ClassNames = Enum.GetNames(typeof(Emotion));

        private static readonly string[] AuNames = AuNamesOf(typeof(OVRFaceExpressions.FaceExpression));

        internal static string[] AuNamesOf(Type expressionEnum)
        {
            return Enum.GetValues(expressionEnum)
                .Cast<object>()
                .Select(member => (value: Convert.ToInt32(member), name: member.ToString()))
                .Where(member => member.value >= 0)
                .OrderBy(member => member.value)
                .Take(CalibrationMath.FaceActionUnitCount)
                .Select(member => member.name)
                .ToArray();
        }

        public static string Preflight(JevSettings settings)
        {
            if (settings == null)
            {
                return "no Jev Settings asset is assigned";
            }

            return settings.ResolveApiKey() == null
                ? $"no Typesafe API key: set it in the Jev Settings asset or in {JevSettings.ApiKeyVariable}"
                : null;
        }

        public static bool ShouldRetry(long statusCode) => statusCode == 0 || statusCode == 408 || statusCode == 429 || statusCode >= 500;

        public static float BackoffSeconds(int attempt) => Mathf.Min(MaxBackoffSeconds, InitialBackoffSeconds * Mathf.Pow(2f, attempt));

        public static async Awaitable<JevResult> Classify(JevSettings settings, float[][] window, CalibrationProfile profile)
        {
            string problem = Preflight(settings);
            if (problem != null)
            {
                return JevResult.Failure(problem);
            }

            string apiKey = settings.ResolveApiKey();

            string body;
            try
            {
                body = JevRequestBuilder.Build(settings.Model, window, AuNames, ClassNames, profile);
            }
            catch (ArgumentException e)
            {
                return JevResult.Failure(e.Message);
            }

            string url = settings.BaseUrl.TrimEnd('/') + SystemOnePath;
            bool profileSent = profile != null;

            for (int attempt = 0; ; ++attempt)
            {
                var (status, text) = await Post(url, apiKey, body, settings.TimeoutSeconds);

                if (status >= 200 && status < 300)
                {
                    var parsed = JevResponseParser.TryParse(text, ClassNames, out var scores, out var raw, out string error)
                        ? JevResult.Success(scores, raw)
                        : JevResult.Failure(error);
                    return parsed.WithProfileSent(profileSent);
                }

                if (attempt >= settings.MaxRetries || !ShouldRetry(status))
                {
                    return JevResult.Failure(JevResponseParser.FailureMessage(status, text)).WithProfileSent(profileSent);
                }

                await Awaitable.WaitForSecondsAsync(BackoffSeconds(attempt));
            }
        }

        private static async Awaitable<(long status, string text)> Post(string url, string apiKey, string body, float timeoutSeconds)
        {
            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {apiKey}");
            request.timeout = Mathf.CeilToInt(timeoutSeconds);

            await request.SendWebRequest();

            string text = request.responseCode == 0 ? request.error : request.downloadHandler.text;
            return (request.responseCode, text);
        }
    }
}

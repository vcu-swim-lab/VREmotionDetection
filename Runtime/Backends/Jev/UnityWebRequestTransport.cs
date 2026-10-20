using System;
using System.Globalization;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

namespace VcuSeCenter.Backends.Jev
{
    public sealed class UnityWebRequestTransport : ICancelableHttpTransport
    {
        private const long TimeoutStatus = 408;

        public Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds)
        {
            return PostJsonAsync(url, bearerToken, jsonBody, timeoutSeconds, CancellationToken.None);
        }

        public async Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds,
                                                        CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", $"Bearer {bearerToken}");
            request.timeout = Mathf.CeilToInt(timeoutSeconds);

            using (cancellationToken.Register(request.Abort))
            {
                await request.SendWebRequest();
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (request.responseCode == 0 ||
                request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.DataProcessingError)
            {
                string error = request.error ?? string.Empty;
                bool timedOut = error.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0;
                return new HttpReply(timedOut ? TimeoutStatus : 0, error);
            }

            return new HttpReply(request.responseCode, request.downloadHandler.text, RetryAfter(request));
        }

        private static float? RetryAfter(UnityWebRequest request)
        {
            string header = request.GetResponseHeader("Retry-After");
            return float.TryParse(header, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) && seconds >= 0f
                ? seconds
                : (float?)null;
        }
    }
}

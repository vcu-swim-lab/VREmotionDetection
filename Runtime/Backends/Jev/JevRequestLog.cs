using UnityEngine;

namespace VcuSwimLab.Backends.Jev
{
    internal static class JevRequestLog
    {
        internal static void Write(string url, HttpReply reply, long milliseconds, int attempt, int attempts, string apiKey)
        {
            string reason = reply.StatusCode >= 200 && reply.StatusCode < 300
                ? string.Empty
                : " " + JevResponseParser.FailureMessage(reply.StatusCode, Redact(reply.Body, apiKey));

            Debug.Log($"[Jev] POST {url} -> {reply.StatusCode} in {milliseconds} ms (attempt {attempt + 1} of {attempts}){reason}");
        }

        internal static string Redact(string text, string apiKey)
        {
            return string.IsNullOrEmpty(text) ? text : text.Replace(apiKey, "<redacted>");
        }
    }
}

using UnityEngine;

namespace VcuSeCenter.Backends.Jev
{
    internal static class JevRequestLog
    {
        internal static void Write(string url, HttpReply reply, long milliseconds, int attempt, int attempts, string apiKey,
                                   bool includeServerDetails)
        {
            string reason = reply.StatusCode >= 200 && reply.StatusCode < 300
                ? string.Empty
                : " " + JevResponseParser.FailureMessage(reply.StatusCode, reply.Body, apiKey, includeServerDetails);

            Debug.Log($"[Jev] POST {url} -> {reply.StatusCode} in {milliseconds} ms (attempt {attempt + 1} of {attempts}){reason}");
        }
    }
}

using UnityEngine;

namespace VcuSwimLab.Backends.Jev
{
    public interface IHttpTransport
    {
        Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds);
    }
}

using System.Threading;
using UnityEngine;

namespace VcuSeCenter.Backends.Jev
{
    public interface ICancelableHttpTransport : IHttpTransport
    {
        Awaitable<HttpReply> PostJsonAsync(string url, string bearerToken, string jsonBody, float timeoutSeconds,
                                           CancellationToken cancellationToken);
    }
}

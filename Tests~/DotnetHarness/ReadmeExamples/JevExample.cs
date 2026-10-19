using UnityEngine;
using VcuSwimLab.Backends.Jev;
using VcuSwimLab.VREmotionDetection;

[RequireComponent(typeof(NewFaceAuModel))]
public class JevExample : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        // 1. Choose the backend and pass YOUR OWN key as a parameter.
        //    Editor and desktop: read it from the TYPESAFE_API_KEY environment variable.
        //    On a headset there are no environment variables: load the key from your own
        //    server or secure storage and call new JevOptions(key) instead.
        predictor.UseJev(JevOptions.FromEnvironment());

        // 2. Optional: hear about every failure (no network, wrong key, ...).
        predictor.PredictionFailed += error => Debug.LogWarning(error);

        _ = Run();
    }

    private async Awaitable Run()
    {
        // 3. Predict. A failed call returns (Neutral, 0): check LastError, and wait before
        //    trying again so that a wrong key does not send a request every frame.
        while (!destroyCancellationToken.IsCancellationRequested)
        {
            var (emotion, confidence) = await predictor.Predict();

            if (predictor.LastError.HasValue)
            {
                await Awaitable.WaitForSecondsAsync(2f);
                continue;
            }

            Debug.Log($"{emotion} ({confidence:F2}) from {predictor.ActiveBackendId}");
        }
    }
}

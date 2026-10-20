using UnityEngine;
using VcuSeCenter.Backends.Jev;
using VcuSeCenter.VREmotionDetection;

[RequireComponent(typeof(NewFaceAuModel))]
public class JevPredictor : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        predictor.UseJev(JevOptions.FromEnvironment());

        predictor.PredictionFailed += error => Debug.LogWarning(error);

        _ = Run();
    }

    private async Awaitable Run()
    {
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

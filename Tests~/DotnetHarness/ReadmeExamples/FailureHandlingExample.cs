using UnityEngine;
using VcuSwimLab.Backends;

[RequireComponent(typeof(NewFaceAuModel))]
public class FailureHandlingExample : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        // Raised for every failed prediction, whichever backend is active.
        predictor.PredictionFailed += OnPredictionFailed;

        _ = Run();
    }

    void OnDestroy()
    {
        predictor.PredictionFailed -= OnPredictionFailed;
    }

    private async Awaitable Run()
    {
        while (!destroyCancellationToken.IsCancellationRequested)
        {
            PredictionOutcome outcome = await predictor.TryPredict();

            if (outcome.Ok)
            {
                Debug.Log($"{outcome.Prediction.Emotion} ({outcome.Prediction.Confidence:F2})");
                continue;
            }

            PredictionError error = outcome.Error.Value;

            if (error.Code == PredictionErrorCode.Unauthorized)
            {
                Debug.LogError("The Typesafe key was rejected. Ask for a new one.");
                return;
            }

            await Awaitable.WaitForSecondsAsync(error.IsTransient ? 2f : 0.5f);
        }
    }

    private void OnPredictionFailed(PredictionError error)
    {
        Debug.LogWarning($"{error.Code}: {error.Message}");
    }
}

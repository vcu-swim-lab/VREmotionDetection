using UnityEngine;

[RequireComponent(typeof(NewFaceAuModel))]
public class SparseVrExample : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        // 1. Choose the backend. SparseVr is already the default, so this line is optional.
        predictor.UseSparseVr();

        _ = Run();
    }

    private async Awaitable Run()
    {
        // 2. Predict, again and again.
        while (!destroyCancellationToken.IsCancellationRequested)
        {
            PredictionOutcome outcome = await predictor.TryPredict();

            if (outcome.Ok)
            {
                FacePrediction prediction = outcome.Prediction;
                Debug.Log($"{prediction.Emotion} ({prediction.Confidence:P0})");
            }
            else
            {
                Debug.LogWarning(outcome.Error.Value);
                await Awaitable.WaitForSecondsAsync(1f);
            }
        }
    }
}

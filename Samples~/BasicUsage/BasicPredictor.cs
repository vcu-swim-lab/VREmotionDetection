using UnityEngine;
using VcuSwimLab.VREmotionDetection;

[RequireComponent(typeof(NewFaceAuModel))]
public class BasicPredictor : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        predictor.UseLocalModel();

        _ = Run();
    }

    private async Awaitable Run()
    {
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

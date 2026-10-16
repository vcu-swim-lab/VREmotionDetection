using UnityEngine;
using VcuSwimLab.Backends;
using VcuSwimLab.Backends.Jev;

[RequireComponent(typeof(NewFaceAuModel))]
public class CompareBackendsExample : MonoBehaviour
{
    private NewFaceAuModel predictor;
    private IEmotionBackend jev;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        // Create the second backend once. It is not selected: SparseVr stays the active one.
        jev = predictor.CreateJevBackend(JevOptions.FromEnvironment());

        _ = Run();
    }

    void OnDestroy()
    {
        jev?.Dispose();
    }

    private async Awaitable Run()
    {
        while (!destroyCancellationToken.IsCancellationRequested)
        {
            PredictionOutcome outcome = await predictor.TryPredict();
            if (!outcome.Ok)
            {
                await Awaitable.WaitForSecondsAsync(1f);
                continue;
            }

            // Ask Jev about the very same window of face data.
            BackendResult other = await predictor.ScoreWith(jev, outcome.Prediction.Window);

            if (other.Ok)
            {
                var jevEmotion = (Emotion)ScoreValidation.Argmax(other.Scores);
                Debug.Log($"SparseVr: {outcome.Prediction.Emotion}   Jev: {jevEmotion}");
            }
            else
            {
                Debug.LogWarning(other.Error.Value);
                await Awaitable.WaitForSecondsAsync(2f);
            }
        }
    }
}

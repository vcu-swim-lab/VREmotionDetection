using UnityEngine;

// Emotion predictions without calibration. Put this on the GameObject that has the
// NewFaceAuModel component.
[RequireComponent(typeof(NewFaceAuModel))]
public class BasicPredictor : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();
        _ = PredictLoop();
    }

    private async Awaitable PredictLoop()
    {
        while (true)
        {
            // The most likely emotion and its probability (0 to 1).
            var (emotion, confidence) = await predictor.Predict();
            Debug.Log($"{emotion} ({confidence:F2})");

            // Or every detail: all seven probabilities, in Emotion order
            // (Anger, Disgust, Fear, Happiness, Neutral, Sadness, Surprise).
            // Null when face tracking gave no valid frames in time.
            var detailed = await predictor.PredictDetailed();
            if (detailed.HasValue)
            {
                float[] scores = detailed.Value.Scores;
                Debug.Log($"Happiness probability: {scores[(int)Emotion.Happiness]:F2}");
            }
        }
    }
}

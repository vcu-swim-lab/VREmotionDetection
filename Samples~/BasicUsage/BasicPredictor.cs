
using UnityEngine;

[RequireComponent(typeof(FaceAuModel))]
public class MyScript : MonoBehaviour
{
    private FaceAuModel auModel;

    void Start()
    {
        // ...
        auModel = GetComponent<FaceAuModel>();

        // Start the prediction task, use the predictor from there
        PredictTask();
    }

    void Update()
    {
    }

    private async Awaitable PredictTask()
    {
        while (true)
        {
            // Get a prediction
            // Confidence tells you how certain the model is about this emotion. You can ignore it most of the time.
            var (emo, confidence) = await predictor.Predict();

            // use the emotion here, possible values are:
            // - Emotion.Anger
            // - Emotion.Disgust
            // - Emotion.Fear
            // - Emotion.Happiness
            // - Emotion.Neutral
            // - Emotion.Sadness
            // - Emotion.Surprise

            Debug.Log($"Got emotion {emo}");
        }
    }
}


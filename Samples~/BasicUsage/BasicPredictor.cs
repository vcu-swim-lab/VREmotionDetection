
using UnityEngine;

[RequireComponent(typeof(EmotionPredictor))]
[RequireComponent(typeof(OVRFaceExpressions))]
public class MyScript : MonoBehaviour
{
    private EmotionPredictor predictor;

    void Start()
    {
        // ...
        predictor = GetComponent<EmotionPredictor>();

        // somewhere; write this if `Poll By Default` is false (which is the default value)
        predictor.Polling = true;
        // ^ you can set `Polling` to false at any time to stop polling data, eg. between scene transitions

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


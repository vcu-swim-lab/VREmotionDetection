
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

            // Or, you can get a list of all confidences and process them yourself.
            // NOTE: when calling `Predict` the model internally multiplies the confidences by some values,
            // so using confidences (`PredictRaw`) is recommended when you have only natural or only acted data and the results
            // will be slightly different from `Predict`.
            var (nat, act) = await predictor.PredictRaw();
            var natArr = nat.AsReadOnlyNativeArray(); // float[7] 0.0-1.0
            var actArr = act.AsReadOnlyNativeArray(); // float[7] 0.0-1.0
            // ... use the confidences here

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


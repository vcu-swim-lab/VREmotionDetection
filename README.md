
# VREmotionDetection

A library for detecting emotions in VR.

## Setup

1. Open your Unity game, go to `Window > Package Manager`.
2. Top-left corner, click on `+ > Install package from git URL..`, paste [https://github.com/vcu-swim-lab/VREmotionDetection.git]() and then click `Install`.
3. Go to `Assets`, then right click and `Create > Scriptable Objects > PredictorConfig`.
4. Click the created `PredictorConfig`, and then `Au Model`. From there pick `natural_trial_96.onnx`. Also enable `Poll By Default` if you want the predictor to start reading data starting from initialization, otherwise you have to manually enable polling later from your script, as shown in the example below.
5. Now go to Unity's `Hierarchy` tab and pick one game object that you want to use as a predictor. From there on `Inspector > Add Component > Emotion Predictor`. Then click on the `Config` field of `Emotion Predictor` and select the `PredictorConfig` asset you created earlier.
6. Create a new `MonoBehavior` script to use the predictions from the model and attach it to the same game object. Open the script in your editor of choice and change the code to look like following:
```cs
using Oculus.Interaction;

[RequireComponent(typeof(EmotionPredictor))]
[RequireComponent(typeof(OVRFaceExpressions))]
// ^ add these lines
public class MyScript : MonoBehaviour
{
    private EmotionPredictor predictor; // < add this line

    void Start()
    {
        // ...
        predictor = GetComponent<EmotionPredictor>(); // < add this line

        // somewhere; write this if `Poll By Default` is false (which is the default value)
        predictor.Polling = true;
        // ^ you can set `Polling` to false at any time to stop polling data, eg. between scene transitions

        // Start the prediction task, use your predictions there
        PredictTask();
    }

    void Update()
    {
    }

    private async void PredictTask()
    {
        while (true)
        {
            // Get a prediction
            (var emo, _) = await predictor.Predict();

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
```

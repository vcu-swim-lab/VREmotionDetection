
# VREmotionDetection

**VREmotionDetection** is a Unity library for detecting emotions in VR using machine learning models.  

## 📋 Prerequisites

- Unity 6 or newer.
- Meta Quest Pro if you want to use use action units for prediction.


## 📦 Installation

1. In your Unity project, open **`Window > Package Manager`**.  
2. In the top-left corner, click **`+` > `Install package from git URL...`**  
3. Paste the following URL:  

   ```
   https://github.com/vcu-swim-lab/VREmotionDetection.git
   ```

4. Click **Install**.


## ⚙️ Configuration

1. In Unity, select the object that you want to add the predictor model from the **Hierarchy** tab.
2. In the **Inspector** tab, click **Add Component**, then **Face Au Model**.
3. In the **Natural Model** field, pick `natural_trial_96.onnx` and for the `Acted Model` field pick `act_trial_92.onnx`.

## ▶ Usage

1. Create a new **MonoBehaviour** script and attach it to the same GameObject as the Emotion Predictor.  
2. Open the script in your code editor.  
3. Copy and paste the sample code from:  
   [`Samples~/Basic/MyScript.cs`](./Samples~/BasicUsage/BasicPredictor.cs).  

This script will give you direct access to the predictions from the emotion detection model.

## 🎯 Movement calibration (optional)

People make the same emotion with different faces. Movement calibration learns how *this*
player makes each emotion, then matches every prediction against the player's own faces.
Without it, `Predict` works exactly as before.

```csharp
predictor.StartCalibration();
foreach (Emotion emotion in Enum.GetValues(typeof(Emotion)))
{
    // Ask the player to make this face and hold it, then:
    await predictor.CalibrateEmotion(emotion);   // about 10 seconds
}
predictor.FinishCalibration();                   // false if it failed; see CalibrationError

var (emotion, confidence) = await predictor.Predict();   // now calibrated
```

- Each emotion takes 10 samples, one per second. The first is dropped while the face is still changing.
- Samples without face tracking are retried. If an emotion cannot be collected, `FinishCalibration`
  returns `false`, `CalibrationError` says why, and `Predict` keeps using the model's own answer.
- `IsCalibrated` tells you which one `Predict` is using. `ClearCalibration` goes back to the model's own answer.
- A full example is in [`Samples~/MovementCalibration`](./Samples~/MovementCalibration/CalibratedPredictor.cs).

On the Quest Pro headset in our tests (10 sessions, mostly one person), movement calibration
raised macro-F1 from 0.58 to 0.74. It has not yet been tested on many different people.


## 📝 License

This project is licensed under the [MIT License](./LICENSE).

© 2026 Virginia Commonwealth University, All rights reserved.

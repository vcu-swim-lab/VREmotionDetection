
# VREmotionDetection

**VREmotionDetection** is a Unity library for detecting emotions in VR from the face-tracking
data of a Meta Quest Pro. It predicts seven emotions: Anger, Disgust, Fear, Happiness,
Neutral, Sadness and Surprise. An optional calibration adapts it to each player.

## 📋 Prerequisites

- Unity 6 or newer.
- Meta Quest Pro (its inward cameras provide the face tracking the model needs).
- Meta XR Core SDK with face tracking enabled for the project.


## 📦 Installation

1. In your Unity project, open **`Window > Package Manager`**.  
2. In the top-left corner, click **`+` > `Install package from git URL...`**  
3. Paste the following URL:  

   ```
   https://github.com/vcu-swim-lab/VREmotionDetection.git
   ```

4. Click **Install**.


## ⚙️ Setup

1. Select the GameObject that should run the predictor in the **Hierarchy**.
2. In the **Inspector**, click **Add Component** and add **New Face Au Model**. It also adds
   `OVRFaceExpressions` and `DeviceManager`.
3. The **Face Model** field is filled in automatically with
   `sparseVR_teacher_model_acted_only_vol-aug-k1_with-EmojiHero.onnx`, the acted-data model.
   If it is empty, pick that file from `Runtime/Models`.

`FaceAuModel`, the original predictor that combined a natural-data and an acted-data model, is
obsolete. Use `NewFaceAuModel`. Models the library no longer uses are kept in `Archive~/`.


## 🧠 Model input and output

The predictor builds the input itself; this is what it does, for anyone feeding the model
directly.

| | |
|---|---|
| Values per frame | 70: the `OVRFaceExpressions` face-tracking weights, in its `FaceExpression` order (`BrowLowererL` … `TongueRetreat`) |
| Range and scaling | Raw weights, 0 to 1. No scaling or normalisation |
| Window | 5 consecutive frames, oldest first, read at 10 Hz (half a second) |
| Missing tracking | Frames without valid face tracking are dropped, never filled with zeros |
| Input tensor | `[N, 5, 70]`, float32 |
| Output | 7 probabilities that sum to 1, in `Emotion` order: Anger, Disgust, Fear, Happiness, Neutral, Sadness, Surprise |

The model was trained on acted expressions (posed on request) recorded on the Quest Pro, with
5-frame windows at about 10 Hz.


## ▶ Usage

Add a script to the same GameObject and call the predictor:

```csharp
var predictor = GetComponent<NewFaceAuModel>();

var (emotion, confidence) = await predictor.Predict();      // most likely emotion, 0 to 1

var detailed = await predictor.PredictDetailed();          // null if tracking was lost
float[] scores = detailed.Value.Scores;                     // all seven probabilities
```

A full example is in [`Samples~/BasicUsage`](./Samples~/BasicUsage/BasicPredictor.cs).


## 🎯 Calibration (optional)

People make the same emotion with different faces. Movement calibration learns how *this*
player makes each emotion, then matches every prediction against the player's own faces.
It is **off by default**: without it, `Predict` returns the model's own answer.

**Turn it on** by ticking **Use Calibration** on the **New Face Au Model** component (or
`predictor.UseCalibration = true` in code). Then run the calibration:

```csharp
predictor.StartCalibration();
foreach (Emotion emotion in Enum.GetValues(typeof(Emotion)))
{
    // Ask the player to make this face and hold it, then:
    await predictor.CalibrateEmotion(emotion);   // about 10 seconds
}
if (predictor.FinishCalibration())               // false if it failed; see CalibrationError
{
    predictor.SaveCalibration("player1");        // optional: keep it for next time
}

var (emotion, confidence) = await predictor.Predict();   // now calibrated
```

| Method | What it does |
|---|---|
| `UseCalibration` | The **Use Calibration** checkbox. Off: the methods below do nothing and `Predict` uses the model's own answer |
| `StartCalibration()` | Start a new calibration |
| `CalibrateEmotion(emotion)` | Collect one emotion: 10 samples, one per second; the first is dropped while the face is still changing. Samples without tracking are retried |
| `FinishCalibration()` | Build it. `false` when an emotion is missing; `CalibrationError` says why |
| `SaveCalibration(userId)` / `LoadCalibration(userId)` | Save it for this player, or load and use a saved one |
| `ClearCalibration()` | Stop using calibration; back to the model's own answer |
| `DeleteSavedCalibration(userId)` | Delete the player's saved calibration from the device |
| `IsCalibrated` | Whether `Predict` is using calibration |
| `BeginCalibrationEmotion(emotion)`, `AddCalibrationSample(position, prediction)` | Advanced: for games that take their own predictions on a fixed clock (e.g. a study protocol) instead of `CalibrateEmotion`. Position 0 is dropped; a tick without tracking is not retried |
| `CalibratedDecision(prediction)` | The calibrated emotion with its similarity scores, for detailed logs |

How it decides: for each emotion, it adds two similarities — how close the model's scores are
to this player's scores for that emotion, and how close the face movements are to this
player's face for that emotion. The emotion with the highest sum wins.

**Where calibration is stored.** A calibration describes one person's face. It is saved only
on the device, in `Application.persistentDataPath/VREmotionDetection/calibration/<userId>.json`,
never in the model asset or the build. It is tied to the model it was made with; after a model
change, `LoadCalibration` refuses it and the player calibrates again.

A full example is in [`Samples~/MovementCalibration`](./Samples~/MovementCalibration/CalibratedPredictor.cs).

**Check it on your headset:** the [`Samples~/CalibrationCheck`](./Samples~/CalibrationCheck/CalibrationCheck.cs)
sample calibrates, then asks for each face again and reports the uncalibrated and the calibrated
answer for the same predictions, with your own Quest Pro face data.


## 📊 Results

With **Save Results** ticked on the predictor (the default), every prediction of a session is
saved on the headset in one CSV file, one row per prediction:

| time_utc | seconds | emotion | confidence | calibrated | model_emotion |
|---|---|---|---|---|---|
| 2026-10-12T14:03:05.120Z | 12.004 | Happiness | 0.8123 | yes | Surprise |

`emotion` is what your game received; `model_emotion` is the model's own answer (they differ
only when calibration changed it). Calibration samples and raw face-tracking values are not
saved.

**Show the player a summary** at the end of a session:

```csharp
resultsText.text = predictor.GetResultsSummary();
// 120 predictions over 2.1 min
// Happiness: 40% (average confidence 0.81)
// Neutral: 30% (average confidence 0.66) ...
```

**Get the files on your PC:** connect the headset with a USB cable, then in Unity choose
**VR Emotion > Get Results From Headset**. The files are copied to a `HeadsetResults` folder
in your project, which opens. Open them in Excel or Google Sheets. The folder comes with its
own `.gitignore`, so the results are never committed.

The files describe real people. `NewFaceAuModel.DeleteSavedResults()` deletes them from the
headset.


## 📝 License

This project is licensed under the [MIT License](./LICENSE).

© 2026 Virginia Commonwealth University, All rights reserved.

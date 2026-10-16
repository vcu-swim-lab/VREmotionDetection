
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
answer for the same predictions, with your own Quest Pro face data. With the local backend and a Jev
Settings asset assigned on the predictor (see below), it adds Jev's answer for the same face data as a
third column.


## 🌐 Jev backend (optional)

Instead of the bundled model, the predictor can ask [Typesafe's Jev model](https://docs.typesafe.ai)
over the network. The API is identical, so `Predict`, `PredictDetailed` and every sample work
unchanged. It is **off by default**: the predictor runs the local model unless you change the backend.

1. Get your own API key at [console.typesafe.ai/keys](https://console.typesafe.ai/keys). The library
   does not provide one, and you should never write it into source code.
2. **`Assets > Create > VR Emotion > Jev Settings`**, and paste the key into the new asset (keep the
   asset out of version control). Alternatively leave **Jev Settings** empty on the predictor and set
   the `TYPESAFE_API_KEY` environment variable (Editor and desktop only, a headset build has none), or
   set it from code with `settings.ApiKey = "..."`.
3. On the **New Face Au Model** component, set **Backend** to **Jev**.
4. For Quest builds, set **Player Settings > Other Settings > Internet Access** to **Require**.

```csharp
predictor.Backend = PredictionBackend.Jev;
predictor.JevSettings = settings;                          // optional if TYPESAFE_API_KEY is set

string problem = predictor.JevPreflight();                 // null when a key is available
var (emotion, confidence) = await predictor.Predict();     // (Neutral, 0) if the call failed
string error = predictor.JevError;                         // why it failed; null when it worked
```

**Confidence.** Jev answers each emotion separately, so its answers do not add up to 1. The winner is
chosen from the answers divided by their sum, but the confidence is Jev's own answer for the winning
emotion (`FacePrediction.Raw`, and `ScoreOf(emotion)`), because a lone weak answer would otherwise
read as 1.0.

**Calibration with Jev.** The calibration works the same way, on the answers of the selected backend.
With Jev, `CalibrateEmotion` asks Jev about each calibration sample (about 70 requests in all), and
the same nearest-centroid rule then maps Jev's answers to this player. Jev answers in steps of 0.01
and often says 0.00, so a small floor is added to its scores before they enter the calibration. A
calibration made with one backend is refused by the other (`LoadCalibration` says to calibrate
again), and changing `Backend` clears the current calibration.

Optionally, tick **Send Own Faces In Request** on the Jev Settings asset to also give Jev the player's
own faces in every request: for each emotion, the cosine similarity between the current face and the
player's face for that emotion, its rank, the player's most characteristic movements and how many
are active now. These are calculated in code; Jev is not given raw calibration numbers.

**Things to know:**
- **Privacy.** Face-tracking data about the player leaves the device and goes to a third party.
  Get the player's consent.
- **A key in a shipped build can be extracted.** Do not embed it in code. For a public release, set
  **Base Url** on the asset to your own server that adds the key and forwards the request to Typesafe.
- **Failures are not hidden.** A missing key, no network or an API error gives `(Neutral, 0)` from
  `Predict`, `null` from `PredictDetailed`, a message in `JevError`, one console warning per distinct
  message, and a `jev_error` row in the results file. There is no fallback to the local model.
- **Cost and speed.** Each prediction is one request with seven questions, billed per input token
  (about $0.042 per million). It takes a network round trip, so ask less often than with the local
  model. If the network is down, one call can take up to about 30 seconds (10 s timeout, two retries).
- **Not proven to be better.** Run `Samples~/CalibrationCheck` with the backend set to Jev to compare
  Jev uncalibrated with Jev calibrated, or with a Jev Settings asset assigned and the local backend
  to compare Jev with the local model, on your own face data before relying on it.


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

### Where the files are

| | Location |
|---|---|
| On the headset | `/sdcard/Android/data/<your app's package name>/files/VREmotionDetection/results/` |
| In the Unity Editor (Play mode) | `Application.persistentDataPath/VREmotionDetection/results/` (on Windows: `C:\Users\<you>\AppData\LocalLow\<Company>\<Product>\...`) |
| On your PC, after copying | `<your project folder>/HeadsetResults/`, next to `Assets` |

- **One file per run of the app**, named by when it started: `session_<date>_<time>.csv`. The time
  is **UTC**, not local time. Several rounds played without closing the app go into the same file.
- **The file does not say who played.** If several people use the headset, write down which
  session time belongs to whom.

### Copy the files to your PC

1. Run at least one session on the headset.
2. Connect the headset to the PC with a USB cable and turn it on. It must be in developer mode.
3. Put the headset on; if it asks **"Allow USB debugging?"**, choose **Allow** (tick "Always
   allow from this computer" to skip this next time).
4. In Unity, with your project open and compiled, choose **VR Emotion > Get Results From
   Headset** in the top menu bar.
5. A message says how many files were copied, and the `HeadsetResults` folder opens. Open a
   file in Excel or Google Sheets: one row per prediction.

It copies **all** session files on the headset, so the newest is always included (sort by name
to find it). Clicking again is safe, and nothing is deleted from the headset. It needs Unity's
Android Build Support, which provides `adb`; your app's package name is read from Player Settings.

| Message | What to do |
|---|---|
| No headset found | Check the cable, that the headset is on and in developer mode |
| The headset has not allowed this computer | Put it on and choose **Allow** (step 3) |
| There are no results on the headset yet | Run a session with **Save Results** ticked |
| Could not find adb | Unity Hub > Installs > Add modules > Android Build Support |

`HeadsetResults` comes with its own `.gitignore`, so the results are never committed. The files
describe real people: `NewFaceAuModel.DeleteSavedResults()` deletes them from the headset.


## 📝 License

This project is licensed under the [MIT License](./LICENSE).

© 2026 Virginia Commonwealth University, All rights reserved.

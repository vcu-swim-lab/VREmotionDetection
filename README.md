# VREmotionDetection

**Read a player's emotion from a Meta Quest Pro, inside your Unity game.**

The Quest Pro tracks the player's face. This package turns that face-tracking data into one of
seven emotions (**Anger, Disgust, Fear, Happiness, Neutral, Sadness, Surprise**) and tells you how
sure it is, about twice a second. You write a few lines; the package does the rest.

```csharp
var (emotion, confidence) = await predictor.Predict();      // Happiness, 0.81
```

## Contents

1. [What this package is for](#1-what-this-package-is-for)
2. [Before you start](#2-before-you-start)
3. [Add it to your project](#3-add-it-to-your-project)
4. [Set up a scene](#4-set-up-a-scene)
5. [Your first prediction](#5-your-first-prediction)
6. [How it works in 60 seconds](#6-how-it-works-in-60-seconds)
7. [The order of calls (cheat sheet)](#7-the-order-of-calls-cheat-sheet)
8. [Recipes](#8-recipes) — SparseVr, SparseVr + calibration, Jev, Jev + calibration, comparing, failures
9. [Function reference](#9-function-reference)
10. [Return types in detail](#10-return-types-in-detail)
11. [Calibration in depth](#11-calibration-in-depth)
12. [Jev in depth](#12-jev-in-depth)
13. [Results files](#13-results-files)
14. [Write your own backend](#14-write-your-own-backend)
15. [Troubleshooting](#15-troubleshooting)
16. [More](#16-more)

---

## 1. What this package is for

Use it when your VR game should **react to how the player feels**: a character that notices a
smile, a horror scene that watches for fear, a study that records emotions during play.

It gives you three things:

| You get | What it means |
|---|---|
| **A prediction** | the most likely emotion, how sure the model is, and the score of every emotion |
| **Two interchangeable "backends"** | **SparseVr** runs on the headset with the bundled model (no internet, free). **Jev** asks Typesafe's cloud model (needs internet and your own API key). You pick one; you can switch |
| **Optional calibration** | people make the same emotion with different faces. A short calibration (about a minute and a half) teaches the package how *this* player makes each emotion, and predictions get more accurate for them |

Everything is reported honestly: if something goes wrong (tracking lost, no internet, wrong key)
you get a **typed error**, never a silent wrong answer, and the package **never quietly switches
to another model**.

**Which backend should I choose?**

| | SparseVr | Jev |
|---|---|---|
| Runs | on the headset | in Typesafe's cloud |
| Needs | nothing extra (the model is in the package) | internet, and **your own** Typesafe API key |
| Cost | none | billed per use by Typesafe |
| Player's face data | stays on the device | is sent to a third party: get consent |
| Speed | immediate | one network round trip per prediction |
| Calibration | works | works (about 70 extra requests while calibrating) |

If you are unsure, start with **SparseVr**. It is the default.

---

## 2. Before you start

| You need | Notes |
|---|---|
| **Unity 6** (6000.0) or newer | |
| **Meta Quest Pro** | its inward cameras provide the face tracking |
| **Meta XR Core SDK** | the package installs it for you (see step 3) |
| **Face Tracking Support** enabled | in your project's Meta settings, set **Face Tracking Support** to **Required** or **Supported** (OVRManager, *Quest Features → General*; Meta's *Project Setup Tool* also flags it). The first time the app runs, the headset asks the player to allow face tracking |
| **Internet Access = Require** | only if you use Jev: *Player Settings → Other Settings → Internet Access* |
| A **Typesafe API key** | only if you use Jev. Get your own at [console.typesafe.ai/keys](https://console.typesafe.ai/keys). The package provides none |

---

## 3. Add it to your project

1. In Unity open **Window → Package Manager**.
2. Click **+** (top left) → **Install package from git URL…**
3. Paste:

   ```
   https://github.com/vcu-swim-lab/VREmotionDetection.git
   ```

4. Click **Install**. Unity also installs what the package depends on: Unity's Inference Engine,
   the Meta XR Core SDK, OpenXR and Newtonsoft Json.

To lock a version, add `#` and a tag or commit to the URL, for example
`https://github.com/vcu-swim-lab/VREmotionDetection.git#<commit-hash>`.

**Samples:** in the Package Manager select *VR Emotion Detector*, open the **Samples** tab and
**Import** one: *Basic Usage*, *Movement Calibration*, *Calibration Check*, *Jev Prediction*.

---

## 4. Set up a scene

1. Select the GameObject that should run the predictor (any object in your scene).
2. **Add Component → New Face Au Model.** Unity also adds `OVRFaceExpressions` and
   `DeviceManager`, which it needs.
3. Look at the Inspector:

| Field | Default | What it does |
|---|---|---|
| **Face Model** | filled in automatically with `sparseVR_teacher_model_acted_only_vol-aug-k1_with-EmojiHero.onnx` | the SparseVr model. If it is empty, drag that file from the package's `Runtime/Models` folder. Leave it empty only if you will use Jev alone |
| **Use Calibration** | off | switches calibration on. You can also set `predictor.UseCalibration = true` in code |
| **Save Results** | on | writes every prediction to a CSV file on the headset (see [Results files](#13-results-files)) |

That is all the setup. Your scripts talk to the `NewFaceAuModel` component.

---

## 5. Your first prediction

Add this script to the same GameObject and press Play on the headset (or Quest Link):

```csharp
using UnityEngine;

[RequireComponent(typeof(NewFaceAuModel))]
public class FirstPrediction : MonoBehaviour
{
    async void Start()
    {
        var predictor = GetComponent<NewFaceAuModel>();

        var (emotion, confidence) = await predictor.Predict();

        Debug.Log($"{emotion} ({confidence:F2})");
    }
}
```

You should see something like `Happiness (0.81)` in the Console. The call takes about half a
second: it waits for five fresh readings of the face (the model reads the face ten times a second).

* `emotion` is one of the seven emotions.
* `confidence` is a number from 0 to 1.
* If anything fails you get `(Neutral, 0)`. That is also a real neutral reading with zero
  confidence, so use `TryPredict` (Recipe 1) when you need to tell the two apart.

---

## 6. How it works in 60 seconds

```
 Quest Pro face tracking ──► 5 readings of 70 face values (half a second)
                                   │
                                   ▼
              ┌──────────── a BACKEND scores the window ────────────┐
              │   SparseVr (on the headset)   or   Jev (the cloud)   │
              └──────────────────────────┬───────────────────────────┘
                                         ▼
                      7 scores, one per emotion  ──►  the highest wins
                                         │
                       (optional) CALIBRATION compares them with how
                       this player made each emotion before
                                         ▼
                           emotion + confidence  ──►  your game
                                         │
                                         └──►  one line in the results CSV
```

| Word | Meaning |
|---|---|
| **window** | the last five valid readings of the face, oldest first |
| **backend** | the thing that turns a window into scores: SparseVr, Jev, or one you write |
| **prediction** | the result for one window: emotion, confidence, scores |
| **outcome** | what `TryPredict` returns: either a prediction or an error |
| **calibration** | the player's own "this is my angry face, this is my happy face" reference |
| **confidence** | how strongly the backend favours the emotion it returned (0 to 1) |

---

## 7. The order of calls (cheat sheet)

Things must happen in this order. The recipes in the next section are exactly this, as code.

| Step | Call | Why this position |
|---|---|---|
| **1. Choose the backend** | nothing for SparseVr (default), or `UseSparseVr()`, or `UseJev(options)` | a calibration belongs to the backend that made it, so the backend comes first. Choosing a *different* backend later clears the calibration |
| **2. Turn calibration on** *(only if you want it)* | `UseCalibration = true` | while it is off, the calibration calls do not calibrate and `CalibrationError` says so |
| **3. Load a saved calibration** | `LoadCalibration(playerId)` → `bool` | skip the minute and a half of calibrating if this player already did it |
| **4. Otherwise calibrate** | `await CalibrateAll(prompt)` → `bool` | asks the player for each of the seven faces in turn |
| **5. Save it** | `SaveCalibration(playerId)` → `bool` | so next time step 3 succeeds |
| **6. Predict, again and again** | `await Predict()` or `await TryPredict()` | |
| **7. Show or keep results** *(optional)* | `GetResultsSummary()` | a text summary of the session |

Good to know:

* All the `await` calls must run from the main thread inside an `async` method: use
  `async void Start()` or `async Awaitable`, as in the recipes. Do not block with `.Wait()`.
* Your script's `Start` may run before the predictor's `Start`. That is fine: the first prediction
  waits one frame for it, and the calibration calls above work before it too.
* One prediction at a time. A second call while one is running returns a `Busy` error.
* When a call fails, **wait before trying again** (the recipes use `WaitForSecondsAsync`). Otherwise
  a wrong Jev key would send a request every frame.

---

## 8. Recipes

Each recipe is a complete script: add it to the GameObject that has **New Face Au Model**.

### Recipe 1 — SparseVr, no calibration

The simplest setup. Nothing to configure: SparseVr is the default.

```csharp
using UnityEngine;

[RequireComponent(typeof(NewFaceAuModel))]
public class SparseVrExample : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        // 1. Choose the backend. SparseVr is already the default, so this line is optional.
        predictor.UseSparseVr();

        _ = Run();
    }

    private async Awaitable Run()
    {
        // 2. Predict, again and again.
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
```

**What comes back:** `TryPredict()` returns a `PredictionOutcome`.
`outcome.Ok` is true and `outcome.Prediction` holds the result, or `outcome.Ok` is false and
`outcome.Error.Value` says why (see [Return types](#10-return-types-in-detail)).
`prediction.Confidence` is the model's probability for the emotion it chose.

### Recipe 2 — SparseVr **with calibration**

The order: **choose backend → calibration on → load a saved one, otherwise calibrate → save → predict.**

```csharp
using UnityEngine;

[RequireComponent(typeof(NewFaceAuModel))]
public class SparseVrCalibratedExample : MonoBehaviour
{
    [SerializeField] private string playerId = "player1";

    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        // 1. Choose the backend first: a calibration belongs to the backend that made it.
        predictor.UseSparseVr();

        // 2. Switch calibration on before you load or start one.
        predictor.UseCalibration = true;

        _ = Run();
    }

    private async Awaitable Run()
    {
        // 3. Reuse this player's saved calibration if there is one...
        if (!predictor.LoadCalibration(playerId))
        {
            // 4. ...otherwise calibrate: seven emotions, about ten seconds each.
            bool calibrated = await predictor.CalibrateAll(ShowPrompt);

            if (calibrated)
            {
                // 5. Keep it for next time.
                predictor.SaveCalibration(playerId);
            }
            else
            {
                Debug.LogWarning($"Calibration failed: {predictor.CalibrationError}");
            }
        }

        // 6. Predict. If calibration failed, this is simply the uncalibrated answer.
        while (!destroyCancellationToken.IsCancellationRequested)
        {
            var (emotion, confidence) = await predictor.Predict();
            Debug.Log($"{emotion} ({confidence:F2}), calibrated: {predictor.IsCalibrated}");
        }
    }

    private static async Awaitable ShowPrompt(Emotion emotion)
    {
        Debug.Log($"Make a {emotion} face and hold it.");
        await Awaitable.WaitForSecondsAsync(2f);
    }
}
```

**What to expect**

1. First run: `LoadCalibration` returns `false` (nothing saved yet), so `CalibrateAll` runs. For
   each emotion the player is prompted, then held for about 10 seconds (ten samples, one a second;
   the first is dropped). About a minute and a half in all, including your 2-second prompts.
2. `CalibrateAll` returns `true` when every emotion was collected, so the calibration is saved.
   It returns `false` if face tracking was lost for too long: `CalibrationError` says why, and you
   can simply run it again.
3. Next run: `LoadCalibration` returns `true` and the whole calibration step is skipped.
4. `Predict()` now returns the **calibrated** emotion. `IsCalibrated` is `true`. The confidence is
   still the model's own probability for the emotion returned.

### Recipe 3 — Jev, no calibration

Jev needs your own API key, **passed as a parameter**. The package never stores or reads a key on
its own.

```csharp
using UnityEngine;
using VcuSwimLab.Backends.Jev;

[RequireComponent(typeof(NewFaceAuModel))]
public class JevExample : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        // 1. Choose the backend and pass YOUR OWN key as a parameter.
        //    Editor and desktop: read it from the TYPESAFE_API_KEY environment variable.
        //    On a headset there are no environment variables: load the key from your own
        //    server or secure storage and call new JevOptions(key) instead.
        predictor.UseJev(JevOptions.FromEnvironment());

        // 2. Optional: hear about every failure (no network, wrong key, ...).
        predictor.PredictionFailed += error => Debug.LogWarning(error);

        _ = Run();
    }

    private async Awaitable Run()
    {
        // 3. Predict. A failed call returns (Neutral, 0): check LastError, and wait before
        //    trying again so that a wrong key does not send a request every frame.
        while (!destroyCancellationToken.IsCancellationRequested)
        {
            var (emotion, confidence) = await predictor.Predict();

            if (predictor.LastError.HasValue)
            {
                await Awaitable.WaitForSecondsAsync(2f);
                continue;
            }

            Debug.Log($"{emotion} ({confidence:F2}) from {predictor.ActiveBackendId}");
        }
    }
}
```

**What comes back:** the same `(Emotion, float)`. The confidence is Jev's *own* answer for the
emotion it chose (Jev answers each emotion separately, so its seven answers do not add up to 1).
`predictor.LastError` is `null` after a success and holds the latest failure otherwise.
**If the key is wrong** you get `(Neutral, 0)` and a `PredictionFailed` event with code
`Unauthorized`. Bad settings are caught immediately, not on the first prediction: an empty key throws
`ArgumentException` at `new JevOptions(...)`, and a `BaseUrl` that is not `https` (or any other bad
option) throws `ArgumentException` at `UseJev`.

### Recipe 4 — Jev **with calibration**

The order: **choose Jev (with your key) → calibration on → load a saved one, otherwise calibrate →
save → predict.** Same shape as Recipe 2; the difference is the backend, and that calibrating takes
a network request per sample.

```csharp
using UnityEngine;
using VcuSwimLab.Backends.Jev;

[RequireComponent(typeof(NewFaceAuModel))]
public class JevCalibratedExample : MonoBehaviour
{
    [SerializeField] private string playerId = "player1";

    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();
        predictor.PredictionFailed += error => Debug.LogWarning(error);

        // 1. Choose the backend first, with your own key. Choosing a different backend later
        //    clears any calibration, because a calibration belongs to the backend that made it.
        var options = JevOptions.FromEnvironment();
        // options.SendOwnFacesInRequest = true;   // optional: also tell Jev how this player makes each face
        predictor.UseJev(options);

        // 2. Switch calibration on before you load or start one.
        predictor.UseCalibration = true;

        _ = Run();
    }

    private async Awaitable Run()
    {
        // 3. Reuse this player's saved Jev calibration if there is one...
        if (!predictor.LoadCalibration(playerId))
        {
            // 4. ...otherwise calibrate. Every sample is one request to Jev:
            //    about 70 requests, around a minute and a half.
            bool calibrated = await predictor.CalibrateAll(ShowPrompt);

            if (calibrated)
            {
                // 5. Keep it for next time.
                predictor.SaveCalibration(playerId);
            }
            else
            {
                Debug.LogWarning($"Calibration failed: {predictor.CalibrationError} {predictor.LastError}");
            }
        }

        // 6. Predict, waiting a little after a failure.
        while (!destroyCancellationToken.IsCancellationRequested)
        {
            var (emotion, confidence) = await predictor.Predict();

            if (predictor.LastError.HasValue)
            {
                await Awaitable.WaitForSecondsAsync(2f);
                continue;
            }

            Debug.Log($"{emotion} ({confidence:F2}), calibrated: {predictor.IsCalibrated}");
        }
    }

    private static async Awaitable ShowPrompt(Emotion emotion)
    {
        Debug.Log($"Make a {emotion} face and hold it.");
        await Awaitable.WaitForSecondsAsync(2f);
    }
}
```

**What to expect**

1. `UseJev` first. If you called `UseSparseVr` earlier, its calibration is cleared when you switch,
   because it was made with different scores. A calibration saved with SparseVr is refused by Jev
   (and the other way round): `LoadCalibration` returns `false` and `CalibrationError` ends with
   "calibrate again".
2. First run: about **70 requests** to Jev and around a minute and a half. Keep the player's face
   in view and make sure the headset is online. A request that fails does not count as a sample
   and is tried again; `CalibrateAll` returns `false` once too many have failed (20 attempts per
   emotion by default).
3. Then it behaves like Recipe 2: the calibrated emotion comes back from `Predict()`, and the
   confidence is Jev's own answer for it.
4. Optional: `options.SendOwnFacesInRequest = true` also tells Jev how this player makes each face
   (see [Jev in depth](#12-jev-in-depth)). It is off by default.

### Recipe 5 — Compare two backends on the same face

Useful for deciding which backend suits your game. The second backend is created but **not
selected**; the predictor keeps using SparseVr.

```csharp
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
```

`ScoreWith` returns a `BackendResult`: `Ok`, `Scores` (seven values in `Emotion` order) and `Error`.
`ScoreValidation.Argmax` gives the index of the highest score, or `-1` if the scores are unusable.
The `Samples~/CalibrationCheck` sample does this for calibrated predictions too.

### Recipe 6 — Handle failures

Two ways to hear about problems: look at each outcome, and/or subscribe to the event.

```csharp
using UnityEngine;
using VcuSwimLab.Backends;

[RequireComponent(typeof(NewFaceAuModel))]
public class FailureHandlingExample : MonoBehaviour
{
    private NewFaceAuModel predictor;

    void Start()
    {
        predictor = GetComponent<NewFaceAuModel>();

        // Raised for every failed prediction, whichever backend is active.
        predictor.PredictionFailed += OnPredictionFailed;

        _ = Run();
    }

    void OnDestroy()
    {
        predictor.PredictionFailed -= OnPredictionFailed;
    }

    private async Awaitable Run()
    {
        while (!destroyCancellationToken.IsCancellationRequested)
        {
            PredictionOutcome outcome = await predictor.TryPredict();

            if (outcome.Ok)
            {
                Debug.Log($"{outcome.Prediction.Emotion} ({outcome.Prediction.Confidence:F2})");
                continue;
            }

            PredictionError error = outcome.Error.Value;

            if (error.Code == PredictionErrorCode.Unauthorized)
            {
                Debug.LogError("The Typesafe key was rejected. Ask for a new one.");
                return;
            }

            await Awaitable.WaitForSecondsAsync(error.IsTransient ? 2f : 0.5f);
        }
    }

    private void OnPredictionFailed(PredictionError error)
    {
        Debug.LogWarning($"{error.Code}: {error.Message}");
    }
}
```

---

## 9. Function reference

Everything below is on the `NewFaceAuModel` component unless it says otherwise.
**Await?** = it returns an `Awaitable` you must `await` from an `async` method.

### Choosing a backend

| Call | Await? | Returns | Notes |
|---|---|---|---|
| `UseSparseVr()` | no | `void` | needs a **Face Model** assigned, otherwise throws `InvalidOperationException`. Already the default |
| `UseJev(JevOptions options)` | no | `void` | validates the options now; throws `ArgumentNullException` or `ArgumentException` (bad key or URL) |
| `UseBackend(IEmotionBackend backend)` | no | `void` | your own backend; the predictor disposes it when it is replaced |
| `CreateJevBackend(JevOptions options)` | no | `IEmotionBackend` | creates a Jev backend **without selecting it** (for `ScoreWith`) |
| `ActiveBackendId` | — | `string` | `null` until a backend is selected. For SparseVr it is the model's name, for Jev e.g. `jev:jev-1.13.0:p1` |

### Predicting

| Call | Await? | Returns | On failure |
|---|---|---|---|
| `TryPredict(float timeoutSeconds = 3, bool saveResult = true)` | yes | `PredictionOutcome` | `outcome.Ok == false`, `outcome.Error` set. **The most informative call** |
| `PredictDetailed(float timeoutSeconds = 3, bool saveResult = true)` | yes | `FacePrediction?` | `null` |
| `Predict()` | yes | `(Emotion, float)` | `(Neutral, 0f)` |
| `PredictRaw()` | yes | `Tensor<float>` | `null`. The SparseVr model's raw output (1×7); you must `Dispose()` it. Returns `null` when the SparseVr model is not loaded (for example when you chose Jev from the start), while another prediction is running, or on a timeout |
| `ScoreWith(IEmotionBackend backend, float[][] window)` | yes | `BackendResult` | `Ok == false`; scores another backend on a window you already have |

`timeoutSeconds` is how long to wait for five valid readings of the face; it must be above 0 or
an `ArgumentOutOfRangeException` is thrown. `saveResult: false` keeps a prediction out of the
results file.

### Calibration

| Call | Await? | Returns | Notes |
|---|---|---|---|
| `UseCalibration` (property) | — | `bool`, settable | the **Use Calibration** checkbox. While `false`, the calls below do not calibrate (`CalibrationError` says so) |
| `IsCalibrated` | — | `bool` | `true` when `Predict` is using a calibration |
| `CalibrationError` | — | `string` | why the last calibration, load or save failed; empty or `null` if nothing failed |
| `CalibrateAll(Func<Emotion, Awaitable> prompt = null, float secondsBetweenSamples = 1, int maxAttempts = 20)` | yes | `bool` | calibrates all seven emotions then finishes. `true` = ready. Call it again to start over |
| `StartCalibration()` | no | `void` | begins a new calibration, forgetting any earlier one (for step-by-step control) |
| `CalibrateEmotion(Emotion emotion, float secondsBetweenSamples = 1, int maxAttempts = 20)` | yes | `bool` | collects one emotion (10 samples). `false` if tracking was lost for too long, or the backend changed meanwhile |
| `FinishCalibration()` | no | `bool` | builds it from the seven emotions. `false` if one is missing; see `CalibrationError` |
| `ClearCalibration()` | no | `void` | stop using calibration |
| `CalibratedEmotion(FacePrediction)` | no | `Emotion?` | the calibrated answer for a prediction, or `null` if not calibrated |
| `CalibratedDecision(FacePrediction)` | no | `NearestCentroid?` | the same with its similarity scores, for detailed logs |
| `BeginCalibrationEmotion(Emotion)` / `AddCalibrationSample(int position, FacePrediction?)` | no | `void` / `bool` | advanced: your own fixed-clock protocol (position 0 is dropped, a missing tick is not retried) |
| `CalibrationSampleCounts()` | no | `int[]` | samples kept per emotion, or `null` before `StartCalibration` |

Calling `CalibrateEmotion`, `BeginCalibrationEmotion` or `AddCalibrationSample` with calibration
**on** but before `StartCalibration` is a programming mistake and throws `InvalidOperationException`.

### Saved calibrations (one file per player, on the device)

| Call | Returns | Notes |
|---|---|---|
| `SaveCalibration(string playerId)` | `bool` | `false` if there is nothing finished to save, or the file could not be written (see `CalibrationError`) |
| `LoadCalibration(string playerId)` | `bool` | `false` if there is no file, it is damaged, **or it was made with a different backend or model** |
| `HasSavedCalibration(string playerId)` | `bool` | |
| `DeleteSavedCalibration(string playerId)` | `void` | |
| `NewFaceAuModel.CalibrationPath(string playerId)` (static) | `string` | where the file lives |

`playerId` may contain only letters, digits, `-` and `_` (anything else makes the file lookup throw `ArgumentException`)
because it becomes a file name.

### Results

| Call | Returns | Notes |
|---|---|---|
| `GetResultsSummary()` | `string` | e.g. `120 predictions over 2.1 min` and the share of each emotion |
| `ResultsFilePath` | `string` | this session's CSV file, or `null` before the first saved prediction |
| `NewFaceAuModel.ResultsFolder` (static) | `string` | where all session files are |
| `NewFaceAuModel.DeleteSavedResults()` (static) | `void` | deletes every results file on the device |

### Events and diagnostics

| Member | Type | Notes |
|---|---|---|
| `PredictionFailed` | `event Action<PredictionError>` | raised for every failed prediction |
| `LastError` | `PredictionError?` | the most recent failure; `null` after a success |
| `FaceModelName` | `string` | the assigned model's name |
| `NewFaceAuModel.WindowFrames` / `FeaturesPerFrame` / `DefaultTimeoutSeconds` | `int` / `int` / `float` | 5, 70, 3 |

---

## 10. Return types in detail

### `FacePrediction` (a struct)

| Field | Type | Meaning |
|---|---|---|
| `Emotion` | `Emotion` | the winning emotion |
| `Confidence` | `float` | the backend's own answer for `Emotion`, 0 to 1 |
| `Scores` | `float[7]` | one score per emotion, in this order: Anger, Disgust, Fear, Happiness, Neutral, Sadness, Surprise |
| `Raw` | `float[7]` | Jev's answers before they were divided by their sum; `null` for SparseVr |
| `Window` | `float[5][70]` | a copy of the face data the backend saw (log it to replay the prediction offline) |
| `Generation` | `long` | how many valid readings had arrived; a smaller-than-expected step between two predictions means tracking dropped out |
| `BackendId` | `string` | which backend produced it |
| `ScoreOf(Emotion)` | `float` | the answer for one emotion (`Raw` if there is one, otherwise `Scores`) |

### `PredictionOutcome` (returned by `TryPredict`)

| Member | Type | Meaning |
|---|---|---|
| `Ok` | `bool` | true when there is a prediction |
| `Prediction` | `FacePrediction` | valid when `Ok` |
| `PredictionOrNull` | `FacePrediction?` | the prediction, or `null` |
| `Error` | `PredictionError?` | set when `Ok` is false |

### `PredictionError` and its codes

`error.Code` (a `PredictionErrorCode`), `error.Message` (for humans), `error.BackendId`,
`error.IsTransient` (true when trying again later is reasonable), `error.ToString()`.

| Code | What happened | What to do |
|---|---|---|
| `NotConfigured` | no backend selected | assign a **Face Model**, or call `UseJev` / `UseBackend` |
| `NotReady` | the predictor has not started, or is disabled | add it to an active GameObject |
| `Busy` | another prediction is running | wait for it |
| `NoTracking` | no valid face data within the timeout | the headset is not tracking the face; check the settings in [Before you start](#2-before-you-start) |
| `InvalidInput` | the window was unusable | usually a bug in a custom caller |
| `Unauthorized` | Jev rejected the key | give a valid key; not transient |
| `InvalidRequest` | Jev rejected the request | wrong model name or URL |
| `RateLimited` | too many requests | slow down; transient |
| `Timeout` | no answer in time | transient; check the network |
| `Unreachable` | no connection | the headset is offline; transient |
| `ServerError` | the service failed | try later; transient |
| `InvalidResponse` | the answer could not be used | wrong number of scores, NaN, or a malformed reply |
| `Internal` | an unexpected exception | a bug in a backend; the message names it |

### Smaller types

* `NearestCentroid` (from `CalibratedDecision`): `ClassIndex` (the emotion's index),
  `WinnerSimilarity`, `RunnerUpSimilarity`, `Margin` (how far ahead the winner is).
* `BackendResult`: `Ok`, `Scores`, `Raw`, `Error`, `ReferenceFacesSent`.
* `Emotion`: `Anger = 0, Disgust, Fear, Happiness, Neutral, Sadness, Surprise = 6`.

---

## 11. Calibration in depth

**Why:** one person's "surprised" is another person's "fear". Calibration records the player's own
version of each emotion, then compares every new prediction with it.

**What the player does:** for each of the seven emotions, make that face and hold it for about
ten seconds. Show them what to do: the `prompt` you pass to `CalibrateAll` runs before each
emotion, so display your own message there.

**How long:** about 70 seconds plus your prompts. With Jev every sample is one request.

**How it decides:** for each emotion it adds two similarities, how close the backend's scores are
to this player's scores for that emotion, and how close the face movements are to this player's face
for that emotion. The highest sum wins. The confidence is still the backend's own answer.

**It follows the backend.** Calibration is collected and used with whichever backend is active.
* A saved calibration remembers which backend made it. Loading it with another backend returns
  `false` ("…calibrate again").
* Changing backend clears the current calibration.

**Where it is kept:** only on the device, in `Application.persistentDataPath/VREmotionDetection/calibration/<playerId>.json`.
It describes one person's face, so never put it in your project or a repository.

**If it fails:** `CalibrateAll` returns `false` and `CalibrationError` explains. Predictions still
work, uncalibrated. To try again call `CalibrateAll` again (it starts over).

**Step by step, if you need your own flow:** `StartCalibration()` → `await CalibrateEmotion(e)` for
each emotion → `FinishCalibration()` → `SaveCalibration(id)`. An emotion can be collected once per
`StartCalibration()`: calling `CalibrateEmotion` for it again throws `InvalidOperationException`, so to
redo one, start over.

A resting face has little shape, so Neutral benefits least from calibration.

---

## 12. Jev in depth

```csharp
var options = new JevOptions(apiKey);              // required
options.Model = "jev-1.13.0";                      // default; pin a versioned model
options.TimeoutSeconds = 10;                       // per attempt
options.MaxRetries = 2;                            // transient failures only
options.SendOwnFacesInRequest = false;             // default
options.LogRequests = false;                       // true: one Console line per request
predictor.UseJev(options);
```

| Option | Default | Rule |
|---|---|---|
| `ApiKey` (constructor) | none | required; no spaces or non-ASCII characters |
| `BaseUrl` | `https://api.typesafe.ai` | an `https` URL without credentials, query or fragment (`http` only for localhost) |
| `Model` | `jev-1.13.0` | not empty |
| `TimeoutSeconds` | 10 | above 0, at most 120 |
| `MaxRetries` | 2 | 0 to 5 |
| `SendOwnFacesInRequest` | `false` | also sends the player's calibration summary |
| `LogRequests` | `false` | writes one Console line for every request the package sends |

* **Your key.** The package never ships, stores or logs a key, and it is never written into a
  Unity asset. `JevOptions.FromEnvironment()` reads `TYPESAFE_API_KEY` (Editor and desktop only).
  **A key inside a shipped build can be extracted**: for a public release, set `BaseUrl` to your
  own server, which adds the key and forwards the request to Typesafe.
* **What is sent:** a short description of the face (strongest movements, as words) and, with
  `SendOwnFacesInRequest`, how similar the face is to each of this player's calibrated faces. The
  player's face data leaves the device: **get their consent**.
* **Speed:** each prediction is one request. If the network is down, a failing call can take about
  30 seconds with the defaults (three tries of up to 10 s); lower `TimeoutSeconds` or `MaxRetries`
  if you need a faster verdict.
* **Cost:** Typesafe bills per input token (about $0.042 per million at the time of writing).

**Seeing the requests.** With `options.LogRequests = true` every request leaves one Console line.
The key and the face data are never printed:

```
[Jev] POST https://api.typesafe.ai/v1/systemone -> 200 in 412 ms (attempt 1 of 3)
```

Nothing is sent unless the headset delivers a valid face window: without face tracking you get
`NoTracking` and no `[Jev]` line.

More: [`Documentation~/Jev.md`](./Documentation~/Jev.md).

---

## 13. Results files

With **Save Results** on (the default) every prediction is appended to a CSV file on the headset:

| time_utc | seconds | emotion | confidence | calibrated | model_emotion |
|---|---|---|---|---|---|
| 2026-10-12T14:03:05.120Z | 12.004 | Happiness | 0.8123 | yes | Surprise |
| 2026-10-12T14:03:06.131Z | 13.015 | no_tracking | | | |
| 2026-10-12T14:03:07.140Z | 14.024 | backend_error | | | |

* `emotion` is what your game received. `model_emotion` is the backend's own answer; they differ
  only when calibration changed it. `calibrated` is `yes` or `no`.
* A prediction that failed because tracking was lost is a `no_tracking` row; any other failure (the
  backend failed or answered with something unusable) is a `backend_error` row.
* One file per predictor per run of the app, named `session_<date>_<time>.csv` (UTC). The file does
  not say who played: note which session belongs to whom. Face-tracking values are never saved.

**Where:** on the headset `/sdcard/Android/data/<your package name>/files/VREmotionDetection/results/`;
in the Editor `Application.persistentDataPath/VREmotionDetection/results/`.

**Copy them to your PC:** connect the headset by USB (developer mode on, the only device
connected), put it on and choose **Allow USB debugging**, then in Unity choose
**VR Emotion → Get Results From Headset**. The files appear in `<project>/HeadsetResults/`, which gets
its own `.gitignore` because the files describe real people. Needs Unity's Android Build Support.

| Message | What to do |
|---|---|
| No headset found | check the cable, that the headset is on and in developer mode |
| The headset has not allowed this computer | put it on and choose **Allow** |
| More than one device is connected | disconnect the others |
| There are no results on the headset yet | run a session with **Save Results** on |
| Could not find adb | Unity Hub → Installs → Add modules → Android Build Support |

`NewFaceAuModel.DeleteSavedResults()` deletes the files from the headset.

---

## 14. Write your own backend

A backend turns a window of face data into seven scores. Implement `IEmotionBackend`
(`VcuSwimLab.Backends`) and pass it to `UseBackend`:

```csharp
using UnityEngine;
using VcuSwimLab.Backends;

public sealed class AlwaysNeutralBackend : IEmotionBackend
{
    public string Id => "always-neutral";

    public async Awaitable<BackendResult> ScoreAsync(BackendRequest request)
    {
        if (!WindowValidation.TryValidate(request.Window, 1, 63, out string problem))
        {
            return BackendResult.Failure(PredictionErrorCode.InvalidInput, problem, Id);
        }

        var scores = new float[request.ClassNames.Count];
        scores[(int)Emotion.Neutral] = 1f;
        return BackendResult.Success(scores);
    }

    public float[] PrepareForCalibration(float[] scores) => scores;

    public void Dispose()
    {
    }
}
```

```csharp
predictor.UseBackend(new AlwaysNeutralBackend());
```

Return errors with `BackendResult.Failure(code, message, Id)` instead of throwing. `Id` is saved
inside calibrations, so change it when a change would make old calibrations meaningless. Details:
[`Documentation~/Backends.md`](./Documentation~/Backends.md).

---

## 15. Troubleshooting

| What you see | Likely cause | Fix |
|---|---|---|
| Always `(Neutral, 0)`; `LastError` is `NoTracking` | the headset is not delivering face data | set **Face Tracking Support** to Required/Supported, accept the permission on the headset, use a Quest Pro, check the face is visible |
| `NotConfigured` | no **Face Model** and no `UseJev` | assign the model, or call `UseJev` |
| `NotReady` | the component is disabled or on an inactive object | enable it |
| `Unauthorized` | wrong, missing or expired Typesafe key | pass a valid key to `new JevOptions(...)` |
| `Unreachable` / `Timeout` on a Quest | no internet, or Internet Access is not `Require` | enable it in Player Settings; check Wi-Fi |
| No Jev requests seem to happen | no face data (`NoTracking`), or the key was rejected | set `options.LogRequests = true` and read the `[Jev]` lines (see [Jev in depth](#12-jev-in-depth)) |
| `new JevOptions(...)` or `UseJev` throws `ArgumentException` | empty key, or a setting such as `BaseUrl` (must be https) is invalid | fix the value; the message lists every problem |
| `LoadCalibration` returns `false` | no file, damaged file, **or made with another backend/model** | read `CalibrationError`; calibrate again |
| `CalibrateAll` returns `false` | tracking lost, or too many Jev failures | read `CalibrationError` and `LastError`; run it again |
| Calibration calls seem to do nothing | **Use Calibration** is off | set `UseCalibration = true` *before* loading or starting |
| `ArgumentException` about `playerId` | a space, slash or other character | use letters, digits, `-` and `_` only |
| `Busy` | two predictions at once | call from one loop; await each call |
| No results file | **Save Results** is off, or no prediction yet | tick it; check `ResultsFilePath` |
| The game freezes in a prediction loop | a loop that retries instantly after a failure | wait after failures, as in the recipes |

---

## 16. More

* **Documentation** — [`Documentation~/`](./Documentation~/README.md): architecture, backends, Jev,
  calibration, error codes, data formats and design notes.
* **Samples** — import from the Package Manager: *Basic Usage* (Recipe 1), *Movement Calibration*
  (Recipe 2), *Calibration Check*, *Jev Prediction* (Recipe 3).
* **Older code** — `FaceAuModel` (the original two-model predictor) is obsolete; use
  `NewFaceAuModel`. Models no longer used are kept in `Archive~/`.

## License

This project is licensed under the [MIT License](./LICENSE).

© 2026 Virginia Commonwealth University, All rights reserved.

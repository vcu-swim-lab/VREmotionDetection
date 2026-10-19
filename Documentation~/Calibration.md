# Calibration

## Reusing a player's calibration

After a successful calibration, call `SaveCalibration(playerId)`. In a later session, choose the same model, enable calibration, and call `LoadCalibration(playerId)`. If a compatible profile exists in this app's storage on the device, the player does not need to repeat the expressions. Otherwise, collect and save a new calibration. Profiles do not automatically transfer to another device.

This stores personal reference information, not newly trained model weights. Allow approximately 70 seconds plus prompts for the local model; tracking loss can add time. Jev also depends on request latency and retries, so its duration is not fixed.

People make the same emotion with different faces. Calibration learns how *this* player makes each emotion, then compares every prediction with the player's own faces. It is **off by default**: without it `Predict` returns the backend's own answer, and the calibration methods do not calibrate: they return `false` (or nothing) and `CalibrationError` says that calibration is off.

## Using it

```csharp
predictor.UseCalibration = true;                        // or tick "Use Calibration"

bool ok = await predictor.CalibrateAll(async emotion =>
{
    Debug.Log($"Make a {emotion} face and hold it.");
    await Awaitable.WaitForSecondsAsync(2f);
});

if (ok) predictor.SaveCalibration("player1");
else Debug.LogWarning(predictor.CalibrationError);
```

`CalibrateAll` is `StartCalibration`, then `CalibrateEmotion` for each emotion, then `FinishCalibration`. Use the individual calls when you need your own flow. Next time, `LoadCalibration("player1")` restores it.

| Call | Does |
|---|---|
| `StartCalibration()` | starts a new calibration, forgetting any previous one |
| `CalibrateEmotion(emotion, secondsBetweenSamples = 1, maxAttempts = 20)` | collects one emotion: 10 samples a second apart; the first is dropped; samples without a usable prediction are retried; false if `maxAttempts` run out |
| `FinishCalibration()` | builds the calibration; false when an emotion is missing, with the reason in `CalibrationError` |
| `SaveCalibration(id)` / `LoadCalibration(id)` | keep or restore a player's calibration |
| `ClearCalibration()` | stop using calibration |
| `DeleteSavedCalibration(id)` / `HasSavedCalibration(id)` | manage the saved file |
| `IsCalibrated` | whether `Predict` is using a calibration |
| `BeginCalibrationEmotion` / `AddCalibrationSample(position, prediction)` | for a study protocol that takes its own predictions on a fixed clock: position 0 is dropped and a missing tick is not retried |
| `CalibratedDecision(prediction)` | the calibrated emotion with its similarity sums, for detailed logs |

If `CalibrateEmotion` returns false, call `StartCalibration` again and redo the calibration: an emotion that already has samples cannot be collected a second time, so a partial calibration is never mixed with a new attempt.

When calibration is off, `CalibrateEmotion`, `CalibrateAll` and `LoadCalibration` return false, `BeginCalibrationEmotion` does nothing and `AddCalibrationSample` returns false.

When calibration is enabled, only the step-by-step collection calls (`CalibrateEmotion`, `BeginCalibrationEmotion`, and `AddCalibrationSample`) require a preceding `StartCalibration()` and throw `InvalidOperationException` if it is missing. `CalibrateAll()` starts a new calibration itself. `LoadCalibration()` loads an existing profile without requiring `StartCalibration()`.

## Getting a calibrated prediction

`Predict()` applies ready calibration automatically. `TryPredict()` and `PredictDetailed()` return the original model prediction, even when calibration is enabled. With a successful detailed result, use `CalibratedEmotion(prediction) ?? prediction.Emotion` to select the calibrated emotion (or fall back to the original), then `prediction.ScoreOf(emotion)` for that emotion's model score. This score is not a measure of calibration confidence.

## How it decides

For each emotion the calibration stores two averages over the player's samples: the backend's scores (as logarithms) and the 63 face-movement values. For a new prediction it adds two cosine similarities per emotion: the prediction's scores against the stored scores, and the face movements against the stored movements. The emotion with the highest sum wins. The confidence returned is still the backend's own answer for that emotion.

## Order of calls

Choose the backend first, then set `UseCalibration = true`, then load or run a calibration. The calibration calls work before the predictor's own `Start` has run (your script's `Start` may come first), and a calibration made or loaded then is kept when the predictor starts, as long as the backend that starts is the one it belongs to.

## It follows the backend

Calibration collects and classifies with whichever backend is active. A calibration made with the local model is not valid for Jev and the other way round. This is enforced:

- a saved calibration records the backend id, and `LoadCalibration` refuses a file made by another backend ("calibrate again");
- choosing a backend with a different `ActiveBackendId` (`UseLocalModel`, `UseJev` or `UseBackend`) clears the current calibration; choosing the same one again keeps it;
- if the backend changes while `CalibrateEmotion` is running, it stops and returns false.

## Where it is stored

A calibration describes one person's face. It is saved only on the device:
`Application.persistentDataPath/VREmotionDetection/calibration/<userId>.json`. It is never part of a model asset or a build, and must not be committed. `userId` may contain only letters, digits, `-` and `_`, because it becomes a file name. Writes go to a temporary file first, so a crash cannot leave a half-written calibration.

## The numbers that must not change

The calibration reproduces a published analysis (`calibration_study.py`) operation for operation, and a session recorded with other values is not a session of that study. Do not change:

| Value | Meaning |
|---|---|
| `Epsilon = 1e-12` | floor inside `log(max(score, epsilon))`; the model's softmax can underflow to 0, which would turn a whole vector into minus infinity |
| `NormFloor = 1e-12` | added to a norm before dividing, so a zero vector gives 0 and not NaN |
| `FrozenHybridAlpha = 0.40` | weight of the personal term in the hybrid blend, chosen on a validation split and then frozen; the curve is flat around it (0.40 against 0.45 differ by 0.00053), so the plateau is the finding, not the exact value |
| `FaceActionUnitCount = 63` | the first 63 values are face movements; the last 7 are tongue values and say nothing about the expression |
| ticks 1 to 9 | tick 0 is dropped because it is taken while the face is still changing |
| exactly 9 ticks per emotion | a centroid from fewer ticks is a different estimate, not a weaker one, so the calibration refuses rather than averaging fewer |
| `ImplementationVersion = "runtime-calibration-v1"` | written into logs; bump it only when a change alters a calibrated decision |
| normalise, then dot | same order of floating-point operations as the Python reference |
| strictly greater wins a tie | an exact tie goes to the lower class index, like NumPy's `argsort` on seven items |

Non-finite scores are refused, not clamped: a NaN from the model means the inference went wrong, and averaging over it would be silently meaningless.

## Limits

Scores must be probabilities (the logarithm needs them above 0). A quiet or resting face has little shape, so Neutral is the weakest class under movement calibration. Calibrating on Jev has not been shown to improve accuracy; compare with `Samples~/CalibrationCheck` on your own data.

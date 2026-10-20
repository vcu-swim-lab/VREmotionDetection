# Migrating from 2.1 to 3.0

## The one breaking change

Every public type of the package is now in the namespace `VcuSwimLab.VREmotionDetection`. Add this line to each script that uses `NewFaceAuModel`, `Emotion`, `FacePrediction`, `PredictionOutcome`, `DeviceManager` or the other runtime types:

```csharp
using VcuSwimLab.VREmotionDetection;
```

Nothing else changes: class names, serialized fields, script GUIDs and the saved files stay the same, so scenes and prefabs keep working (Unity finds scripts by GUID). The backend, Jev and calibration types were already in their own `VcuSwimLab.*` namespaces. Only code that looks a type up by its bare name as a string, such as `Type.GetType("NewFaceAuModel")`, needs the full name `VcuSwimLab.VREmotionDetection.NewFaceAuModel`.

## What keeps working

### Local-model naming

Use `UseLocalModel()` to select the on-device model trained on acted emotional-expression data.
`UseSparseVr()` remains a deprecated alias with identical behavior, so existing calls still compile
with an obsolete warning. Update those calls to remove the warning. This naming change does not
rename the internal `SparseVrBackend` type or the script GUIDs.

Everything in 2.1 (after that `using` line): `NewFaceAuModel` with its `Predict`, `PredictDetailed`, `PredictRaw`, the calibration calls, the results calls, `FacePrediction`, `Emotion`, `DeviceManager`, the serialized fields (`faceModel`, `useCalibration`, `saveResults`) and the saved calibration and results files. Scenes and prefabs do not need changes. A calibration saved by 2.1 is refused by 3.0 because the bundled model file was renamed, as described below.

### Bundled model file renamed

The bundled ONNX file is now `sparseVR_teacher_model_acted_only_vol-aug-k1.onnx`. Its GUID is unchanged, so scenes and prefabs that reference it need no change. The local model's backend id is the asset's file name, so a calibration saved by 2.1 is refused with “calibrate again”: calibrate again and save it.

## What is new

- Backends: `UseLocalModel()`, `UseJev(JevOptions)`, `UseBackend(IEmotionBackend)`, `ActiveBackendId`.
- Typed errors: `TryPredict()`, `LastError`, `PredictionFailed`.
- `CalibrateAll(prompt)`, `ScoreWith(backend, window)`, `CreateJevBackend(options)`.
- `FacePrediction.Raw`, `ScoreOf(emotion)`, `BackendId`.
- The Jev backend and its sample, `CalibrationStore`, `Documentation~`.

## Behaviour changes

| Change | Why |
|---|---|
| The microphone is no longer started at start-up | it was never used; it triggered a permission prompt for face-only apps |
| A prediction that overlaps another returns `Busy` | two predictions used to reset each other's window |
| `BeginCalibrationEmotion` and `AddCalibrationSample` do nothing (false) when calibration is off | the README always said so; they used to throw |
| `SaveCalibration` and `LoadCalibration` report file problems through `CalibrationError` | they used to throw on I/O errors |
| `PredictDetailed` with a timeout of 0 or less throws `ArgumentOutOfRangeException`; before start it returns `NotReady` | they used to fail with a `NullReferenceException` or time out silently |
| Failures that are not lost tracking are written as `backend_error` rows | so a service failure is not mistaken for lost tracking |
| `userId` such as `"abc\n"` is refused | the old pattern accepted a trailing newline |
| A results file name that is already taken gets a `_2` suffix | two predictors started in the same second shared one file |
| Disabling and re-enabling the component restores prediction | the model used to stay disposed |
| The package needs `com.unity.nuget.newtonsoft-json` | used by the Jev backend only |

## If you used the unreleased Jev branch

| Before | Now |
|---|---|
| `Backend = PredictionBackend.Jev` and a `JevSettings` asset | `UseJev(new JevOptions(apiKey))` |
| `JevError`, `JevProfileSent` | `LastError`, `BackendResult.ReferenceFacesSent` |
| `ScoreWithJev(window)`, `ClassifyWithJev(window)` | `ScoreWith(predictor.CreateJevBackend(options), window)` |
| key read from an asset or compiled into a script | pass the key as a parameter |
| calibration always collected with the local model | calibration follows the active backend |

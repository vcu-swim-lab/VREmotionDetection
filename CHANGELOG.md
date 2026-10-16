# Changelog

## 3.0.0 - 2026-10-17

### Breaking
- Every public type of the runtime and editor assemblies is now in the `VcuSwimLab.VREmotionDetection` namespace. Add `using VcuSwimLab.VREmotionDetection;` to your scripts. Class names, serialized fields, script GUIDs, the results CSV and the calibration files are unchanged, so scenes, prefabs and saved calibrations keep working. See `Documentation~/Migration.md`.

### Added
- Pluggable backends behind `IEmotionBackend`: the bundled SparseVr ONNX model and a new Jev (Typesafe) backend. `UseSparseVr`, `UseJev`, `UseBackend`, `ScoreWith`.
- `JevOptions`: the API key is a constructor parameter; `FromEnvironment()` is an explicit opt-in.
- Typed errors: `PredictionErrorCode`, `PredictionError`, `TryPredict`, `LastError`, `PredictionFailed`.
- Calibration follows the active backend and is tied to it when saved.
- `CalibrateAll`, `CalibrationStore` (atomic writes), `FacePrediction.Raw`/`ScoreOf`/`BackendId`.
- `backend_error` rows in the results file.
- `JevOptions.LogRequests`: one Console line per Jev request (status, time, attempt), without the key. Server text is scrubbed of the key before it reaches an error or a log.
- Documentation (`Documentation~`), a Jev sample.
- `THIRD_PARTY_NOTICES.md`, `CITATION.cff` and `CITATIONS.bib` (related papers), a privacy and limitations section in the README, and OpenUPM install instructions.

### Changed
- The first prediction waits one frame for the predictor to start, so a script's own `Start` can call `Predict` straight away.
- The code is split into five assemblies with one-way dependencies.
- `DeviceManager` creates devices on request; the microphone is no longer started at start-up.
- Concurrent predictions return `Busy`; invalid arguments throw; calibration calls are no-ops when calibration is off.
- The headset results menu reports the files it copied and refuses to guess between several devices.
- `package.json` completed (Unity 6 with `unityRelease` 33f1, contact email, changelog link, keywords, links, and the `jsonserialize` and `unitywebrequest` module dependencies; no Audio module dependency is declared, as in 2.1).
- All source comments moved into the documentation.

### Removed
- `Archive~/`, the models the library no longer uses. They stay in the Git history and in the `v2.0` tag.
- A reference in `VREmotionDetection.asmdef` to an Editor-only OpenXR assembly; no code used it.

### Fixed
- Calibration calls made before the predictor's `Start` (`LoadCalibration`, `SaveCalibration`, `StartCalibration`) work again, and the calibration survives the start.
- The results recorder reopened its file after being disposed and could lose the header.
- Re-enabling the component left a disposed model.
- `userId` validation accepted a trailing newline.
- A download that failed after the response headers arrived was read as a successful answer.
- A custom backend that threw could make `Predict` throw.
- The calibration summary was copied for every prediction; it is now built once per calibration.
- On Android the microphone was started before the permission dialog was answered.
- `CalibrateEmotion` and `CalibrateAll` returned `false` when called before the predictor's `Start`; `CalibrationError` now says why a calibration stopped.
- SparseVr accepted frames longer than 70 values; a custom backend's `Raw` array was not validated; `PredictRaw` could return `null` without yielding a frame.
- Jev: a key could survive in an error message when the text was cut before it was scrubbed; `BaseUrl` accepted credentials and query strings; long JSON error messages were not cut to 200 characters.
- `NotConfigured` answers are no longer written to the results file.

## 2.1.0
- Acted-data model, optional movement calibration, results saving.

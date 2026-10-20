# Changelog

## 3.0.0 - 2026-10-20

### Breaking
- Every public type of the runtime and editor assemblies is now in the `VcuSeCenter.VREmotionDetection` namespace. Add `using VcuSeCenter.VREmotionDetection;` to your scripts. Class names, serialized fields, script GUIDs, the results CSV and the calibration files are unchanged, so scenes and prefabs keep working. A calibration saved by 2.1 must be redone, because the bundled model was replaced (see Changed). See `Documentation~/Migration.md`.
- The calibration types and their assembly are now `VcuSeCenter.Calibration`. Scripts and assembly definitions that used 2.1's calibration types must use this namespace and assembly name. The new backend assemblies are `VcuSeCenter.Backends`, `VcuSeCenter.Backends.SparseVr` and `VcuSeCenter.Backends.Jev`.

### Added
- Pluggable backends behind `IEmotionBackend`: the bundled local ONNX model and a new Jev (Typesafe) backend. `UseLocalModel` (`UseSparseVr` stays as a deprecated alias), `UseJev`, `UseBackend`, `ScoreWith`.
- `JevOptions`: the API key is a constructor parameter; `FromEnvironment()` is an explicit opt-in.
- Typed errors: `PredictionErrorCode`, `PredictionError`, `TryPredict`, `LastError`, `PredictionFailed`.
- Calibration follows the active backend and is tied to it when saved.
- `CalibrateAll`, `CalibrationStore` (atomic writes), `FacePrediction.Raw`/`ScoreOf`/`BackendId`.
- `backend_error` rows in the results file.
- `JevOptions.LogRequests`: one Console line per Jev request (status, time, attempt), without the key. Server text is scrubbed of the key before it reaches an error or a log.
- Documentation (`Documentation~`), a Jev sample.
- `PredictionErrorCode.Canceled`, `ICancelableBackend` and `ICancelableHttpTransport`: a prediction that is still waiting when the backend changes or the component is disabled ends with `Canceled`, and Jev aborts its request and retries.
- `JevOptions.IncludeServerDetails` (off by default): adds the service's own error text to messages, cleaned and cut to 200 characters.
- `THIRD_PARTY_NOTICES.md`, `CITATION.cff` and `CITATIONS.bib` (related papers), a privacy and limitations section in the README, and OpenUPM install instructions.

### Changed
- The first prediction waits one frame for the predictor to start, so a script's own `Start` can call `Predict` straight away.
- The code is split into five assemblies with one-way dependencies.
- `DeviceManager` creates devices on request; the microphone is no longer started at start-up.
- Concurrent predictions return `Busy`; invalid arguments throw; calibration calls are no-ops when calibration is off.
- The headset results menu reports the files it copied and refuses to guess between several devices.
- `package.json` completed (Unity 6 with `unityRelease` 33f1, contact email, changelog link, keywords, links, and the `jsonserialize` and `unitywebrequest` module dependencies; no Audio module dependency is declared, as in 2.1).
- All source comments moved into the documentation.
- The bundled model is replaced by `sparseVR_teacher_model_acted_only_vol-aug-k1_v2_seed42.onnx`, the same MoE–TCN recipe (volume augmentation k1, seed 42) trained only on the lab's own acted-expression recordings. Its input and output are unchanged. It keeps the GUID of 2.1's model, so scenes and prefabs still find it, but the local model's backend id is its file name, so a calibration saved by 2.1 is refused (“calibrate again”) and must be redone. Its predictions can differ from 2.1's.
- The package author is now the VCU SE Center; `CITATIONS.bib` lists the lab's own papers and the Typesafe sources Jev is built on.
- The bundled local model is described as the acted-data-trained MoE–TCN model based on Rahman et al.'s architecture; the remaining user-facing “SparseVr” wording in `CITATION.cff` and `THIRD_PARTY_NOTICES.md` now says “local model”.
- The README is shorter, with complete examples and reference material in `Documentation~/UsageGuide.md`; saved-calibration reuse and timing are clarified, and the overview is renamed “How it works.”

### Behaviour
- Error messages for failed Jev replies no longer copy the service's text unless `IncludeServerDetails` is on.
- After a backend switch the next prediction is not refused as `Busy` because of the old one.
- A Jev answer above 1 (or below 0, or not a number) is an `InvalidResponse`.

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
- A prediction could return a success from the previous backend after you switched backend or disabled the component, keep retrying Jev requests, and let an old call clear a newer call's busy state.
- Calibration noticed a backend switch or a disable only if it landed inside a prediction; it now stops at any point with an explanatory `CalibrationError`, and a stopped calibration no longer writes its error onto a newer one.
- A `NotConfigured` or `Busy` answer that was overtaken by choosing a backend was still reported afterwards; a throwing `CancelPending` on a custom backend could skip disposing the predictor's resources; a SparseVr worker created before the component was ever enabled was never disposed.
- Jev accepted answers above 1, so the confidence could exceed 1.
- Jev error text from the service was not bounded and control characters and `Bearer` values were not removed.
- Jev: a key could survive in an error message when the text was cut before it was scrubbed; `BaseUrl` accepted credentials and query strings; long JSON error messages were not cut to 200 characters.
- `NotConfigured` answers are no longer written to the results file.
- Face sampling and the spacing of calibration samples follow real time, so pausing or slowing the game (`Time.timeScale`) no longer stops or slows face tracking or stalls a calibration.
- Setting `UseCalibration` to `false` while a calibration is running now stops it: it returns `false`, `CalibrationError` says why, and a Jev request or retry delay of that calibration is aborted. A finished calibration and saved files are kept.
- `CalibrateAll` validates `secondsBetweenSamples` and `maxAttempts` before it touches anything, like `CalibrateEmotion`; a rejected call no longer discards an existing calibration, and `NaN` or infinite intervals are refused by both.
- `FaceAuModel` and `SoundModel` dispose the input tensors they create; `DeviceManager` releases its devices when it is disabled or destroyed, and the sound device stops the microphone (and a startup that is still waiting).

## 2.1.0
- Acted-data model, optional movement calibration, results saving.
